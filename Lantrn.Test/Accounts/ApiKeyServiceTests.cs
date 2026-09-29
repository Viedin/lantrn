using Lantrn.Infra;
using Lantrn.Services.Accounts;
using Lantrn.Test.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantrn.Test.Accounts;

public class ApiKeyServiceTests : IDisposable
{
    private const string Alice = "alice";
    private const string Bob = "bob";

    private readonly TestDatabase database = new();
    private readonly ApiKeyService keys;

    public ApiKeyServiceTests()
    {
        using var db = database.CreateContext();
        db.Users.AddRange(new ApplicationUser { Id = Alice }, new ApplicationUser { Id = Bob });
        db.SaveChanges();

        keys = new ApiKeyService(database.Factory, NullLogger<ApiKeyService>.Instance);
    }

    [Fact]
    public async Task A_created_key_signs_in_as_its_user()
    {
        var key = await keys.CreateAsync(Alice, "CI");

        Assert.StartsWith(ApiKeyService.KeyPrefix, key);
        Assert.Equal(Alice, await keys.FindUserIdAsync(key));
    }

    [Fact]
    public async Task Only_the_hash_is_stored()
    {
        var key = await keys.CreateAsync(Alice, "CI");

        await using var db = database.CreateContext();
        var stored = await db.ApiKeys.AsNoTracking().SingleAsync();
        Assert.DoesNotContain(key, new[] { stored.KeyHash, stored.Prefix, stored.Name });
        Assert.StartsWith(stored.Prefix, key);
    }

    [Fact]
    public async Task Unknown_and_tampered_keys_are_refused()
    {
        var key = await keys.CreateAsync(Alice, "CI");

        Assert.Null(await keys.FindUserIdAsync(key[..^1]));
        Assert.Null(await keys.FindUserIdAsync(key.ToUpperInvariant()));
        Assert.Null(await keys.FindUserIdAsync(key[ApiKeyService.KeyPrefix.Length..]));
        Assert.Null(await keys.FindUserIdAsync(""));
    }

    [Fact]
    public async Task A_revoked_key_stops_working()
    {
        var key = await keys.CreateAsync(Alice, "CI");
        var id = (await keys.ListAsync(Alice)).Single().Id;

        await keys.RevokeAsync(Alice, id);

        Assert.Null(await keys.FindUserIdAsync(key));
    }

    [Fact]
    public async Task Nobody_revokes_someone_elses_key()
    {
        var key = await keys.CreateAsync(Alice, "CI");
        var id = (await keys.ListAsync(Alice)).Single().Id;

        await keys.RevokeAsync(Bob, id);

        Assert.Equal(Alice, await keys.FindUserIdAsync(key));
    }

    [Fact]
    public async Task Users_only_list_their_own_keys()
    {
        await keys.CreateAsync(Alice, "Alice's");
        await keys.CreateAsync(Bob, "Bob's");

        var listed = await keys.ListAsync(Alice);

        Assert.Equal("Alice's", Assert.Single(listed).Name);
    }

    [Fact]
    public async Task Using_a_key_records_when()
    {
        var key = await keys.CreateAsync(Alice, "CI");

        await keys.FindUserIdAsync(key);

        Assert.NotNull((await keys.ListAsync(Alice)).Single().LastUsedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_key_needs_a_name(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => keys.CreateAsync(Alice, name));
    }

    public void Dispose() => database.Dispose();
}
