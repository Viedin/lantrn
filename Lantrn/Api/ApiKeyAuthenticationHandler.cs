using System.Text.Encodings.Web;
using Lantrn.Infra;
using Lantrn.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Lantrn.Api;

// Signs API requests in as the owner of the key in "Authorization: Bearer lnt_...". The principal is built from the
// user as they are now, so a role taken away or an account removed applies to the very next request.
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ApiKeyService keys,
    UserManager<ApplicationUser> users,
    IUserClaimsPrincipalFactory<ApplicationUser> principals)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ApiKey";

    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var userId = await keys.FindUserIdAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        if (userId is null || await users.FindByIdAsync(userId) is not { } user)
        {
            return AuthenticateResult.Fail("Unknown or revoked API key.");
        }

        var principal = await principals.CreateAsync(user);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.Append(HeaderNames.WWWAuthenticate, "Bearer");
        return Task.CompletedTask;
    }
}
