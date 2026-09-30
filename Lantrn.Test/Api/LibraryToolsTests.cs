using Lantrn.Api;
using Lantrn.Infra;
using Lantrn.Services;
using Lantrn.Services.Ingestion;
using Lantrn.Services.Search;
using Lantrn.Test.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using NSubstitute;

namespace Lantrn.Test.Api;

public class LibraryToolsTests : IAsyncLifetime
{
    private const string Root = "root";

    private readonly TestDatabase database = new();
    private readonly IQdrantStore qdrant = Substitute.For<IQdrantStore>();
    private readonly IEmbeddingService embeddings = Substitute.For<IEmbeddingService>();
    private readonly string contentRoot = Path.Combine(Path.GetTempPath(), $"lantrn-tests-{Guid.NewGuid():N}");
    private readonly DocumentStore store;
    private LibraryTools tools = null!;

    public LibraryToolsTests()
    {
        using (var db = database.CreateContext())
        {
            db.Users.Add(new ApplicationUser { Id = Root });
            db.SaveChanges();
        }

        embeddings.EmbedQueryAsync(default!, default).ReturnsForAnyArgs([0.1f, 0.2f]);
        qdrant.SearchAsync(default, default!, default!, default!, default, default, default)
            .ReturnsForAnyArgs(new SearchResults([], ScoreKind.Fusion));

        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(contentRoot);
        store = new DocumentStore(database.Factory, qdrant, Options.Create(new StorageOptions()), environment, NullLogger<DocumentStore>.Instance);
    }

    public async Task InitializeAsync()
    {
        var settings = await TestSettings.LoadAsync(database, s => s.Reranker.Enabled = false);
        var reranker = new Reranker(Substitute.For<IHttpClientFactory>(), settings, NullLogger<Reranker>.Instance);
        var search = new SearchService(embeddings, qdrant, reranker, NullLogger<SearchService>.Instance);
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Users.Admin(Root) } };
        tools = new LibraryTools(http, store, search, NullLogger<LibraryTools>.Instance);
    }

    [Fact]
    public void The_tools_register_the_way_startup_registers_them()
    {
        using var services = new ServiceCollection()
            .AddMcpServer()
            .WithTools<LibraryTools>(LibraryTools.SerializerOptions)
            .Services
            .BuildServiceProvider();

        var names = services.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name);

        Assert.Equal(["get_document", "list_collections", "search"], names.Order());
    }

    [Fact]
    public async Task Searching_passes_the_tags_along_in_lowercase()
    {
        var handbook = await store.CreateCollectionAsync("handbook", null, false, Root);

        await tools.SearchAsync(handbook, "leave", ["HR", "policies"], limit: 5);

        await qdrant.Received().SearchAsync(
            handbook, "leave", Arg.Any<float[]>(), Arg.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new[] { "hr", "policies" })),
            null, Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Searching_an_unknown_collection_fails_without_searching()
    {
        await Assert.ThrowsAsync<McpException>(() => tools.SearchAsync(Guid.NewGuid(), "leave"));

        await qdrant.DidNotReceiveWithAnyArgs().SearchAsync(default, default!, default!, default!, default, default, default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Searching_needs_a_limit_from_1_to_50(int limit)
    {
        var handbook = await store.CreateCollectionAsync("handbook", null, false, Root);

        await Assert.ThrowsAsync<McpException>(() => tools.SearchAsync(handbook, "leave", limit: limit));
    }

    [Fact]
    public async Task Reading_a_document_returns_its_markdown()
    {
        var handbook = await store.CreateCollectionAsync("handbook", null, false, Root);
        var stored = await store.StoreAsync(handbook, "leave.md", Result("# Leave"), [], Root);

        Assert.Equal("# Leave", (await tools.GetDocumentAsync(stored.Id)).Markdown);
        await Assert.ThrowsAsync<McpException>(() => tools.GetDocumentAsync(Guid.NewGuid()));
    }

    private static IngestResult Result(string markdown) =>
        new(markdown, [new DocumentChunk(markdown, [0.1f, 0.2f], "", 0, null, null)], DocumentKind.Text);

    public Task DisposeAsync()
    {
        database.Dispose();
        if (Directory.Exists(contentRoot))
        {
            Directory.Delete(contentRoot, recursive: true);
        }

        return Task.CompletedTask;
    }
}
