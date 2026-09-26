using System.Diagnostics;
using System.Text;
using Lantrn.Infra;
using Xberg;

namespace Lantrn.Services.Ingestion;

/// <summary>
/// Extracts and chunks documents in-process through the Xberg native library.
/// </summary>
public sealed class DocumentExtractor(ILogger<DocumentExtractor> logger)
{
    public static readonly IReadOnlyDictionary<string, string> SupportedTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".md"] = "text/markdown",
            [".markdown"] = "text/markdown",
            [".txt"] = "text/plain",
            [".html"] = "text/html",
            [".htm"] = "text/html",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
        };

    public static string AcceptAttribute { get; } = string.Join(",", SupportedTypes.Keys.Concat(SupportedTypes.Values.Distinct()));

    public static bool TryGetContentType(string fileName, out string contentType) =>
        SupportedTypes.TryGetValue(Path.GetExtension(fileName), out contentType!);

    // Images carry no text layer; VisionOcrService reads them and ChunkMarkdownAsync chunks the result.
    public static bool IsImage(string fileName) =>
        TryGetContentType(fileName, out var contentType) && contentType.StartsWith("image/", StringComparison.Ordinal);

    // Images are shown beside their text, and PDFs open in the browser's viewer at the matching page.
    public static bool KeepsOriginal(string fileName, DocumentKind kind) =>
        kind == DocumentKind.Ocr || Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<IngestResult> IngestAsync(
        byte[] bytes,
        string fileName,
        IngestOptions ingestOptions,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetContentType(fileName, out var contentType) || IsImage(fileName))
        {
            throw new NotSupportedException(
                $"Unsupported file type '{Path.GetExtension(fileName)}'. Supported: {string.Join(", ", SupportedTypes.Keys.Where(k => !IsImage(k)))}.");
        }

        return await ExtractAsync(bytes, fileName, contentType, ingestOptions, cancellationToken);
    }

    // Chunks markdown produced elsewhere (such as OCR) the same way as an uploaded markdown file.
    public async Task<IngestResult> ChunkMarkdownAsync(
        string markdown,
        string sourceFileName,
        DocumentKind kind,
        IngestOptions ingestOptions,
        CancellationToken cancellationToken = default)
    {
        var result = await ExtractAsync(
            Encoding.UTF8.GetBytes(markdown),
            Path.ChangeExtension(sourceFileName, ".md"),
            "text/markdown",
            ingestOptions,
            cancellationToken);

        return result with { Kind = kind };
    }

    private async Task<IngestResult> ExtractAsync(
        byte[] bytes,
        string fileName,
        string contentType,
        IngestOptions ingestOptions,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Extracting {FileName} ({Bytes:N0} bytes, {ContentType}): chunker=markdown size={MaxCharacters} overlap={Overlap}",
            fileName, bytes.Length, contentType, ingestOptions.MaxCharacters, ingestOptions.Overlap);

        var input = new ExtractInput
        {
            Kind = ExtractInputKind.Bytes,
            Bytes = bytes,
            MimeType = contentType,
            Filename = fileName,
        };

        var stopwatch = Stopwatch.StartNew();
        ExtractionResult response;
        try
        {
            // The binding takes no cancellation token, so a cancelled ingest stops waiting while the extraction finishes in the background.
            response = await XbergConverter.ExtractAsync(input, BuildConfig(contentType, ingestOptions))
                .WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Xberg failed on {FileName} after {Elapsed:N0} ms", fileName, stopwatch.ElapsedMilliseconds);
            throw new InvalidOperationException($"Extraction failed: {ex.Message}", ex);
        }

        foreach (var error in response.Errors)
        {
            logger.LogWarning("Xberg reported a non-fatal error on input {Index} ({ErrorType}): {Message}",
                error.Index, error.ErrorType, error.Message);
        }

        var document = response.Results.FirstOrDefault();
        if (document is null)
        {
            var message = response.Errors.FirstOrDefault()?.Message ?? "Xberg returned no document.";
            logger.LogError("Extraction of {FileName} produced no document: {Message}", fileName, message);
            throw new InvalidOperationException($"Extraction failed: {message}");
        }

        var markdown = document.Content ?? string.Empty;
        var chunks = DropShortChunks((document.Chunks ?? []).Select(DocumentChunk.From).ToList(), markdown, ingestOptions.MinCharacters);

        logger.LogInformation(
            "Extracted {FileName} in {Elapsed:N0} ms: {Characters:N0} chars of markdown, {Chunks} chunks ({Dropped} under {MinCharacters} chars dropped)",
            fileName, stopwatch.ElapsedMilliseconds, markdown.Length, chunks.Count, document.Chunks?.Count - chunks.Count ?? 0, ingestOptions.MinCharacters);

        if (chunks.Count == 0)
        {
            // Usually an image without legible text, or a web page that renders its content with JavaScript.
            throw new InvalidOperationException("No text could be extracted.");
        }

        return new IngestResult(markdown, chunks, DocumentKind.Text);
    }

    // A heading on its own ("Responses API") becomes a chunk that matches everything and says nothing.
    // Short documents keep their chunks, otherwise a page with one line of text would store nothing.
    private static List<DocumentChunk> DropShortChunks(List<DocumentChunk> chunks, string markdown, int minCharacters)
    {
        if (markdown.Trim().Length <= minCharacters)
        {
            return chunks;
        }

        var kept = chunks.Where(c => c.Content.Trim().Length >= minCharacters).ToList();
        return kept.Count == 0
            ? chunks
            : kept.Select((c, i) => c with { ChunkIndex = i }).ToList();
    }

    // Embeddings come from EmbeddingService, so the chunking config leaves them out.
    private static ExtractionConfig BuildConfig(string contentType, IngestOptions ingestOptions) => new()
    {
        OutputFormat = OutputFormat.Markdown,
        Chunking = new ChunkingConfig
        {
            ChunkerType = ChunkerType.Markdown,
            MaxCharacters = (ulong)ingestOptions.MaxCharacters,
            Overlap = (ulong)ingestOptions.Overlap,
        },
        // Let the HTML converter drop nav, header, footer, aside and forms so only page content is chunked.
        HtmlOptions = contentType == "text/html"
            ? new ConversionOptions
            {
                Preprocessing = new PreprocessingOptions
                {
                    Enabled = true,
                    Preset = PreprocessingPreset.Aggressive,
                    RemoveNavigation = true,
                    RemoveForms = true,
                },
                // Otherwise the <head> comes out as a front matter block of canonical/meta-og/meta-twitter lines.
                ExtractMetadata = false,
                // Icons and images come out as "SVG Image" and broken image links.
                SkipImages = true,
                ExcludeSelectors = [.. NoiseSelectors],
            }
            : null,
    };

    // Site chrome and widgets that are not page content. Most docs sites (Mintlify, Docusaurus and the like)
    // use semantic tags; "sr-only" catches "Skip to main content" and the hidden llms.txt index notice.
    private static readonly string[] NoiseSelectors =
    [
        "head", "script", "style", "noscript", "template", "svg", "img", "picture", "video", "audio",
        // Not "header": docs sites often put the page's <h1> in a <header> inside the article.
        "canvas", "iframe", "button", "select", "input", "nav", "footer", "aside",
        "[role='navigation']", "[role='banner']", "[role='contentinfo']", "[aria-hidden='true']", ".sr-only",
    ];
}

// Mutable so the ingest and crawl pages can bind their fields to it directly.
public sealed class IngestOptions
{
    public int MaxCharacters { get; set; } = 2000;
    public int MinCharacters { get; set; } = 100;
    public int Overlap { get; set; } = 200;

    public string? Validate() =>
        MaxCharacters <= 0 ? "Chunk size must be positive."
        : MinCharacters < 0 || Overlap < 0 ? "Min chunk size and overlap can't be negative."
        : Overlap >= MaxCharacters ? "Overlap must be smaller than the chunk size."
        : null;
}

public sealed record IngestResult(
    string Markdown,
    IReadOnlyList<DocumentChunk> Chunks,
    DocumentKind Kind);

/// <summary>A chunk as this app cares about it, flattened from Xberg's chunk.</summary>
public sealed record DocumentChunk(
    string Content,
    float[] Embedding,
    string HeadingPath,
    long ChunkIndex,
    uint? FirstPage,
    uint? LastPage)
{
    internal static DocumentChunk From(Chunk chunk) => new(
        chunk.Content ?? string.Empty,
        [],
        // "1. Introduction > 1.2 Background", or empty when the chunker recorded no headings.
        string.Join(" > ", chunk.Metadata?.HeadingPath ?? []),
        (long)(chunk.Metadata?.ChunkIndex ?? 0),
        chunk.Metadata?.FirstPage,
        chunk.Metadata?.LastPage);

    // What gets embedded and keyword-indexed, as opposed to shown: on its own a chunk cannot say which
    // document or section it came from, so "Total: 342 kr" would never match a search for the shop's receipt.
    public string IndexText(string source) =>
        string.IsNullOrEmpty(HeadingPath)
            ? $"Document: {source}\n\n{Content}"
            : $"Document: {source}\nSection: {HeadingPath}\n\n{Content}";
}
