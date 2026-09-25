using Microsoft.AspNetCore.Authorization;

namespace Lantrn.Services;

// Who may search and read documents: anyone signed in, or everyone while public search is on.
// Read from the settings on every check, so turning it off applies to the next request.
public sealed class SearchAccess(SettingsStore settings) : AuthorizationHandler<SearchAccess.Requirement>
{
    public const string Policy = "Search";

    public sealed class Requirement : IAuthorizationRequirement;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, Requirement requirement)
    {
        if (settings.Current.Access.PublicSearch || context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
