using Lantrn.Infra;

namespace Lantrn.Services.Ingestion;

// Crawls a website source again: changed pages are queued, pages gone from the site are removed.
public sealed class WebsiteSync(DocumentStore documents, IngestQueue queue, WebCrawler crawler, ILogger<WebsiteSync> logger)
{
    // Returns what went wrong, to show beside the site, or null. Stop in the ingest panel stops the crawl too.
    public async Task<string?> SyncAsync(Source site, CancellationToken stoppingToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, queue.StopToken);
        try
        {
            return await CrawlAsync(site, cancellation.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return "Stopped before it finished.";
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Syncing {Site} failed", site.Location);
            return $"Could not find pages at {site.Location}: {ex.Message}";
        }
    }

    private async Task<string?> CrawlAsync(Source site, CancellationToken cancellationToken)
    {
        var start = new Uri(site.Location);
        queue.ReportStep($"Reading the sitemap and following links from {start}");
        var pages = await crawler.MapAsync(start, site.Scope ?? WebCrawler.DefaultScope(start), site.MaxPages, cancellationToken);
        var stored = (await documents.ListSourceDocumentsAsync(site.Id, cancellationToken)).ToDictionary(d => d.Source);

        var failed = 0;
        foreach (var page in pages)
        {
            // The URL is the source, so search hits point back at the page.
            var source = page.ToString();
            if (queue.IsPending(site.Collection, source))
            {
                continue;
            }

            try
            {
                queue.ReportStep($"Fetching {page}");
                var fetched = await crawler.FetchAsync(page, cancellationToken);
                if (stored.TryGetValue(source, out var existing) && existing.ContentHash == DocumentStore.Hash(fetched.Bytes))
                {
                    continue;
                }

                await queue.EnqueueAsync(
                    new IngestRequest(site.Collection, source, fetched.FileName, site.Tags, site.Chunking, site.Id, KeepOriginal: false),
                    fetched.Bytes, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Fetching {Url} for {Site} failed", page, site.Location);
                failed++;
            }
        }

        // A site that is down or only half mapped must not empty the collection.
        var urls = pages.Select(p => p.ToString()).ToHashSet();
        var missing = stored.Values.Where(d => !urls.Contains(d.Source)).ToList();
        if (missing.Count > 0 && pages.Count * 2 < stored.Count)
        {
            return $"Found only {pages.Count} pages where {stored.Count} were stored before, so none were removed.";
        }

        foreach (var document in missing)
        {
            await documents.DeleteAsync(document.Id, CancellationToken.None);
        }

        logger.LogInformation("Synced {Site} into '{Collection}': {Pages} pages, {Removed} removed, {Failed} failed",
            site.Location, site.Collection, pages.Count, missing.Count, failed);

        return failed > 0 ? $"{failed} of {pages.Count} pages could not be fetched." : null;
    }
}
