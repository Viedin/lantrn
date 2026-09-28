using Lantrn.Infra;

namespace Lantrn.Services.Ingestion;

// Turns a file or markdown into embedded chunks: the steps shared by the ingest worker and the public API.
public sealed class DocumentIngestor(DocumentExtractor extractor, EmbeddingService embeddings, VisionOcrService vision)
{
    // Images are read by the vision model; everything else is extracted by Xberg. The file name decides how the bytes
    // are read, the source is what the chunks are indexed under; they differ for a fetched page, whose source is its URL.
    public async Task<IngestResult> IngestFileAsync(
        byte[] bytes, string fileName, string source, IngestOptions options,
        CancellationToken cancellationToken = default, Action<string>? reportStep = null)
    {
        IngestResult extracted;
        if (DocumentExtractor.IsImage(fileName))
        {
            reportStep?.Invoke($"Reading {source} with {vision.Model}");
            var markdown = await vision.ReadImageAsync(bytes, fileName, cancellationToken);
            extracted = await extractor.ChunkMarkdownAsync(markdown, fileName, DocumentKind.Ocr, options, cancellationToken);
        }
        else
        {
            reportStep?.Invoke($"Extracting {source}");
            extracted = await extractor.IngestAsync(bytes, fileName, options, cancellationToken);
        }

        reportStep?.Invoke($"Embedding {extracted.Chunks.Count} chunks from {source}");
        return await EmbedAsync(extracted, source, cancellationToken);
    }

    // Chunks and embeds a stored document again from its markdown, so an image never goes back to the vision model.
    // A PDF is read again from its kept original instead, since its page numbers only come from the file.
    public async Task<IngestResult> ReembedAsync(
        Document document, byte[]? pdf, IngestOptions options,
        CancellationToken cancellationToken = default, Action<string>? reportStep = null)
    {
        if (pdf is not null)
        {
            return await IngestFileAsync(pdf, document.Source, document.Source, options, cancellationToken, reportStep);
        }

        reportStep?.Invoke($"Chunking {document.Source}");
        var extracted = await extractor.ChunkMarkdownAsync(document.Markdown, document.Source, document.Kind, options, cancellationToken);

        reportStep?.Invoke($"Embedding {extracted.Chunks.Count} chunks from {document.Source}");
        return await EmbedAsync(extracted, document.Source, cancellationToken);
    }

    public async Task<IngestResult> IngestMarkdownAsync(
        string markdown, string source, IngestOptions options, CancellationToken cancellationToken = default)
    {
        var extracted = await extractor.ChunkMarkdownAsync(markdown, source, DocumentKind.Text, options, cancellationToken);
        return await EmbedAsync(extracted, source, cancellationToken);
    }

    private async Task<IngestResult> EmbedAsync(IngestResult extracted, string source, CancellationToken cancellationToken) =>
        extracted with { Chunks = await embeddings.EmbedChunksAsync(extracted.Chunks, source, cancellationToken) };
}
