using System.ComponentModel.DataAnnotations;
using Lantrn.Infra;
using Lantrn.Services;
using Lantrn.Services.Ingestion;

namespace Lantrn.Api;

/// <summary>A document in a collection, without its text.</summary>
/// <param name="Id">The document's id.</param>
/// <param name="Collection">The collection it belongs to.</param>
/// <param name="Source">The file name or URL it was ingested from. Ingesting the same source into the collection again replaces it.</param>
/// <param name="Kind">Text, or Ocr for text read from an image.</param>
/// <param name="Tags">Its tags, in lowercase.</param>
/// <param name="ChunkCount">How many searchable chunks it was split into.</param>
/// <param name="Characters">The length of its markdown.</param>
/// <param name="IngestedAt">When it was added or last refreshed.</param>
public sealed record DocumentResponse(
    Guid Id,
    string Collection,
    string Source,
    DocumentKind Kind,
    IReadOnlyList<string> Tags,
    int ChunkCount,
    int Characters,
    DateTimeOffset IngestedAt)
{
    internal static DocumentResponse From(string collection, DocumentSummary document) => new(
        document.Id,
        collection,
        document.Source,
        document.Kind,
        document.Tags,
        document.ChunkCount,
        document.Characters,
        ApiTime.Utc(document.IngestedAt));

    internal static DocumentResponse From(Document document) => new(
        document.Id,
        document.Collection,
        document.Source,
        document.Kind,
        document.Tags.Select(t => t.Name).Order().ToList(),
        document.ChunkCount,
        document.Markdown.Length,
        ApiTime.Utc(document.IngestedAt));
}

/// <summary>A document with the markdown it was extracted to.</summary>
/// <param name="Id">The document's id.</param>
/// <param name="Collection">The collection it belongs to.</param>
/// <param name="Source">The file name or URL it was ingested from.</param>
/// <param name="Kind">Text, or Ocr for text read from an image.</param>
/// <param name="Tags">Its tags, in lowercase.</param>
/// <param name="ChunkCount">How many searchable chunks it was split into.</param>
/// <param name="IngestedAt">When it was added or last refreshed.</param>
/// <param name="HasOriginal">Whether the uploaded file was kept (PDFs and images); fetch it from /documents/{id}/original.</param>
/// <param name="Markdown">The full text, as markdown.</param>
public sealed record DocumentDetailResponse(
    Guid Id,
    string Collection,
    string Source,
    DocumentKind Kind,
    IReadOnlyList<string> Tags,
    int ChunkCount,
    DateTimeOffset IngestedAt,
    bool HasOriginal,
    string Markdown)
{
    internal static DocumentDetailResponse From(Document document) => new(
        document.Id,
        document.Collection,
        document.Source,
        document.Kind,
        document.Tags.Select(t => t.Name).Order().ToList(),
        document.ChunkCount,
        ApiTime.Utc(document.IngestedAt),
        document.OriginalFile is not null,
        document.Markdown);
}

/// <summary>Markdown or plain text to add to a collection as a document.</summary>
public sealed record IngestMarkdownRequest
{
    /// <summary>
    /// A name for the document, such as "handbook/leave.md". Ingesting the same source into the collection again
    /// replaces the earlier document.
    /// </summary>
    [Required, MaxLength(2048)]
    public required string Source { get; init; }

    /// <summary>The document's text. Headings are used to split it into sections.</summary>
    [Required]
    public required string Markdown { get; init; }

    /// <summary>Tags to search the document by. They are lowercased and de-duplicated.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>How to split the document into chunks; the defaults suit most documents.</summary>
    public ChunkingOptions? Chunking { get; init; }
}

/// <summary>A web page to fetch and add to a collection as a document.</summary>
public sealed record IngestUrlRequest
{
    /// <summary>An absolute http or https URL. The URL becomes the document's source.</summary>
    [Required]
    public required Uri Url { get; init; }

    /// <summary>Tags to search the document by. They are lowercased and de-duplicated.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>How to split the document into chunks; the defaults suit most documents.</summary>
    public ChunkingOptions? Chunking { get; init; }
}

/// <summary>How a document is split into chunks. Anything left out keeps its default.</summary>
public sealed record ChunkingOptions
{
    /// <summary>The most characters in a chunk. Defaults to 2000.</summary>
    [Range(1, 100_000)]
    public int? MaxCharacters { get; init; }

    /// <summary>Chunks shorter than this are merged into their neighbour. Defaults to 100.</summary>
    [Range(0, 100_000)]
    public int? MinCharacters { get; init; }

    /// <summary>How many characters neighbouring chunks share. Must be less than MaxCharacters. Defaults to 200.</summary>
    [Range(0, 100_000)]
    public int? Overlap { get; init; }

    internal static IngestOptions ToOptions(int? maxCharacters, int? minCharacters, int? overlap)
    {
        var options = new IngestOptions();
        options.MaxCharacters = maxCharacters ?? options.MaxCharacters;
        options.MinCharacters = minCharacters ?? options.MinCharacters;
        options.Overlap = overlap ?? options.Overlap;
        return options;
    }

    internal IngestOptions ToOptions() => ToOptions(MaxCharacters, MinCharacters, Overlap);
}

/// <summary>The tags a document should have, replacing the ones it has.</summary>
public sealed record SetTagsRequest
{
    /// <summary>The new tags. They are lowercased and de-duplicated; an empty list removes them all.</summary>
    [Required]
    public required IReadOnlyList<string> Tags { get; init; }
}
