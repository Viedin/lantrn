using Lantrn.Components.Shared;
using Lantrn.Services;
using Lantrn.Test.Support;

namespace Lantrn.Test.Accounts;

// Collections are listed to everyone who can read them, so the owner label decides whose email address gets shown.
public class CollectionLabelsTests
{
    [Fact]
    public void Only_admins_see_the_owners_email()
    {
        var collection = Collection("notes", "alice", "alice@example.com");

        Assert.Equal("alice@example.com", CollectionLabels.Owner(collection, Users.Admin("admin")));
        Assert.Equal("alice", CollectionLabels.Owner(collection, Users.SignedIn("bob")));
        Assert.Equal("User", CollectionLabels.Owner(collection, Users.Guest));
    }

    [Fact]
    public void Owners_see_their_own_collections_as_theirs()
    {
        var collection = Collection("notes", "alice", "alice@example.com", ownerIsAdmin: true);

        Assert.Equal("You", CollectionLabels.Owner(collection, Users.Admin("alice")));
    }

    [Fact]
    public void An_admins_collection_belongs_to_the_workspace()
    {
        var collection = Collection("handbook", "root", "root@example.com", ownerIsAdmin: true);

        Assert.Equal("Admin", CollectionLabels.Owner(collection, Users.SignedIn("bob")));
    }

    [Fact]
    public void A_name_is_only_qualified_when_another_collection_shares_it()
    {
        var alices = Collection("notes", "alice", "alice@example.com");
        var bobs = Collection("notes", "bob", "bob@example.com");
        var other = Collection("recipes", "bob", "bob@example.com");
        var among = new[] { alices, bobs, other };

        Assert.Equal("notes · alice", CollectionLabels.Name(alices, among, Users.SignedIn("carol")));
        Assert.Equal("notes · You", CollectionLabels.Name(bobs, among, Users.SignedIn("bob")));
        Assert.Equal("recipes", CollectionLabels.Name(other, among, Users.SignedIn("carol")));
    }

    [Fact]
    public void Two_admins_namesakes_fall_back_to_the_person()
    {
        var first = Collection("handbook", "root", "root@example.com", ownerIsAdmin: true);
        var second = Collection("handbook", "ops", "ops@example.com", ownerIsAdmin: true);
        var among = new[] { first, second };

        Assert.Equal("handbook · root", CollectionLabels.Name(first, among, Users.SignedIn("carol")));
        Assert.Equal("handbook · ops", CollectionLabels.Name(second, among, Users.SignedIn("carol")));
    }

    private static CollectionSummary Collection(string name, string ownerId, string ownerEmail, bool ownerIsAdmin = false) =>
        new(Guid.NewGuid(), name, null, false, ownerId, ownerEmail, ownerIsAdmin, DateTime.UtcNow, 0, 0, null);
}
