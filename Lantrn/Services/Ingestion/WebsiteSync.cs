using Lantrn.Infra;
using Lantrn.Services.Ingestion.Links;

namespace Lantrn.Services.Ingestion;

// Syncs a website source again: a link an extractor takes, such as a GitHub repository, is extracted, anything else
// is crawled. Changed files and pages are queued, and the ones gone from the source are removed.
public sealed class WebsiteSync(
    DocumentStore documents,
    IngestQueue queue,
    WebCrawler crawler,
    IEnumerable<ILinkExtractor> extractors,
    ILogger<WebsiteSync> logger)
{
    // Returns what went wrong, to show beside the site, or null. Stop in the ingest panel stops the crawl through stopToken.
    // The pages are queued, and credited, as whoever started the crawl.
    public async Task<string?> SyncAsync(
        Source site, string startedById, Action<string> reportStep, CancellationToken stopToken, CancellationToken stoppingToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, stopToken);
        try
        {
            return await SyncSiteAsync(site, startedById, reportStep, cancellation.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return "Stopped before it finished.";
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Syncing {Site} failed", site.Location);
            return $"Could not read {site.Location}: {ex.Message}";
        }
    }

    private async Task<string?> SyncSiteAsync(
        Source site, string startedById, Action<string> reportStep, CancellationToken cancellationToken)
    {
        var start = new Uri(site.Location);
        var stored = (await documents.ListSourceDocumentsAsync(site.Id, cancellationToken)).ToDictionary(d => d.Source);
        var extractor = extractors.FirstOrDefault(e => e.CanExtract(start));

        var (found, failed) = extractor is null
            ? await CrawlAsync(site, startedById, start, stored, reportStep, cancellationToken)
            : await ExtractAsync(site, startedById, start, extractor, stored, reportStep, cancellationToken);

        // A site that is down or only half mapped must not empty the collection.
        var missing = stored.Values.Where(d => !found.Contains(d.Source)).ToList();
        if (missing.Count > 0 && found.Count * 2 < stored.Count)
        {
            return $"Found only {found.Count} documents where {stored.Count} were stored before, so none were removed.";
        }

        foreach (var document in missing)
        {
            await documents.DeleteAsync(document.Id, CancellationToken.None);
        }

        logger.LogInformation("Synced {Site} into collection {CollectionId}: {Found} documents, {Removed} removed, {Failed} failed",
            site.Location, site.CollectionId, found.Count, missing.Count, failed);

        return failed > 0 ? $"{failed} of {found.Count} pages could not be fetched." : null;
    }

    private async Task<(HashSet<string> Found, int Failed)> ExtractAsync(
        Source site,
        string startedById,
        Uri start,
        ILinkExtractor extractor,
        Dictionary<string, SyncedDocument> stored,
        Action<string> reportStep,
        CancellationToken cancellationToken)
    {
        reportStep($"Downloading {start}");
        var files = await extractor.ExtractAsync(start, cancellationToken);
        foreach (var file in files)
        {
            await EnqueueIfChangedAsync(site, startedById, file, stored, cancellationToken);
        }

        return (files.Select(f => f.Source).ToHashSet(), 0);
    }

    private async Task<(HashSet<string> Found, int Failed)> CrawlAsync(
        Source site,
        string startedById,
        Uri start,
        Dictionary<string, SyncedDocument> stored,
        Action<string> reportStep,
        CancellationToken cancellationToken)
    {
        reportStep($"Reading the sitemap and following links from {start}");
        var pages = await crawler.MapAsync(start, site.Scope ?? WebCrawler.DefaultScope(start), site.MaxPages, cancellationToken);

        var failed = 0;
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            try
            {
                reportStep($"Fetching page {i + 1} of {pages.Count}");
                var fetched = await crawler.FetchAsync(page, cancellationToken);
                // The URL is the source, so search hits point back at the page.
                await EnqueueIfChangedAsync(
                    site, startedById, new ExtractedFile(page.ToString(), fetched.FileName, fetched.Bytes, []), stored, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Fetching {Url} for {Site} failed", page, site.Location);
                failed++;
            }
        }

        return (pages.Select(p => p.ToString()).ToHashSet(), failed);
    }

    private async Task EnqueueIfChangedAsync(
        Source site, string startedById, ExtractedFile file, Dictionary<string, SyncedDocument> stored, CancellationToken cancellationToken)
    {
        if (queue.IsPending(site.CollectionId, file.Source)
            || (stored.TryGetValue(file.Source, out var existing) && existing.ContentHash == DocumentStore.Hash(file.Bytes)))
        {
            return;
        }

        await queue.EnqueueAsync(
            new IngestRequest(
                site.CollectionId, file.Source, file.FileName, [.. site.Tags.Union(file.Tags)], site.Chunking, startedById,
                site.Id, KeepOriginal: false),
            file.Bytes, cancellationToken);
    }
}
