using Lantrn.Infra;

namespace Lantrn.Services;

// Turns a file or markdown into embedded chunks: the steps shared by the documents folder and the public API.
// The ingest and crawl pages run the same steps one by one, so they can report each as it happens.
public sealed class DocumentIngestor(DocumentExtractor extractor, EmbeddingService embeddings, VisionOcrService vision)
{
    // Images are read by the vision model; everything else is extracted by Xberg. The file name decides how the bytes
    // are read, the source is what the chunks are indexed under; they differ for a fetched page, whose source is its URL.
    public async Task<IngestResult> IngestFileAsync(
        byte[] bytes, string fileName, string source, IngestOptions options, CancellationToken cancellationToken = default)
    {
        var extracted = DocumentExtractor.IsImage(fileName)
            ? await extractor.ChunkMarkdownAsync(
                await vision.ReadImageAsync(bytes, fileName, cancellationToken), fileName, DocumentKind.Ocr, options, cancellationToken)
            : await extractor.IngestAsync(bytes, fileName, options, cancellationToken);

        return await EmbedAsync(extracted, source, cancellationToken);
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
