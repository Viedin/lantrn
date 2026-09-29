using Lantrn.Infra;
using Microsoft.EntityFrameworkCore;

namespace Lantrn.Services.Ingestion;

// The folders and websites that SourceSyncService keeps in step with their collections.
public sealed class SourceStore(IDbContextFactory<DatabaseContext> dbFactory, DocumentStore documents, ILogger<SourceStore> logger)
{
    public async Task EnsureDocumentsFolderAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Sources.AsNoTracking().AnyAsync(s => s.Id == Source.DocumentsFolderId, cancellationToken))
        {
            return;
        }

        db.Sources.Add(new Source
        {
            Id = Source.DocumentsFolderId,
            Kind = SourceKind.Folder,
            CollectionId = Collection.DefaultId,
            Location = documents.FolderPath,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Added the documents folder as a source");
    }

    public async Task<IReadOnlyList<Source>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Sources.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync(cancellationToken);
    }

    // Crawling the same site into the same collection again updates its source: two sources
    // claiming the same pages would each re-embed them on every sync.
    public async Task<Source> SaveWebsiteAsync(
        Guid collectionId,
        Uri start,
        string? scope,
        int maxPages,
        IReadOnlyList<string> tags,
        IngestOptions chunking,
        TimeSpan? syncInterval,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var location = start.ToString();
        var source = await db.Sources.SingleOrDefaultAsync(
            s => s.Kind == SourceKind.Website && s.CollectionId == collectionId && s.Location == location, cancellationToken);

        if (source is null)
        {
            source = new Source
            {
                Kind = SourceKind.Website,
                CollectionId = collectionId,
                Location = location,
                CreatedAt = DateTime.UtcNow,
            };
            db.Sources.Add(source);
        }

        source.Scope = scope;
        source.MaxPages = maxPages;
        source.Tags = [.. tags];
        source.Chunking = chunking.Clone();
        source.SyncInterval = syncInterval;
        await db.SaveChangesAsync(cancellationToken);

        return source;
    }

    public async Task RecordSyncAsync(Guid id, string? error, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Sources
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.LastSyncedAt, DateTime.UtcNow)
                .SetProperty(x => x.LastError, error), cancellationToken);
    }

    // The documents stay; they just stop being synced.
    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Source.DocumentsFolderId)
        {
            throw new InvalidOperationException("The documents folder is always synced.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Sources.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
