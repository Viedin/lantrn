using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Lantrn.Infra;
using Lantrn.Services.Ingestion;

namespace Lantrn.Services.Search;

// Re-scores search candidates with a cross-encoder, which reads the query and each passage together, through the
// Cohere/Jina-style /rerank endpoint that vLLM, llama.cpp, Infinity, Jina and Cohere serve.
public sealed class Reranker(IHttpClientFactory httpFactory, SettingsStore store, ILogger<Reranker> logger)
{
    public bool Enabled => store.Current.Reranker.Enabled;

    public int Candidates => Math.Max(1, store.Current.Reranker.Candidates);

    // Returns the hits best first, each scored 0–1 by the model.
    public async Task<IReadOnlyList<SearchHit>> RerankAsync(
        string query, IReadOnlyList<SearchHit> hits, CancellationToken cancellationToken = default)
    {
        var settings = store.Current.Reranker;
        if (hits.Count == 0)
        {
            return hits;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/rerank")
        {
            Content = JsonContent.Create(new RerankRequest(
                settings.Model,
                query,
                hits.Select(hit => DocumentChunk.IndexText(hit.Source, hit.HeadingPath, hit.Content)).ToList())),
        };
        if (!string.IsNullOrEmpty(settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await httpFactory.CreateClient(nameof(Reranker)).SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RerankResponse>(timeout.Token)
            ?? throw new InvalidOperationException("The rerank endpoint returned an empty response.");

        // Some servers (llama.cpp) return raw logits rather than probabilities.
        var logits = body.Results.Any(r => r.RelevanceScore is < 0 or > 1);
        var reranked = body.Results
            .Where(r => r.Index >= 0 && r.Index < hits.Count)
            .Select(r => hits[r.Index] with { Score = logits ? Sigmoid(r.RelevanceScore) : r.RelevanceScore })
            .OrderByDescending(hit => hit.Score)
            .ToList();

        logger.LogInformation("Reranked {Candidates} candidates with '{Model}' in {Elapsed:N0} ms (top score {Score})",
            hits.Count, settings.Model, stopwatch.ElapsedMilliseconds, reranked.FirstOrDefault()?.Score ?? 0f);

        return reranked;
    }

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    private sealed record RerankRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("documents")] IReadOnlyList<string> Documents);

    private sealed record RerankResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<RerankResult> Results);

    private sealed record RerankResult(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("relevance_score")] float RelevanceScore);
}
