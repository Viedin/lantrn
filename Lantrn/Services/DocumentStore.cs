using System.Security.Claims;
using System.Text.RegularExpressions;
using Lantrn.Infra;
using Lantrn.Services.Accounts;
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
    IQdrantStore qdrant,
    IOptions<StorageOptions> storage,
    IHostEnvironment environment,
    ILogger<DocumentStore> logger)
{
    private readonly string originalsPath = Path.Combine(
        Path.GetFullPath(storage.Value.DataPath, environment.ContentRootPath), "originals");

    private readonly SemaphoreSlim storing = new(1, 1);

    // Lowercase, so names read alike wherever they are shown.
    public const string CollectionNamePattern = "^[a-z0-9][a-z0-9_-]{0,62}$";

    public const string CollectionNameRule =
        "Use lowercase letters, digits, '-' and '_' (starting with a letter or digit), at most 63 characters.";

    [GeneratedRegex(CollectionNamePattern)]
    private static partial Regex CollectionNameRegex();

    public static bool IsValidCollectionName(string name) => CollectionNameRegex().IsMatch(name);

    // "CS-Docs, drafts ,cs-docs" -> ["cs-docs", "drafts"]
    public static IReadOnlyList<string> ParseTags(string? input) =>
        (input ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.ToLowerInvariant())
            .Distinct()
            .ToList();

    public async Task<IReadOnlyList<CollectionSummary>> ListCollectionsAsync(
        ClaimsPrincipal user,
        CollectionAccess access,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // Ordered before projecting: EF cannot translate a member of the constructed record.
        return await SummarizeCollections(db, db.Collections.AsNoTracking().WhereAllowed(user, access).OrderBy(c => c.Name))
            .ToListAsync(cancellationToken);
    }

    // A collection the user may not reach reads as missing, so its id gives nothing away.
    public async Task<CollectionSummary?> GetCollectionAsync(
        Guid id,
        ClaimsPrincipal user,
        CollectionAccess access,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await SummarizeCollections(db, db.Collections.AsNoTracking().WhereAllowed(user, access).Where(c => c.Id == id))
            .SingleOrDefaultAsync(cancellationToken);
    }

    // For checks that only need a yes or no, without counting the collection's documents.
    public async Task<bool> CanAccessAsync(
        Guid id,
        ClaimsPrincipal user,
        CollectionAccess access,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Collections.AsNoTracking().WhereAllowed(user, access).AnyAsync(c => c.Id == id, cancellationToken);
    }

    // For the public API, whose keys only work for admins, who reach every collection.
    public async Task<bool> CollectionExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Collections.AsNoTracking().AnyAsync(c => c.Id == id, cancellationToken);
    }

    // The Qdrant side is created on the first push, once the embedding size is known.
    public async Task<Guid> CreateCollectionAsync(
        string name,
        string? description,
        bool isPrivate,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidCollectionName(name))
        {
            throw new ArgumentException(CollectionNameRule);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Collections.AsNoTracking().AnyAsync(c => c.OwnerId == ownerId && c.Name == name, cancellationToken))
        {
            throw new InvalidOperationException($"You already have a collection named '{name}'.");
        }

        var collection = new Collection
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsPrivate = isPrivate,
            OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow,
        };
        db.Collections.Add(collection);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Created {Visibility} collection '{Collection}' ({CollectionId})",
            isPrivate ? "private" : "public", name, collection.Id);
        return collection.Id;
    }

    public async Task SetVisibilityAsync(Guid id, bool isPrivate, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Collections
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.IsPrivate, isPrivate), cancellationToken);

        logger.LogInformation("Made collection {CollectionId} {Visibility}", id, isPrivate ? "private" : "public");
    }

    public async Task DeleteCollectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await ClearCollectionAsync(id, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Collections.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Deleted collection {CollectionId}", id);
    }

    // A removed admin's collections are the workspace's, so they and everything the admin added are handed to another admin.
    // Names are unique per owner, so a clash gets a number: "docs" becomes "docs-2".
    public async Task TransferContentAsync(string fromUserId, string toUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var taken = (await db.Collections
            .AsNoTracking()
            .Where(c => c.OwnerId == toUserId)
            .Select(c => c.Name)
            .ToListAsync(cancellationToken)).ToHashSet();

        foreach (var collection in await db.Collections.Where(c => c.OwnerId == fromUserId).ToListAsync(cancellationToken))
        {
            collection.OwnerId = toUserId;
            collection.Name = FreeName(collection.Name, taken);
            taken.Add(collection.Name);
        }
        await db.SaveChangesAsync(cancellationToken);

        await db.Documents
            .Where(d => d.AddedById == fromUserId)
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.AddedById, toUserId), cancellationToken);

        logger.LogInformation("Handed the content of {FromUserId} to {ToUserId}", fromUserId, toUserId);
    }

    private static string FreeName(string name, HashSet<string> taken)
    {
        var candidate = name;
        for (var i = 2; taken.Contains(candidate); i++)
        {
            var suffix = $"-{i}";
            candidate = name[..Math.Min(name.Length, 63 - suffix.Length)] + suffix;
        }
        return candidate;
    }

    // A removed user's content goes with them, vectors and kept originals included: the collections they own,
    // and what they added to anyone else's.
    public async Task DeleteContentOfAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var collections = await db.Collections
            .AsNoTracking()
            .Where(c => c.OwnerId == userId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in collections)
        {
            await DeleteCollectionAsync(id, cancellationToken);
        }

        var documents = await db.Documents
            .AsNoTracking()
            .Where(d => d.AddedById == userId)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in documents)
        {
            await DeleteAsync(id, cancellationToken);
        }
    }

    // Removes every document and vector but keeps the collection itself.
    public async Task ClearCollectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await qdrant.DeleteCollectionAsync(id, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var originals = await db.Documents
            .AsNoTracking()
            .Where(d => d.CollectionId == id && d.OriginalFile != null)
            .Select(d => d.OriginalFile!)
            .ToListAsync(cancellationToken);

        // Tags go with them through the cascading foreign key.
        await db.Documents.Where(d => d.CollectionId == id).ExecuteDeleteAsync(cancellationToken);

        foreach (var original in originals)
        {
            DeleteOriginal(original);
        }

        logger.LogInformation("Cleared the documents of collection {CollectionId}", id);
    }

    // Keeps the documents so they can be embedded again, such as with another model.
    public Task DeleteVectorsAsync(Guid collectionId, CancellationToken cancellationToken = default) =>
        qdrant.DeleteCollectionAsync(collectionId, cancellationToken);

    // The documents a website brought in, with the hash of what they were embedded from.
    public async Task<IReadOnlyList<SyncedDocument>> ListSourceDocumentsAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents
            .AsNoTracking()
            .Where(d => d.SourceId == sourceId)
            .Select(d => new SyncedDocument(d.Id, d.CollectionId, d.Source, d.ContentHash))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(
        Guid collectionId,
        string? tag = null,
        string? sourceFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Documents.AsNoTracking().Where(d => d.CollectionId == collectionId);
        if (!string.IsNullOrEmpty(tag))
        {
            query = query.Where(d => d.Tags.Any(t => t.Name == tag));
        }
        if (!string.IsNullOrWhiteSpace(sourceFilter))
        {
            var filter = sourceFilter.Trim().ToLower();
            query = query.Where(d => d.Source.ToLower().Contains(filter));
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
                d.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList(),
                d.AddedById,
                db.Users.Where(u => u.Id == d.AddedById).Select(u => u.Email).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TagCount>> ListTagsAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents
            .AsNoTracking()
            .Where(d => d.CollectionId == collectionId)
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

        var collections = await SummarizeCollections(db, db.Collections.AsNoTracking().OrderBy(c => c.Name))
            .ToListAsync(cancellationToken);

        var recent = await db.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.IngestedAt)
            .Take(recentDocuments)
            .Select(d => new RecentDocument(
                d.Id,
                d.CollectionId,
                db.Collections.Where(c => c.Id == d.CollectionId).Select(c => c.Name).First(),
                d.Source,
                d.IngestedAt,
                d.ChunkCount))
            .ToListAsync(cancellationToken);

        var tags = db.Documents.AsNoTracking().SelectMany(d => d.Tags, (d, t) => new { d.CollectionId, t.Name });

        var distinctTags = await tags.Select(t => t.Name).Distinct().CountAsync(cancellationToken);

        // Per collection, since search filters tags within one collection at a time.
        var top = await tags
            .GroupBy(t => new { t.CollectionId, t.Name })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Name)
            .Take(topTags)
            .Select(g => new { g.Key.CollectionId, g.Key.Name, Documents = g.Count() })
            .ToListAsync(cancellationToken);

        // A collection created since the list above was read is left out rather than shown without a name.
        var names = collections.ToDictionary(c => c.Id, c => c.Name);
        return new LibraryOverview(
            collections,
            recent,
            distinctTags,
            [.. top
                .Where(t => names.ContainsKey(t.CollectionId))
                .Select(t => new CollectionTagCount(t.CollectionId, names[t.CollectionId], t.Name, t.Documents))]);
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
            .Select(d => new DocumentLocation(d.CollectionId, d.Source))
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
        Guid collectionId,
        string source,
        IngestResult result,
        IReadOnlyList<string> tags,
        string addedById,
        byte[]? original = null,
        string? contentHash = null,
        Guid? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        // The API stores beside the ingest worker. Two stores of one source, or two first stores into a new collection,
        // would both create what only one can, leaving points behind for a row that never saves.
        await storing.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Queued jobs can outlive their collection, source or uploader. Checked before Qdrant is touched,
            // since the save below would fail only after the points were written.
            if (!await db.Collections.AsNoTracking().AnyAsync(c => c.Id == collectionId, cancellationToken))
            {
                throw new InvalidOperationException("The collection no longer exists.");
            }
            if (sourceId is { } id && !await db.Sources.AsNoTracking().AnyAsync(s => s.Id == id, cancellationToken))
            {
                throw new InvalidOperationException("The website this came from is no longer synced.");
            }
            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == addedById, cancellationToken))
            {
                throw new InvalidOperationException("The account that added this no longer exists.");
            }

            var document = await db.Documents
                .Include(d => d.Tags)
                .SingleOrDefaultAsync(d => d.CollectionId == collectionId && d.Source == source, cancellationToken);

            if (document is null)
            {
                document = new Document { CollectionId = collectionId, Source = source, Markdown = result.Markdown, AddedById = addedById };
                db.Documents.Add(document);
            }

            document.Markdown = result.Markdown;
            document.Kind = result.Kind;
            document.ContentHash = contentHash;
            document.SourceId = sourceId;
            document.AddedById = addedById;
            document.ChunkCount = await qdrant.ReplaceDocumentAsync(
                collectionId, source, document.Id, result.Kind, result.Chunks, tags, cancellationToken);
            document.IngestedAt = DateTime.UtcNow;

            var previousOriginal = document.OriginalFile;
            document.OriginalFile = original is null ? null : await SaveOriginalAsync(document.Id, source, original, cancellationToken);
            if (previousOriginal is not null && previousOriginal != document.OriginalFile)
            {
                DeleteOriginal(previousOriginal);
            }

            ApplyTags(document, tags);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Stored {Characters:N0} chars of markdown for {Source} in collection {CollectionId} as {DocumentId}",
                result.Markdown.Length, source, collectionId, document.Id);

            return new StoredDocument(document.Id, document.ChunkCount);
        }
        finally
        {
            storing.Release();
        }
    }

    public async Task SetTagsAsync(Guid id, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var document = await db.Documents.Include(d => d.Tags).SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("This document no longer exists.");

        // Qdrant first: search filters on its copy, so the database must not claim tags the points lack.
        await qdrant.SetTagsAsync(document.CollectionId, id, tags, cancellationToken);
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

        await qdrant.DeleteDocumentAsync(document.CollectionId, id, cancellationToken);
        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        if (document.OriginalFile is not null)
        {
            DeleteOriginal(document.OriginalFile);
        }

        logger.LogInformation("Deleted {Source} from collection {CollectionId}", document.Source, document.CollectionId);
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

    private static IQueryable<CollectionSummary> SummarizeCollections(DatabaseContext db, IQueryable<Collection> collections) =>
        collections.Select(c => new CollectionSummary(
            c.Id,
            c.Name,
            c.Description,
            c.IsPrivate,
            c.OwnerId,
            db.Users.Where(u => u.Id == c.OwnerId).Select(u => u.Email).FirstOrDefault(),
            db.UserRoles.Any(ur => ur.UserId == c.OwnerId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Admin)),
            c.CreatedAt,
            c.Documents.Count,
            c.Documents.Sum(d => d.ChunkCount),
            c.Documents.Max(d => (DateTime?)d.IngestedAt)));
}

public sealed record CollectionSummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsPrivate,
    string OwnerId,
    string? OwnerEmail,
    bool OwnerIsAdmin,
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
    IReadOnlyList<string> Tags,
    string AddedById,
    string? AddedByEmail);

public sealed record TagCount(string Name, int Documents);

public sealed record CollectionTagCount(Guid CollectionId, string CollectionName, string Name, int Documents);

public sealed record RecentDocument(Guid Id, Guid CollectionId, string CollectionName, string Source, DateTime IngestedAt, int ChunkCount);

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

public sealed record DocumentLocation(Guid CollectionId, string Source);

public sealed record StoredOriginal(string Path, string ContentType);

public sealed record StoredDocument(Guid Id, int ChunkCount);

public sealed record SyncedDocument(Guid Id, Guid CollectionId, string Source, string? ContentHash);

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    // Relative paths resolve against the content root.
    public string DataPath { get; set; } = "data";
}
