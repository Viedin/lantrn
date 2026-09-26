using System.Text.RegularExpressions;
using Lantrn.Infra;
using Lantrn.Services.Ingestion;
using Lantrn.Services.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lantrn.Services;

/// <summary>
/// The library: collections, the full markdown of every ingested document and its tags.
/// Changes that also touch the vectors go through here, so the database and Qdrant stay in step.
/// </summary>
public sealed partial class DocumentStore(
    IDbContextFactory<DatabaseContext> dbFactory,
    QdrantStore qdrant,
    IOptions<StorageOptions> storage,
    IHostEnvironment environment,
    ILogger<DocumentStore> logger)
{
    public const string DefaultCollection = "documents";

    private readonly string originalsPath = Path.Combine(
        Path.GetFullPath(storage.Value.DataPath, environment.ContentRootPath), "originals");

    // Files placed here are kept in sync with the default collection by DocumentFolderWatcher.
    public string FolderPath { get; } = Path.Combine(
        Path.GetFullPath(storage.Value.DataPath, environment.ContentRootPath), "documents");

    // Lowercase so names read the same in URLs, and a subset of what Qdrant accepts.
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,62}$")]
    private static partial Regex CollectionNamePattern();

    public static bool IsValidCollectionName(string name) => CollectionNamePattern().IsMatch(name);

    // "CS-Docs, drafts ,cs-docs" -> ["cs-docs", "drafts"]
    public static IReadOnlyList<string> ParseTags(string? input) =>
        (input ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.ToLowerInvariant())
            .Distinct()
            .ToList();

    // The default collection always exists: the documents folder syncs into it, so it can be cleared but not deleted.
    public async Task EnsureDefaultCollectionAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Collections.AsNoTracking().AnyAsync(c => c.Name == DefaultCollection, cancellationToken))
        {
            return;
        }

        db.Collections.Add(new Collection { Name = DefaultCollection, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created the default collection '{Collection}'", DefaultCollection);
    }

    public async Task<IReadOnlyList<CollectionSummary>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // Ordered before projecting: EF cannot translate a member of the constructed record.
        return await SummarizeCollections(db.Collections.AsNoTracking().OrderBy(c => c.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<CollectionSummary?> GetCollectionAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await SummarizeCollections(db.Collections.AsNoTracking().Where(c => c.Name == name))
            .SingleOrDefaultAsync(cancellationToken);
    }

    // The Qdrant side is created on the first push, once the embedding size is known.
    public async Task CreateCollectionAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        if (!IsValidCollectionName(name))
        {
            throw new ArgumentException(
                "Use lowercase letters, digits, '-' and '_' (starting with a letter or digit), at most 63 characters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Collections.AsNoTracking().AnyAsync(c => c.Name == name, cancellationToken))
        {
            throw new InvalidOperationException($"A collection named '{name}' already exists.");
        }

        db.Collections.Add(new Collection
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Created collection '{Collection}'", name);
    }

    public async Task DeleteCollectionAsync(string name, CancellationToken cancellationToken = default)
    {
        if (name == DefaultCollection)
        {
            throw new InvalidOperationException($"The '{DefaultCollection}' collection can be cleared but not deleted.");
        }

        await ClearCollectionAsync(name, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Collections.Where(c => c.Name == name).ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Deleted collection '{Collection}'", name);
    }

    // Removes every document and vector but keeps the collection itself.
    public async Task ClearCollectionAsync(string name, CancellationToken cancellationToken = default)
    {
        await qdrant.DeleteCollectionAsync(name, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var originals = await db.Documents
            .AsNoTracking()
            .Where(d => d.Collection == name && d.OriginalFile != null)
            .Select(d => d.OriginalFile!)
            .ToListAsync(cancellationToken);

        // Tags go with them through the cascading foreign key.
        await db.Documents.Where(d => d.Collection == name).ExecuteDeleteAsync(cancellationToken);

        foreach (var original in originals)
        {
            DeleteOriginal(original);
        }

        logger.LogInformation("Cleared the documents of collection '{Collection}'", name);
    }

    // The documents synced from the folder, by source, with the hash of the file they were embedded from.
    public async Task<Dictionary<string, FolderDocument>> ListFolderDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents
            .AsNoTracking()
            .Where(d => d.Collection == DefaultCollection && d.ContentHash != null)
            .Select(d => new FolderDocument(d.Id, d.Source, d.ContentHash!))
            .ToDictionaryAsync(d => d.Source, cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(
        string collection,
        string? tag = null,
        string? sourceFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Documents.AsNoTracking().Where(d => d.Collection == collection);
        if (!string.IsNullOrEmpty(tag))
        {
            query = query.Where(d => d.Tags.Any(t => t.Name == tag));
        }
        if (!string.IsNullOrWhiteSpace(sourceFilter))
        {
            query = query.Where(d => EF.Functions.Like(d.Source, $"%{sourceFilter.Trim()}%"));
        }

        return await query
            .OrderByDescending(d => d.IngestedAt)
            .Select(d => new DocumentSummary(
                d.Id,
                d.Source,
                d.IngestedAt,
                d.ChunkCount,
                d.Markdown.Length,
                d.Kind,
                d.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TagCount>> ListTagsAsync(string collection, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents
            .AsNoTracking()
            .Where(d => d.Collection == collection)
            .SelectMany(d => d.Tags)
            .GroupBy(t => t.Name)
            .OrderBy(g => g.Key)
            .Select(g => new TagCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }

    public async Task<LibraryOverview> GetOverviewAsync(
        int recentDocuments,
        int topTags,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var collections = await SummarizeCollections(db.Collections.AsNoTracking().OrderBy(c => c.Name))
            .ToListAsync(cancellationToken);

        var recent = await db.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.IngestedAt)
            .Take(recentDocuments)
            .Select(d => new RecentDocument(d.Id, d.Collection, d.Source, d.IngestedAt, d.ChunkCount))
            .ToListAsync(cancellationToken);

        var tags = db.Documents.AsNoTracking().SelectMany(d => d.Tags, (d, t) => new { d.Collection, t.Name });

        var distinctTags = await tags.Select(t => t.Name).Distinct().CountAsync(cancellationToken);

        // Per collection, since search filters tags within one collection at a time.
        var top = await tags
            .GroupBy(t => new { t.Collection, t.Name })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Name)
            .Take(topTags)
            .Select(g => new CollectionTagCount(g.Key.Collection, g.Key.Name, g.Count()))
            .ToListAsync(cancellationToken);

        return new LibraryOverview(collections, recent, distinctTags, top);
    }

    public async Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents.AsNoTracking().Include(d => d.Tags).SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    // Where a document lives, without loading its markdown.
    public async Task<DocumentLocation?> GetLocationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DocumentLocation(d.Collection, d.Source))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<StoredOriginal?> GetOriginalAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var fileName = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => d.OriginalFile)
            .SingleOrDefaultAsync(cancellationToken);

        if (fileName is null || !DocumentExtractor.TryGetContentType(fileName, out var contentType))
        {
            return null;
        }

        var path = Path.Combine(originalsPath, fileName);
        return File.Exists(path) ? new StoredOriginal(path, contentType) : null;
    }

    // Replaces any earlier ingest of the same source, including its points, so re-ingesting never leaves duplicates.
    // The uploaded bytes are kept as the original when given, so an OCR'd image can be shown next to its text
    // and a PDF opened in the browser's viewer.
    public async Task<StoredDocument> StoreAsync(
        string collection,
        string source,
        IngestResult result,
        IReadOnlyList<string> tags,
        byte[]? original = null,
        string? contentHash = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var document = await db.Documents
            .Include(d => d.Tags)
            .SingleOrDefaultAsync(d => d.Collection == collection && d.Source == source, cancellationToken);

        if (document is null)
        {
            document = new Document { Collection = collection, Source = source, Markdown = result.Markdown };
            db.Documents.Add(document);
        }

        document.Markdown = result.Markdown;
        document.Kind = result.Kind;
        document.ContentHash = contentHash;
        document.ChunkCount = await qdrant.ReplaceDocumentAsync(
            collection, source, document.Id, result.Kind, result.Chunks, tags, cancellationToken);
        document.IngestedAt = DateTime.UtcNow;

        var previousOriginal = document.OriginalFile;
        document.OriginalFile = original is null ? null : await SaveOriginalAsync(document.Id, source, original, cancellationToken);
        if (previousOriginal is not null && previousOriginal != document.OriginalFile)
        {
            DeleteOriginal(previousOriginal);
        }

        ApplyTags(document, tags);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Stored {Characters:N0} chars of markdown for {Source} in '{Collection}' as {DocumentId}",
            result.Markdown.Length, source, collection, document.Id);

        return new StoredDocument(document.Id, document.ChunkCount);
    }

    public async Task SetTagsAsync(Guid id, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var document = await db.Documents.Include(d => d.Tags).SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("This document no longer exists.");

        // Qdrant first: search filters on its copy, so the database must not claim tags the points lack.
        await qdrant.SetTagsAsync(document.Collection, id, tags, cancellationToken);
        ApplyTags(document, tags);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return;
        }

        await qdrant.DeleteDocumentAsync(document.Collection, id, cancellationToken);
        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        if (document.OriginalFile is not null)
        {
            DeleteOriginal(document.OriginalFile);
        }

        logger.LogInformation("Deleted {Source} from '{Collection}'", document.Source, document.Collection);
    }

    // Diffs rather than clearing, since a removed and re-added tag would share its key.
    private static void ApplyTags(Document document, IReadOnlyList<string> tags)
    {
        document.Tags.RemoveAll(t => !tags.Contains(t.Name));
        foreach (var tag in tags.Where(tag => document.Tags.All(t => t.Name != tag)))
        {
            document.Tags.Add(new DocumentTag { Name = tag });
        }
    }

    // Named by document id, so a source name never reaches the file system.
    private async Task<string> SaveOriginalAsync(Guid documentId, string source, byte[] bytes, CancellationToken cancellationToken)
    {
        var fileName = $"{documentId:N}{Path.GetExtension(source).ToLowerInvariant()}";
        Directory.CreateDirectory(originalsPath);
        await File.WriteAllBytesAsync(Path.Combine(originalsPath, fileName), bytes, cancellationToken);

        logger.LogInformation("Kept the original of {Source} ({Bytes:N0} bytes) as {FileName}", source, bytes.Length, fileName);
        return fileName;
    }

    private void DeleteOriginal(string fileName)
    {
        try
        {
            File.Delete(Path.Combine(originalsPath, fileName));
        }
        catch (IOException ex)
        {
            // The document is already gone; a stray file is harmless.
            logger.LogWarning(ex, "Could not delete the original {FileName}", fileName);
        }
    }

    private static IQueryable<CollectionSummary> SummarizeCollections(IQueryable<Collection> collections) =>
        collections.Select(c => new CollectionSummary(
            c.Name,
            c.Description,
            c.CreatedAt,
            c.Documents.Count,
            c.Documents.Sum(d => d.ChunkCount),
            c.Documents.Max(d => (DateTime?)d.IngestedAt)));
}

public sealed record CollectionSummary(
    string Name,
    string? Description,
    DateTime CreatedAt,
    int DocumentCount,
    int ChunkCount,
    DateTime? LastIngestedAt);

public sealed record DocumentSummary(
    Guid Id,
    string Source,
    DateTime IngestedAt,
    int ChunkCount,
    int Characters,
    DocumentKind Kind,
    IReadOnlyList<string> Tags);

public sealed record TagCount(string Name, int Documents);

public sealed record CollectionTagCount(string Collection, string Name, int Documents);

public sealed record RecentDocument(Guid Id, string Collection, string Source, DateTime IngestedAt, int ChunkCount);

public sealed record LibraryOverview(
    IReadOnlyList<CollectionSummary> Collections,
    IReadOnlyList<RecentDocument> RecentDocuments,
    int DistinctTags,
    IReadOnlyList<CollectionTagCount> TopTags)
{
    public int DocumentCount => Collections.Sum(c => c.DocumentCount);

    public int ChunkCount => Collections.Sum(c => c.ChunkCount);

    public DateTime? LastIngestedAt => Collections.Max(c => c.LastIngestedAt);
}

public sealed record DocumentLocation(string Collection, string Source);

public sealed record StoredOriginal(string Path, string ContentType);

public sealed record StoredDocument(Guid Id, int ChunkCount);

public sealed record FolderDocument(Guid Id, string Source, string ContentHash);

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    // Relative paths resolve against the content root.
    public string DataPath { get; set; } = "data";
}
