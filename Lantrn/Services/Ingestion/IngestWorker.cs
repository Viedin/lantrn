using Lantrn.Infra;

namespace Lantrn.Services.Ingestion;

// Works through the ingest queue one job at a time: extract or OCR, chunk, embed, store.
public sealed class IngestWorker(
    IngestQueue queue,
    DocumentIngestor ingestor,
    DocumentStore documents,
    ILogger<IngestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await queue.RestoreAsync(stoppingToken);

        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var cancellation = queue.Start(job, stoppingToken);
            if (cancellation is null)
            {
                continue;
            }

            var status = await RunAsync(job, cancellation.Token);

            // Shutting down: the job's inbox files stay, so the next start picks it up again.
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            queue.Complete(job, status);
        }
    }

    private async Task<IngestJobStatus> RunAsync(IngestJob job, CancellationToken cancellationToken)
    {
        try
        {
            var (extracted, documentId) = job.Request.ReembedDocumentId is { } id
                ? await ReembedAsync(job, id, cancellationToken)
                : await IngestAsync(job, cancellationToken);

            job.DocumentId = documentId;
            job.Result = extracted with { Chunks = [.. extracted.Chunks.Select(c => c with { Embedding = [] })] };
            return IngestJobStatus.Done;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return IngestJobStatus.Cancelled;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingest of {Source} into collection {CollectionId} failed", job.Request.Source, job.Request.CollectionId);
            job.Error = ex.Message;
            return IngestJobStatus.Failed;
        }
    }

    private async Task<(IngestResult Extracted, Guid DocumentId)> IngestAsync(IngestJob job, CancellationToken cancellationToken)
    {
        var request = job.Request;
        var bytes = await File.ReadAllBytesAsync(job.FilePath, cancellationToken);

        var extracted = await ingestor.IngestFileAsync(
            bytes, request.FileName, request.Source, request.Options, cancellationToken, step => queue.ReportStep(job, step));

        // Not cancellable: a store cut short would leave the database and Qdrant out of step.
        queue.ReportStep(job, $"Storing {request.Source}");
        var original = request.KeepOriginal && DocumentExtractor.KeepsOriginal(request.FileName, extracted.Kind) ? bytes : null;
        var stored = await documents.StoreAsync(
            request.CollectionId, request.Source, extracted, request.Tags, request.AddedById, original,
            Crypto.Sha256Hex(bytes), request.SourceId, CancellationToken.None);

        return (extracted, stored.Id);
    }

    // Everything but the chunks and vectors stays as it was: tags, source, hash, the kept original and who added it.
    private async Task<(IngestResult Extracted, Guid DocumentId)> ReembedAsync(
        IngestJob job, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(documentId, cancellationToken)
            ?? throw new InvalidOperationException("This document no longer exists.");
        var original = await documents.GetOriginalAsync(documentId, cancellationToken) is { } kept
            ? await File.ReadAllBytesAsync(kept.Path, cancellationToken)
            : null;

        // Only PDFs and images keep their original, and an image's text is already in its markdown.
        var pdf = document.Kind == DocumentKind.Ocr ? null : original;
        var extracted = await ingestor.ReembedAsync(
            document, pdf, job.Request.Options, cancellationToken, step => queue.ReportStep(job, step));

        queue.ReportStep(job, $"Storing {document.Source}");
        var stored = await documents.StoreAsync(
            document.CollectionId, document.Source, extracted, [.. document.Tags.Select(t => t.Name)], document.AddedById,
            original, document.ContentHash, document.SourceId, CancellationToken.None);

        return (extracted, stored.Id);
    }
}
