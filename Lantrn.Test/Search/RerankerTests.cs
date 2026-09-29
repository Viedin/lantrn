using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantrn.Infra;
using Lantrn.Services.Search;
using Lantrn.Test.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Lantrn.Test.Search;

public class RerankerTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IHttpClientFactory httpFactory = Substitute.For<IHttpClientFactory>();

    private readonly SearchHit[] hits =
    [
        Hit("invoice.pdf", "Payment terms", "Pay within 30 days."),
        Hit("handbook.md", "", "Holidays are 25 days."),
        Hit("faq.md", "Refunds", "Refunds take a week."),
    ];

    [Fact]
    public async Task Orders_the_hits_by_the_models_scores()
    {
        var reranker = await RerankerAsync(Respond((0, 0.2f), (1, 0.05f), (2, 0.9f)));

        var reranked = await reranker.RerankAsync("refund", hits);

        Assert.Equal(["faq.md", "invoice.pdf", "handbook.md"], reranked.Select(h => h.Source));
        Assert.Equal([0.9f, 0.2f, 0.05f], reranked.Select(h => h.Score));
    }

    [Fact]
    public async Task Raw_logits_are_turned_into_probabilities_in_the_same_order()
    {
        var reranker = await RerankerAsync(Respond((0, -3f), (1, -8f), (2, 4f)));

        var reranked = await reranker.RerankAsync("refund", hits);

        Assert.Equal(["faq.md", "invoice.pdf", "handbook.md"], reranked.Select(h => h.Source));
        Assert.All(reranked, h => Assert.InRange(h.Score, 0f, 1f));
    }

    [Fact]
    public async Task Ignores_results_for_documents_it_wasnt_sent()
    {
        var reranker = await RerankerAsync(Respond((5, 0.9f), (-1, 0.8f), (1, 0.5f)));

        var reranked = await reranker.RerankAsync("refund", hits);

        Assert.Equal("handbook.md", Assert.Single(reranked).Source);
    }

    [Fact]
    public async Task Sends_each_passage_with_its_document_and_section()
    {
        var handler = Respond((0, 0.5f));
        var reranker = await RerankerAsync(handler, apiKey: "secret");

        await reranker.RerankAsync("refund", hits);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://reranker.local/v1/rerank", request.RequestUri!.ToString());
        Assert.Equal("Bearer secret", request.Headers.Authorization!.ToString());

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("rerank-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("refund", body.RootElement.GetProperty("query").GetString());
        var documents = body.RootElement.GetProperty("documents").EnumerateArray().Select(d => d.GetString()).ToList();
        Assert.Equal(
            [
                "Document: invoice.pdf\nSection: Payment terms\n\nPay within 30 days.",
                "Document: handbook.md\n\nHolidays are 25 days.",
                "Document: faq.md\nSection: Refunds\n\nRefunds take a week.",
            ],
            documents);
    }

    [Fact]
    public async Task Sends_no_key_when_none_is_set()
    {
        var handler = Respond((0, 0.5f));
        var reranker = await RerankerAsync(handler, apiKey: null);

        await reranker.RerankAsync("refund", hits);

        Assert.Null(Assert.Single(handler.Requests).Headers.Authorization);
    }

    [Fact]
    public async Task A_failing_endpoint_throws_so_search_can_fall_back()
    {
        var reranker = await RerankerAsync(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<HttpRequestException>(() => reranker.RerankAsync("refund", hits));
    }

    [Fact]
    public async Task No_hits_means_no_request()
    {
        var handler = Respond();
        var reranker = await RerankerAsync(handler);

        Assert.Empty(await reranker.RerankAsync("refund", []));
        Assert.Empty(handler.Requests);
    }

    private async Task<Reranker> RerankerAsync(StubHttpHandler handler, string? apiKey = null)
    {
        httpFactory.CreateClient(nameof(Reranker)).Returns(new HttpClient(handler));
        var store = await TestSettings.LoadAsync(database, settings =>
        {
            settings.Reranker.Enabled = true;
            settings.Reranker.BaseUrl = "http://reranker.local/v1/";
            settings.Reranker.Model = "rerank-model";
            settings.Reranker.ApiKey = apiKey;
        });
        return new Reranker(httpFactory, store, NullLogger<Reranker>.Instance);
    }

    private static StubHttpHandler Respond(params (int Index, float Score)[] results) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                results = results.Select(r => new { index = r.Index, relevance_score = r.Score }),
            }),
        });

    private static SearchHit Hit(string source, string headingPath, string content) =>
        new(0f, content, headingPath, source, [], DocumentKind.Text, Guid.NewGuid(), 0, null, null);

    public void Dispose() => database.Dispose();
}
