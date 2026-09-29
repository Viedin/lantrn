using System.Security.Claims;
using Lantrn.Infra;
using Lantrn.Services;
using Lantrn.Services.Accounts;

namespace Lantrn.Components.Shared;

// How a collection and its owner are named to the person looking at them.
public static class CollectionLabels
{
    // Admins see the owner's email; everyone else only the part before the @, so addresses aren't handed around.
    public static string Owner(CollectionSummary collection, ClaimsPrincipal viewer)
    {
        if (collection.OwnerId is null)
        {
            return "Admins";
        }
        if (collection.OwnerId == CollectionAccessRules.UserId(viewer))
        {
            return "You";
        }

        var email = collection.OwnerEmail ?? string.Empty;
        return viewer.IsInRole(Roles.Admin) ? email : email.Split('@')[0];
    }

    // Names repeat between owners, so a name shared with another collection in the list gets its owner added.
    public static string Name(CollectionSummary collection, IReadOnlyList<CollectionSummary> among, ClaimsPrincipal viewer) =>
        among.Count(c => c.Name == collection.Name) > 1 ? $"{collection.Name} · {Owner(collection, viewer)}" : collection.Name;
}
