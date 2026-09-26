using System.Security.Cryptography;

namespace Lantrn.Services.Ingestion;

// Polls the documents folder and keeps the default collection in step with it: new and changed files
// (by content hash) are embedded, and files that were removed are deleted from the collection.
public sealed class DocumentFolderWatcher(
    DocumentStore documents,
    DocumentIngestor ingestor,
    ILogger<DocumentFolderWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const long MaxFileSize = 100 * 1024 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(documents.FolderPath);
        logger.LogInformation("Watching {FolderPath} for documents", documents.FolderPath);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Syncing the documents folder failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        var stored = await documents.ListFolderDocumentsAsync(cancellationToken);

        var files = Directory.EnumerateFiles(documents.FolderPath, "*", SearchOption.AllDirectories)
            .Where(path => DocumentExtractor.TryGetContentType(path, out _))
            .ToDictionary(path => Path.GetRelativePath(documents.FolderPath, path).Replace('\\', '/'));

        foreach (var (source, path) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (new FileInfo(path).Length > MaxFileSize)
                {
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (stored.TryGetValue(source, out var existing) && existing.ContentHash == hash)
                {
                    continue;
                }

                await IngestAsync(source, bytes, hash, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Ingest of {Source} from the documents folder failed", source);
            }
        }

        foreach (var removed in stored.Values.Where(d => !files.ContainsKey(d.Source)))
        {
            await documents.DeleteAsync(removed.Id, cancellationToken);
        }
    }

    private async Task IngestAsync(string source, byte[] bytes, string hash, CancellationToken cancellationToken)
    {
        var extracted = await ingestor.IngestFileAsync(bytes, source, source, new IngestOptions(), cancellationToken);

        var original = DocumentExtractor.KeepsOriginal(source, extracted.Kind) ? bytes : null;
        await documents.StoreAsync(DocumentStore.DefaultCollection, source, extracted, [], original, hash, CancellationToken.None);

        logger.LogInformation("Synced {Source} from the documents folder", source);
    }
}
