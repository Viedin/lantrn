using Lantrn.Infra;

namespace Lantrn.Services.Search;

// Finds candidates by meaning and keywords, lets the reranker reorder them when it is on, then drops overlapping passages.
public sealed class SearchService(
    EmbeddingService embeddings,
    QdrantStore qdrant,
    Reranker reranker,
    ILogger<SearchService> logger)
{
    // Spare candidates without a reranker, so collapsing overlapping hits still leaves a full page.
    private const int CandidatesPerResult = 3;

    public async Task<SearchResults> SearchAsync(
        string collection,
        string query,
        IReadOnlyList<string> tags,
        DocumentKind? kind,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var rerank = reranker.Enabled;
        var candidates = rerank ? Math.Max(limit, reranker.Candidates) : limit * CandidatesPerResult;

        var vector = await embeddings.EmbedQueryAsync(query, cancellationToken);
        var results = await qdrant.SearchAsync(collection, query, vector, tags, kind, (ulong)candidates, cancellationToken);

        if (rerank)
        {
            try
            {
                var reranked = await reranker.RerankAsync(query, results.Hits, cancellationToken);
                results = new SearchResults(reranked, ScoreKind.Relevance);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Reranking failed, showing the unreranked order instead");
            }
        }

        return results with { Hits = HitCollapser.Collapse(results.Hits, limit) };
    }
}
