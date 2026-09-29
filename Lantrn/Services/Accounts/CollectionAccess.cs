using System.Security.Claims;
using Lantrn.Infra;

namespace Lantrn.Services.Accounts;

public enum CollectionAccess
{
    // Search it and read its documents.
    Read,

    // Add, retag and remove its documents.
    Contribute,

    // Change its visibility, re-embed, clear or delete it.
    Manage,
}

// Admins reach every collection. Users read and contribute to public collections and their own, and manage only their own.
// Guests aren't signed in, so they only read public collections.
public static class CollectionAccessRules
{
    public static IQueryable<Collection> WhereAllowed(this IQueryable<Collection> collections, ClaimsPrincipal user, CollectionAccess access)
    {
        if (user.IsInRole(Roles.Admin))
        {
            return collections;
        }

        var userId = UserId(user);
        return access switch
        {
            CollectionAccess.Read => collections.Where(c => !c.IsPrivate || (userId != null && c.OwnerId == userId)),
            CollectionAccess.Contribute => collections.Where(c => userId != null && (!c.IsPrivate || c.OwnerId == userId)),
            _ => collections.Where(c => userId != null && c.OwnerId == userId),
        };
    }

    public static bool Allows(this CollectionSummary collection, ClaimsPrincipal user, CollectionAccess access)
    {
        if (user.IsInRole(Roles.Admin))
        {
            return true;
        }

        var userId = UserId(user);
        var owns = userId is not null && collection.OwnerId == userId;
        return access switch
        {
            CollectionAccess.Read => !collection.IsPrivate || owns,
            CollectionAccess.Contribute => userId is not null && (!collection.IsPrivate || owns),
            _ => owns,
        };
    }

    public static string? UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);

    // For what only signed-in people can do, such as adding documents, which is always credited to someone.
    public static string SignedInUserId(ClaimsPrincipal user) =>
        UserId(user) ?? throw new InvalidOperationException("Only signed-in users can do this.");
}
