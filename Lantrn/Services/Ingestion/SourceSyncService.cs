using System.Collections.Concurrent;
using System.Threading.Channels;
using Lantrn.Infra;

namespace Lantrn.Services.Ingestion;

// Decides when sources sync. The documents folder syncs once a minute and shortly after anything in it changes;
// websites are crawled on their schedule, or right away when asked, each in the background.
public sealed class SourceSyncService(
    SourceStore sources,
    FolderSync folderSync,
    WebsiteSync websiteSync,
    DocumentStore documents,
    ILogger<SourceSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);

    private readonly Channel<bool> folderChanged = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ConcurrentDictionary<Guid, bool> syncing = new();
    private CancellationToken stopping;

    public event Action? Changed;

    public bool IsSyncing(Guid sourceId) => syncing.ContainsKey(sourceId);

    public void SyncNow(Source site)
    {
        if (syncing.TryAdd(site.Id, true))
        {
            _ = Task.Run(() => SyncWebsiteAsync(site));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stopping = stoppingToken;
        Directory.CreateDirectory(documents.FolderPath);
        using var watcher = WatchFolder();
        logger.LogInformation("Watching {FolderPath} for documents", documents.FolderPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var source in await sources.ListAsync(stoppingToken))
                {
                    if (source.Kind == SourceKind.Folder)
                    {
                        await folderSync.SyncAsync(source, stoppingToken);
                    }
                    else if (source.LastSyncedAt is null || source.LastSyncedAt + source.SyncInterval <= DateTime.UtcNow)
                    {
                        SyncNow(source);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Syncing sources failed");
            }

            await WaitAsync(stoppingToken);
        }
    }

    private async Task SyncWebsiteAsync(Source site)
    {
        Changed?.Invoke();
        try
        {
            var error = await websiteSync.SyncAsync(site, stopping);
            await sources.RecordSyncAsync(site.Id, error, CancellationToken.None);
        }
        catch (Exception ex) when (!stopping.IsCancellationRequested)
        {
            logger.LogError(ex, "Recording the sync of {Site} failed", site.Location);
        }
        finally
        {
            syncing.TryRemove(site.Id, out _);
            Changed?.Invoke();
        }
    }

    // Until the poll interval is up, or shortly after the folder changes.
    private async Task WaitAsync(CancellationToken stoppingToken)
    {
        using var poll = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        poll.CancelAfter(PollInterval);
        try
        {
            await folderChanged.Reader.ReadAsync(poll.Token);
            // Copying a folder in raises an event per file; let them settle into one sync.
            await Task.Delay(Debounce, stoppingToken);
            folderChanged.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
        }
    }

    // Change events don't arrive from some network shares and bind mounts, which the poll still catches.
    private FileSystemWatcher? WatchFolder()
    {
        try
        {
            var watcher = new FileSystemWatcher(documents.FolderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            FileSystemEventHandler onChange = (_, _) => folderChanged.Writer.TryWrite(true);
            watcher.Created += onChange;
            watcher.Changed += onChange;
            watcher.Deleted += onChange;
            watcher.Renamed += (_, _) => folderChanged.Writer.TryWrite(true);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Can't watch {FolderPath} for changes; it is still checked once a minute", documents.FolderPath);
            return null;
        }
    }
}
