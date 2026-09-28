using System.Security.Cryptography;
using Lantrn.Infra;

namespace Lantrn.Services.Ingestion;

// Keeps collections in step with the documents folder: each top-level folder is a collection, deeper folders are tags.
// New and changed files are queued; documents whose file is gone are removed.
public sealed class FolderSync(DocumentStore documents, IngestQueue queue, ILogger<FolderSync> logger)
{
    // By path, so a file is only read again when its size or write time changes.
    private Dictionary<string, FileState> hashes = [];
    private readonly HashSet<string> skippedFolders = [];
    private bool warnedEmptyFolder;

    public async Task SyncAsync(Source folder, CancellationToken cancellationToken)
    {
        var root = documents.FolderPath;
        var stored = (await documents.ListSourceDocumentsAsync(folder.Id, cancellationToken)).ToDictionary(d => (d.Collection, d.Source));
        var collections = (await documents.ListCollectionsAsync(cancellationToken)).Select(c => c.Name).ToHashSet();
        var seen = new HashSet<(string Collection, string Source)>();
        var nextHashes = new Dictionary<string, FileState>();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Seen before anything can fail, so a file that can't be read right now doesn't lose its document.
            if (!DocumentExtractor.TryGetContentType(path, out _)
                || ToFolderItem(root, path) is not { } item
                || !seen.Add((item.Collection, item.Source)))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Length > IngestQueue.MaxFileSize)
                {
                    continue;
                }

                var hash = await HashAsync(path, info, nextHashes, cancellationToken);
                if ((stored.TryGetValue((item.Collection, item.Source), out var existing) && existing.ContentHash == hash)
                    || queue.IsPending(item.Collection, item.Source)
                    || queue.HasFailed(item.Collection, item.Source, hash))
                {
                    continue;
                }

                if (collections.Add(item.Collection))
                {
                    await documents.CreateCollectionAsync(item.Collection, "Synced from the documents folder.", cancellationToken);
                }

                queue.EnqueueFile(
                    new IngestRequest(item.Collection, item.Source, Path.GetFileName(path), item.Tags, new IngestOptions(), folder.Id),
                    path);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Syncing {Path} from the documents folder failed", path);
            }
        }

        hashes = nextHashes;
        await RemoveMissingAsync(stored, seen, cancellationToken);
    }

    // An unmounted share or volume looks just like an emptied folder, and must not wipe every collection.
    private async Task RemoveMissingAsync(
        Dictionary<(string Collection, string Source), SyncedDocument> stored,
        HashSet<(string Collection, string Source)> seen,
        CancellationToken cancellationToken)
    {
        var missing = stored.Where(s => !seen.Contains(s.Key)).Select(s => s.Value).ToList();
        if (seen.Count == 0 && missing.Count > 0)
        {
            if (!warnedEmptyFolder)
            {
                logger.LogWarning(
                    "The documents folder {FolderPath} is empty, so its {Documents} synced documents were kept. Delete them from their collections if that was intended",
                    documents.FolderPath, missing.Count);
                warnedEmptyFolder = true;
            }
            return;
        }

        warnedEmptyFolder = false;
        foreach (var document in missing)
        {
            await documents.DeleteAsync(document.Id, cancellationToken);
        }
    }

    // "manual.pdf" -> the default collection; "recipes/pasta.pdf" -> 'recipes'; "hr/2025/leave.pdf" -> 'hr' as "2025/leave.pdf", tagged "2025".
    private FolderItem? ToFolderItem(string root, string path)
    {
        var segments = Path.GetRelativePath(root, path).Replace('\\', '/').Split('/');
        if (segments.Any(s => s.StartsWith('.')))
        {
            return null;
        }

        if (segments.Length == 1)
        {
            return new FolderItem(DocumentStore.DefaultCollection, segments[0], []);
        }

        var collection = segments[0].ToLowerInvariant();
        if (!DocumentStore.IsValidCollectionName(collection))
        {
            if (skippedFolders.Add(segments[0]))
            {
                logger.LogWarning(
                    "Skipping the folder '{Folder}' in the documents folder: collection names are lowercase letters, digits, '-' and '_'",
                    segments[0]);
            }
            return null;
        }

        return new FolderItem(
            collection,
            string.Join('/', segments[1..]),
            DocumentStore.ParseTags(string.Join(',', segments[1..^1])));
    }

    private async Task<string> HashAsync(
        string path, FileInfo info, Dictionary<string, FileState> nextHashes, CancellationToken cancellationToken)
    {
        if (!hashes.TryGetValue(path, out var state) || state.Length != info.Length || state.WrittenAt != info.LastWriteTimeUtc)
        {
            await using var stream = File.OpenRead(path);
            state = new FileState(info.Length, info.LastWriteTimeUtc, Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)));
        }

        nextHashes[path] = state;
        return state.Hash;
    }

    private sealed record FolderItem(string Collection, string Source, IReadOnlyList<string> Tags);

    private sealed record FileState(long Length, DateTime WrittenAt, string Hash);
}
