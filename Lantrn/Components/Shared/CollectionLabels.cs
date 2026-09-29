using System.Security.Claims;
using Lantrn.Infra;
using Lantrn.Services;
using Lantrn.Services.Accounts;

namespace Lantrn.Components.Shared;

// How collections and the people behind them are named to the person looking at them.
public static class CollectionLabels
{
    // An admin's collections belong to the workspace rather than to a person.
    public static string Owner(CollectionSummary collection, ClaimsPrincipal viewer) =>
        collection.OwnerIsAdmin && collection.OwnerId != CollectionAccessRules.UserId(viewer)
            ? "Admin"
            : Person(collection.OwnerId, collection.OwnerEmail, viewer);

    // Admins see the email; other users only the part before the @, so addresses aren't handed around.
    // Guests aren't told who anyone is.
    public static string Person(string userId, string? email, ClaimsPrincipal viewer)
    {
        if (viewer.Identity?.IsAuthenticated != true)
        {
            return "User";
        }
        if (userId == CollectionAccessRules.UserId(viewer))
        {
            return "You";
        }

        email ??= string.Empty;
        return viewer.IsInRole(Roles.Admin) ? email : email.Split('@')[0];
    }

    // Names repeat between owners, so a name shared with another collection in the list gets its owner added.
    // Two admins' collections would both read "Admin", so those fall back to the person.
    public static string Name(CollectionSummary collection, IReadOnlyList<CollectionSummary> among, ClaimsPrincipal viewer)
    {
        var namesakes = among.Where(c => c.Name == collection.Name).ToList();
        if (namesakes.Count == 1)
        {
            return collection.Name;
        }

        var owner = Owner(collection, viewer);
        if (namesakes.Count(c => Owner(c, viewer) == owner) > 1)
        {
            owner = Person(collection.OwnerId, collection.OwnerEmail, viewer);
        }
        return $"{collection.Name} · {owner}";
    }
}
