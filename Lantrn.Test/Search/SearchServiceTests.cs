using System.Net;
using System.Net.Http.Json;
using Lantrn.Infra;
using Lantrn.Services;
using Lantrn.Services.Search;
using Lantrn.Test.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Lantrn.Test.Search;

public class SearchServiceTests : IDisposable
{
    private static readonly Guid Collection = Guid.NewGuid();

    private readonly TestDatabase database = new();
    private readonly IEmbeddingService embeddings = Substitute.For<IEmbeddingService>();
    private readonly IQdrantStore qdrant = Substitute.For<IQdrantStore>();
    private readonly IHttpClientFactory httpFactory = Substitute.For<IHttpClientFactory>();

    private readonly SearchHit first = Hit("first.md");
    private readonly SearchHit second = Hit("second.md");
    private readonly SearchHit third = Hit("third.md");

    public SearchServiceTests()
    {
        embeddings.EmbedQueryAsync("refund", Arg.Any<CancellationToken>()).Returns([0.1f, 0.2f]);
        qdrant.SearchAsync(default, default!, default!, default!, default, default, default)
            .ReturnsForAnyArgs(new SearchResults([first, second, third], ScoreKind.Fusion));
    }

    [Fact]
    public async Task Without_a_reranker_it_asks_for_spare_candidates_and_trims_to_the_limit()
    {
        var search = await SearchAsync(rerankerEnabled: false, Respond((0, 0.9f)));

        var results = await search.SearchAsync(Collection, "refund", [], null, limit: 2);

        await qdrant.Received().SearchAsync(Collection, "refund", Arg.Any<float[]>(), Arg.Any<IReadOnlyList<string>>(), null, 6UL, Arg.Any<CancellationToken>());
        Assert.Equal(["first.md", "second.md"], results.Hits.Select(h => h.Source));
        Assert.Equal(ScoreKind.Fusion, results.ScoreKind);
    }

    [Fact]
    public async Task With_a_reranker_it_asks_for_the_configured_candidates_and_uses_the_new_order()
    {
        var search = await SearchAsync(rerankerEnabled: true, Respond((0, 0.1f), (1, 0.2f), (2, 0.9f)));

        var results = await search.SearchAsync(Collection, "refund", [], null, limit: 2);

        await qdrant.Received().SearchAsync(Collection, "refund", Arg.Any<float[]>(), Arg.Any<IReadOnlyList<string>>(), null, 40UL, Arg.Any<CancellationToken>());
        Assert.Equal(["third.md", "second.md"], results.Hits.Select(h => h.Source));
        Assert.Equal(ScoreKind.Relevance, results.ScoreKind);
    }

    [Fact]
    public async Task A_failing_reranker_falls_back_to_the_search_order()
    {
        var search = await SearchAsync(rerankerEnabled: true, new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var results = await search.SearchAsync(Collection, "refund", [], null, limit: 2);

        Assert.Equal(["first.md", "second.md"], results.Hits.Select(h => h.Source));
        Assert.Equal(ScoreKind.Fusion, results.ScoreKind);
    }

    [Fact]
    public async Task A_cancelled_search_stays_cancelled_instead_of_falling_back()
    {
        using var cancellation = new CancellationTokenSource();
        var search = await SearchAsync(rerankerEnabled: true, new StubHttpHandler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => search.SearchAsync(Collection, "refund", [], null, limit: 2, cancellation.Token));
    }

    private async Task<SearchService> SearchAsync(bool rerankerEnabled, StubHttpHandler handler)
    {
        httpFactory.CreateClient(nameof(Reranker)).Returns(new HttpClient(handler));
        var settings = await TestSettings.LoadAsync(database, s =>
        {
            s.Reranker.Enabled = rerankerEnabled;
            s.Reranker.BaseUrl = "http://reranker.local/v1";
            s.Reranker.Model = "rerank-model";
            s.Reranker.Candidates = 40;
        });
        var reranker = new Reranker(httpFactory, settings, NullLogger<Reranker>.Instance);
        return new SearchService(embeddings, qdrant, reranker, NullLogger<SearchService>.Instance);
    }

    private static StubHttpHandler Respond(params (int Index, float Score)[] results) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                results = results.Select(r => new { index = r.Index, relevance_score = r.Score }),
            }),
        });

    private static SearchHit Hit(string source) =>
        new(0.5f, "text", "", source, [], DocumentKind.Text, Guid.NewGuid(), 0, null, null);

    public void Dispose() => database.Dispose();
}
