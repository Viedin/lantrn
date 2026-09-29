using System.Security.Claims;
using Lantrn.Services;
using Lantrn.Services.Accounts;
using Lantrn.Test.Support;
using Microsoft.AspNetCore.Authorization;

namespace Lantrn.Test.Accounts;

public class SearchAccessTests : IDisposable
{
    private readonly TestDatabase database = new();

    [Fact]
    public async Task Guests_are_refused_while_search_is_private()
    {
        var store = await TestSettings.LoadAsync(database, s => s.Access.PublicSearch = false);

        Assert.False(await AllowsAsync(store, Users.Guest));
        Assert.True(await AllowsAsync(store, Users.SignedIn("alice")));
    }

    [Fact]
    public async Task Guests_may_search_while_search_is_public()
    {
        var store = await TestSettings.LoadAsync(database, s => s.Access.PublicSearch = true);

        Assert.True(await AllowsAsync(store, Users.Guest));
    }

    [Fact]
    public async Task Turning_public_search_off_applies_to_the_next_check()
    {
        var store = await TestSettings.LoadAsync(database, s => s.Access.PublicSearch = true);
        var settings = store.Current.Copy();
        settings.Access.PublicSearch = false;

        await store.SaveAsync(settings);

        Assert.False(await AllowsAsync(store, Users.Guest));
    }

    private static async Task<bool> AllowsAsync(SettingsStore store, ClaimsPrincipal user)
    {
        var context = new AuthorizationHandlerContext([new SearchAccess.Requirement()], user, null);
        await new SearchAccess(store).HandleAsync(context);
        return context.HasSucceeded;
    }

    public void Dispose() => database.Dispose();
}
