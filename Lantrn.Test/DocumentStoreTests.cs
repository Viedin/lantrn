using Lantrn.Infra;
using Lantrn.Services;
using Lantrn.Services.Accounts;
using Lantrn.Services.Ingestion;
using Lantrn.Services.Search;
using Lantrn.Test.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Lantrn.Test;

// The store keeps the database and Qdrant in step, so these check both sides of each change.
public class DocumentStoreTests : IDisposable
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Root = "root";

    private readonly TestDatabase database = new();
    private readonly IQdrantStore qdrant = Substitute.For<IQdrantStore>();
    private readonly string contentRoot = Path.Combine(Path.GetTempPath(), $"lantrn-tests-{Guid.NewGuid():N}");
    private readonly DocumentStore store;

    public DocumentStoreTests()
    {
        using (var db = database.CreateContext())
        {
            db.Users.AddRange(new ApplicationUser { Id = Alice }, new ApplicationUser { Id = Bob }, new ApplicationUser { Id = Root });
            db.SaveChanges();
        }

        qdrant.ReplaceDocumentAsync(default, default!, default, default, default!, default!, default)
            .ReturnsForAnyArgs(call => call.ArgAt<IReadOnlyList<DocumentChunk>>(4).Count);

        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(contentRoot);
        store = new DocumentStore(database.Factory, qdrant, Options.Create(new StorageOptions()), environment, NullLogger<DocumentStore>.Instance);
    }

    [Fact]
    public async Task Re_ingesting_a_source_replaces_the_document_and_its_tags()
    {
        var collection = await store.CreateCollectionAsync("notes", null, false, Alice);
        var first = await store.StoreAsync(collection, "guide.md", Result("old"), ["draft", "guide"], Alice);

        var second = await store.StoreAsync(collection, "guide.md", Result("new"), ["guide", "final"], Bob);

        Assert.Equal(first.Id, second.Id);
        var document = Assert.Single(await store.ListDocumentsAsync(collection));
        Assert.Equal(["final", "guide"], document.Tags);
        Assert.Equal(Bob, document.AddedById);
        Assert.Equal("new", (await store.GetAsync(first.Id))!.Markdown);
    }

    [Fact]
    public async Task Nothing_reaches_qdrant_for_a_collection_that_is_gone()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.StoreAsync(Guid.NewGuid(), "guide.md", Result("text"), [], Alice));

        await qdrant.DidNotReceiveWithAnyArgs().ReplaceDocumentAsync(default, default!, default, default, default!, default!, default);
    }

    [Fact]
    public async Task Nothing_reaches_qdrant_for_an_uploader_who_is_gone()
    {
        var collection = await store.CreateCollectionAsync("notes", null, false, Alice);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.StoreAsync(collection, "guide.md", Result("text"), [], "removed-user"));

        await qdrant.DidNotReceiveWithAnyArgs().ReplaceDocumentAsync(default, default!, default, default, default!, default!, default);
    }

    [Fact]
    public async Task Tags_stay_as_they_were_when_qdrant_cant_be_updated()
    {
        var collection = await store.CreateCollectionAsync("notes", null, false, Alice);
        var stored = await store.StoreAsync(collection, "guide.md", Result("text"), ["draft"], Alice);
        qdrant.SetTagsAsync(collection, stored.Id, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Qdrant is down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => store.SetTagsAsync(stored.Id, ["final"]));

        Assert.Equal(["draft"], (await store.ListDocumentsAsync(collection)).Single().Tags);
    }

    [Fact]
    public async Task Deleting_a_document_removes_its_points_and_its_kept_original()
    {
        var collection = await store.CreateCollectionAsync("scans", null, false, Alice);
        var stored = await store.StoreAsync(collection, "scan.pdf", Result("text"), [], Alice, original: [1, 2, 3]);
        var original = (await store.GetOriginalAsync(stored.Id))!.Path;

        await store.DeleteAsync(stored.Id);

        await qdrant.Received().DeleteDocumentAsync(collection, stored.Id, Arg.Any<CancellationToken>());
        Assert.Null(await store.GetAsync(stored.Id));
        Assert.False(File.Exists(original));
    }

    [Fact]
    public async Task Re_ingesting_without_an_original_removes_the_old_one()
    {
        var collection = await store.CreateCollectionAsync("scans", null, false, Alice);
        var stored = await store.StoreAsync(collection, "scan.pdf", Result("text"), [], Alice, original: [1, 2, 3]);
        var original = (await store.GetOriginalAsync(stored.Id))!.Path;

        await store.StoreAsync(collection, "scan.pdf", Result("text"), [], Alice);

        Assert.Null(await store.GetOriginalAsync(stored.Id));
        Assert.False(File.Exists(original));
    }

    [Fact]
    public async Task Clearing_a_collection_keeps_it_but_drops_its_documents_vectors_and_originals()
    {
        var collection = await store.CreateCollectionAsync("scans", null, false, Alice);
        var stored = await store.StoreAsync(collection, "scan.pdf", Result("text"), ["tax"], Alice, original: [1, 2, 3]);
        var original = (await store.GetOriginalAsync(stored.Id))!.Path;

        await store.ClearCollectionAsync(collection);

        await qdrant.Received().DeleteCollectionAsync(collection, Arg.Any<CancellationToken>());
        Assert.Empty(await store.ListDocumentsAsync(collection));
        Assert.Empty(await store.ListTagsAsync(collection));
        Assert.False(File.Exists(original));
        Assert.True(await store.CanAccessAsync(collection, Users.Admin(Root), CollectionAccess.Manage));
    }

    [Fact]
    public async Task Handing_over_content_renames_collections_whose_names_are_taken()
    {
        await store.CreateCollectionAsync("docs", null, false, Root);
        await store.CreateCollectionAsync("docs-2", null, false, Root);
        var docs = await store.CreateCollectionAsync("docs", null, true, Alice);
        await store.CreateCollectionAsync("notes", null, false, Alice);
        var stored = await store.StoreAsync(docs, "guide.md", Result("text"), [], Alice);

        await store.TransferContentAsync(Alice, Root);

        await using var db = database.CreateContext();
        var names = await db.Collections.AsNoTracking().Where(c => c.OwnerId == Root).Select(c => c.Name).ToListAsync();
        Assert.Equal(["docs", "docs-2", "docs-3", "notes"], names.Order());
        Assert.False(await db.Collections.AsNoTracking().AnyAsync(c => c.OwnerId == Alice));
        Assert.Equal(Root, (await store.GetAsync(stored.Id))!.AddedById);
    }

    [Fact]
    public async Task A_renamed_collection_keeps_a_valid_name()
    {
        var longName = new string('a', 63);
        await store.CreateCollectionAsync(longName, null, false, Root);
        await store.CreateCollectionAsync(longName, null, false, Alice);

        await store.TransferContentAsync(Alice, Root);

        await using var db = database.CreateContext();
        var renamed = await db.Collections.AsNoTracking().Where(c => c.Name != longName).Select(c => c.Name).SingleAsync();
        Assert.EndsWith("-2", renamed);
        Assert.True(DocumentStore.IsValidCollectionName(renamed));
    }

    [Fact]
    public async Task Removing_a_users_content_takes_their_collections_and_what_they_added_elsewhere()
    {
        var alices = await store.CreateCollectionAsync("mine", null, false, Alice);
        var bobs = await store.CreateCollectionAsync("shared", null, false, Bob);
        await store.StoreAsync(alices, "a.md", Result("text"), [], Alice);
        var added = await store.StoreAsync(bobs, "from-alice.md", Result("text"), [], Alice);
        var kept = await store.StoreAsync(bobs, "from-bob.md", Result("text"), [], Bob);

        await store.DeleteContentOfAsync(Alice);

        await qdrant.Received().DeleteCollectionAsync(alices, Arg.Any<CancellationToken>());
        await qdrant.Received().DeleteDocumentAsync(bobs, added.Id, Arg.Any<CancellationToken>());
        await qdrant.DidNotReceive().DeleteDocumentAsync(bobs, kept.Id, Arg.Any<CancellationToken>());
        Assert.Equal(["from-bob.md"], (await store.ListDocumentsAsync(bobs)).Select(d => d.Source));
        Assert.False(await store.CanAccessAsync(alices, Users.Admin(Root), CollectionAccess.Read));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Docs")]
    [InlineData("-docs")]
    [InlineData("my docs")]
    [InlineData("../docs")]
    public async Task Collection_names_are_lowercase_slugs(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateCollectionAsync(name, null, false, Alice));
    }

    [Fact]
    public async Task Collection_names_are_unique_per_owner()
    {
        await store.CreateCollectionAsync("docs", null, false, Alice);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateCollectionAsync("docs", null, false, Alice));
        await store.CreateCollectionAsync("docs", null, false, Bob);
    }

    private static IngestResult Result(string markdown) =>
        new(markdown, [new DocumentChunk(markdown, [0.1f, 0.2f], "", 0, null, null)], DocumentKind.Text);

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(contentRoot))
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }
}
