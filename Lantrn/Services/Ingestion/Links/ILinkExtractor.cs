namespace Lantrn.Services.Ingestion.Links;

// Turns a link added on the crawl page into the files to ingest, instead of crawling it as a website.
// Extractors are tried in the order CoreServices registers them, and the first that can extract a link does.
public interface ILinkExtractor
{
    // What the link is added as: "Add GitHub repository".
    string Label { get; }

    bool CanExtract(Uri url);

    Task<IReadOnlyList<ExtractedFile>> ExtractAsync(Uri url, CancellationToken cancellationToken = default);
}

// The file name decides how the bytes are read; the source is what the document is stored and found under.
public sealed record ExtractedFile(string Source, string FileName, byte[] Bytes, IReadOnlyList<string> Tags);
