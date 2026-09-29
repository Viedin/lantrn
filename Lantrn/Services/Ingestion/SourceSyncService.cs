using System.Collections.Concurrent;
using Lantrn.Infra;

namespace Lantrn.Services.Ingestion;

// Decides when websites sync: on their schedule, checked once a minute, or right away when asked, each in the background.
public sealed class SourceSyncService(
    SourceStore sources,
    WebsiteSync websiteSync,
    ILogger<SourceSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    // Each running crawl has its own step and can be stopped on its own, so nobody's Stop touches anyone else's.
    private readonly ConcurrentDictionary<Guid, RunningSync> syncing = new();
    private CancellationToken stopping;

    // A crawl started or finished.
    public event Action? Changed;

    // A running crawl moved on to its next step; raised once per page, so listeners shouldn't reload anything for it.
    public event Action? StepChanged;

    public bool IsSyncing(Guid sourceId) => syncing.ContainsKey(sourceId);

    // What a running crawl is doing, such as which page it is fetching.
    public string? StepOf(Guid sourceId) => syncing.TryGetValue(sourceId, out var sync) ? sync.Step : null;

    // The pages are credited to whoever started the crawl; a scheduled one to whoever added the website.
    public void SyncNow(Source site, string? startedById = null)
    {
        var sync = new RunningSync(site, startedById ?? site.AddedById);
        if (syncing.TryAdd(site.Id, sync))
        {
            _ = Task.Run(() => SyncWebsiteAsync(sync));
        }
    }

    // Given each running crawl's website and whoever started it.
    public void Stop(Func<Source, string, bool> which)
    {
        foreach (var sync in syncing.Values.Where(s => which(s.Site, s.StartedById)))
        {
            sync.Stop.Cancel();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stopping = stoppingToken;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var site in await sources.ListAsync(stoppingToken))
                {
                    if (site.LastSyncedAt is null || site.LastSyncedAt + site.SyncInterval <= DateTime.UtcNow)
                    {
                        SyncNow(site);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Syncing websites failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task SyncWebsiteAsync(RunningSync sync)
    {
        var site = sync.Site;
        Changed?.Invoke();
        try
        {
            var error = await websiteSync.SyncAsync(site, sync.StartedById, step =>
            {
                sync.Step = step;
                StepChanged?.Invoke();
            }, sync.Stop.Token, stopping);
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

    // Its Stop is never disposed: a Stop may still be cancelling it, and without a timer it holds nothing to release.
    private sealed class RunningSync(Source site, string startedById)
    {
        public Source Site { get; } = site;

        public string StartedById { get; } = startedById;

        public CancellationTokenSource Stop { get; } = new();

        public string? Step { get; set; }
    }
}
