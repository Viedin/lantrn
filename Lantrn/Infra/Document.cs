namespace Lantrn.Infra;

public sealed class Document
{
    public Guid Id { get; set; }

    public required string Collection { get; set; }

    // File name for uploads, URL for crawled pages; the same value Qdrant stores as "source".
    public required string Source { get; set; }

    public required string Markdown { get; set; }

    public DocumentKind Kind { get; set; }

    // File name under the originals folder, for documents whose uploaded file is kept (OCR images and PDFs).
    public string? OriginalFile { get; set; }

    // SHA-256 of the file, set only for documents synced from the documents folder, so a changed file is re-embedded.
    public string? ContentHash { get; set; }

    public int ChunkCount { get; set; }

    public DateTime IngestedAt { get; set; }

    public List<DocumentTag> Tags { get; set; } = [];
}
