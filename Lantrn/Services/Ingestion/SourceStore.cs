using Lantrn.Infra;
using Microsoft.EntityFrameworkCore;

namespace Lantrn.Services.Ingestion;

// The websites that SourceSyncService keeps in step with their collections.
public sealed class SourceStore(IDbContextFactory<DatabaseContext> dbFactory)
{
    public async Task<IReadOnlyList<Source>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Sources.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebsiteSource>> ListWebsitesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Sources
            .AsNoTracking()
            .OrderBy(s => s.CreatedAt)
            .Select(s => new WebsiteSource(s, db.Users.Where(u => u.Id == s.AddedById).Select(u => u.Email).FirstOrDefault()))
            .ToListAsync(cancellationToken);
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
        string addedById,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var location = start.ToString();
        var source = await db.Sources.SingleOrDefaultAsync(
            s => s.CollectionId == collectionId && s.Location == location, cancellationToken);

        if (source is null)
        {
            source = new Source
            {
                CollectionId = collectionId,
                Location = location,
                AddedById = addedById,
                CreatedAt = DateTime.UtcNow,
            };
            db.Sources.Add(source);
        }

        source.Scope = scope;
        source.MaxPages = maxPages;
        source.Tags = [.. tags];
        source.Chunking = chunking.Clone();
        source.SyncInterval = syncInterval;
        source.AddedById = addedById;
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
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Sources.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    // A removed admin's websites keep syncing for whoever takes over their collections.
    public async Task TransferAsync(string fromUserId, string toUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Sources
            .Where(s => s.AddedById == fromUserId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AddedById, toUserId), cancellationToken);
    }

    // A removed user's websites stop syncing; DocumentStore deletes the pages they brought in.
    public async Task RemoveAddedByAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Sources.Where(s => s.AddedById == userId).ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed record WebsiteSource(Source Source, string? AddedByEmail);
