using Lantrn.Infra;
using Lantrn.Services.Search;

namespace Lantrn.Api;

/// <summary>The passages that best match a query, best first.</summary>
/// <param name="ScoreKind">
/// What the scores mean: Similarity is cosine similarity from a meaning-only search, Fusion only orders hits from
/// a combined meaning and keyword search, and Relevance is a reranker's 0–1 judgement.
/// </param>
/// <param name="Hits">The matching passages.</param>
public sealed record SearchResponse(ScoreKind ScoreKind, IReadOnlyList<SearchHitResponse> Hits)
{
    internal static SearchResponse From(SearchResults results) =>
        new(results.ScoreKind, results.Hits.Select(SearchHitResponse.From).ToList());
}

/// <summary>A passage from a document that matches the query.</summary>
/// <param name="DocumentId">The document it comes from; fetch the full text from /documents/{id}.</param>
/// <param name="Source">The file name or URL the document was ingested from.</param>
/// <param name="HeadingPath">The headings the passage sits under, if any, such as "Leave > Parental leave".</param>
/// <param name="Content">The passage, as markdown.</param>
/// <param name="Score">How well it matches; see ScoreKind.</param>
/// <param name="Tags">The document's tags.</param>
/// <param name="Kind">Text, or Ocr for text read from an image.</param>
/// <param name="FirstPage">The page the passage starts on, for paged documents such as PDFs.</param>
/// <param name="LastPage">The page the passage ends on, for paged documents such as PDFs.</param>
public sealed record SearchHitResponse(
    Guid DocumentId,
    string Source,
    string HeadingPath,
    string Content,
    float Score,
    IReadOnlyList<string> Tags,
    DocumentKind Kind,
    long? FirstPage,
    long? LastPage)
{
    internal static SearchHitResponse From(SearchHit hit) => new(
        hit.DocumentId,
        hit.Source,
        hit.HeadingPath,
        hit.Content,
        hit.Score,
        hit.Tags,
        hit.Kind,
        hit.FirstPage,
        hit.LastPage);
}
