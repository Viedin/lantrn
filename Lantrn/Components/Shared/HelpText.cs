namespace Lantrn.Components.Shared;

// Tooltips for the ingest and crawl settings, shared so both pages explain them the same way.
public static class HelpText
{
    public const string MaxPages =
        "The most pages to store. The start page always counts; the rest come from the site's sitemap and links under the scope.";

    public const string ChunkSize =
        "The longest a chunk can be, in characters. Documents are split at headings first, so most chunks are shorter. " +
        "Smaller chunks give more precise search hits; larger ones keep more context together.";

    public const string MinChunkSize =
        "Chunks shorter than this are dropped, such as a heading on its own line. " +
        "Documents shorter than this keep their chunks. Set to 0 to keep everything.";

    public const string Overlap =
        "How many characters each chunk repeats from the end of the one before it, " +
        "so a sentence split between two chunks can still be found.";
}
