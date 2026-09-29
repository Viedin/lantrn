using System.Security.Claims;
using Lantrn.Infra;
using Lantrn.Services;
using Lantrn.Services.Accounts;
using Lantrn.Test.Support;
using Microsoft.EntityFrameworkCore;

namespace Lantrn.Test.Accounts;

// The rules exist twice: as a database filter for lists and in memory for a single collection. Both run the same table,
// so they can't drift apart and let someone see a collection in one place they are refused in another.
public class CollectionAccessRulesTests
{
    private const string OwnerId = "owner";

    public static TheoryData<string, bool, CollectionAccess, bool> Rules => new()
    {
        { "admin", true, CollectionAccess.Read, true },
        { "admin", true, CollectionAccess.Contribute, true },
        { "admin", true, CollectionAccess.Manage, true },
        { "admin", false, CollectionAccess.Manage, true },

        { "owner", true, CollectionAccess.Read, true },
        { "owner", true, CollectionAccess.Contribute, true },
        { "owner", true, CollectionAccess.Manage, true },
        { "owner", false, CollectionAccess.Manage, true },

        { "other", true, CollectionAccess.Read, false },
        { "other", true, CollectionAccess.Contribute, false },
        { "other", true, CollectionAccess.Manage, false },
        { "other", false, CollectionAccess.Read, true },
        { "other", false, CollectionAccess.Contribute, true },
        { "other", false, CollectionAccess.Manage, false },

        { "guest", true, CollectionAccess.Read, false },
        { "guest", true, CollectionAccess.Contribute, false },
        { "guest", true, CollectionAccess.Manage, false },
        { "guest", false, CollectionAccess.Read, true },
        { "guest", false, CollectionAccess.Contribute, false },
        { "guest", false, CollectionAccess.Manage, false },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Allows_follows_the_rules(string viewer, bool isPrivate, CollectionAccess access, bool expected)
    {
        var collection = new CollectionSummary(
            Guid.NewGuid(), "notes", null, isPrivate, OwnerId, null, false, DateTime.UtcNow, 0, 0, null);

        Assert.Equal(expected, collection.Allows(Viewer(viewer), access));
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task WhereAllowed_follows_the_rules(string viewer, bool isPrivate, CollectionAccess access, bool expected)
    {
        using var database = new TestDatabase();
        await using (var db = database.CreateContext())
        {
            db.Users.Add(new ApplicationUser { Id = OwnerId, UserName = "owner@example.com" });
            db.Collections.Add(new Collection { Name = "notes", OwnerId = OwnerId, IsPrivate = isPrivate });
            await db.SaveChangesAsync();
        }

        await using var query = database.CreateContext();
        var found = await query.Collections.AsNoTracking().WhereAllowed(Viewer(viewer), access).AnyAsync();

        Assert.Equal(expected, found);
    }

    private static ClaimsPrincipal Viewer(string viewer) => viewer switch
    {
        "admin" => Users.Admin("admin"),
        "owner" => Users.SignedIn(OwnerId),
        "other" => Users.SignedIn("other"),
        _ => Users.Guest,
    };
}
