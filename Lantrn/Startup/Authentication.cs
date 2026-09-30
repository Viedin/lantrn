using Lantrn.Components.Account;
using Lantrn.Infra;
using Lantrn.Services.Accounts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Lantrn.Startup;

public static class Authentication
{
    public static IServiceCollection AddSiteAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var oidcSection = configuration.GetSection(OidcOptions.SectionName);
        var oidc = oidcSection.Get<OidcOptions>() ?? new OidcOptions();
        services.Configure<OidcOptions>(oidcSection);

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, RevalidatingAuthenticationStateProvider>();
        var authentication = services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            });
        authentication.AddIdentityCookies();
        if (oidc.Enabled)
        {
            authentication.AddOpenIdConnect(OidcOptions.Scheme, oidc.DisplayName, options => ConfigureOidc(options, oidc, environment));
        }
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.AccessDeniedPath = "/account/access-denied";
        });
        // Cookies pick up role changes and removed users as quickly as open circuits do.
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = RevalidatingAuthenticationStateProvider.Interval);
        services.AddSingleton<IAuthorizationHandler, SearchAccess>();
        services.AddAuthorizationBuilder()
            .AddPolicy(SearchAccess.Policy, policy => policy.AddRequirements(new SearchAccess.Requirement()));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<DatabaseContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        return services;
    }

    private static void ConfigureOidc(OpenIdConnectOptions options, OidcOptions oidc, IHostEnvironment environment)
    {
        options.Authority = oidc.Authority;
        options.ClientId = oidc.ClientId;
        options.ClientSecret = oidc.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        // The ID token is kept in the site cookie, where signing out finds it to end the provider's session too.
        options.SaveTokens = true;
        options.SignOutScheme = IdentityConstants.ApplicationScheme;
        options.RequireHttpsMetadata = !environment.IsDevelopment();
        options.GetClaimsFromUserInfoEndpoint = true;
        // Keep the provider's claim names (sub, email, groups) instead of mapping them to the long ClaimTypes URIs.
        options.MapInboundClaims = false;
        options.ClaimActions.MapUniqueJsonKey("email_verified", "email_verified");
        options.ClaimActions.MapJsonKey(OidcOptions.GroupsClaim, OidcOptions.GroupsClaim);

        options.Scope.Clear();
        foreach (var scope in oidc.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            options.Scope.Add(scope);
        }

        // Cancelling at the provider or a misconfigured client would otherwise end on the error page.
        options.Events.OnRemoteFailure = context =>
        {
            LogFailure(context.HttpContext, context.Failure);
            context.Response.Redirect($"/account/login?error={ExternalSignInError.Failed}");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }

    private static void LogFailure(HttpContext context, Exception? failure) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(Authentication))
            .LogWarning(failure, "Talking to the single sign-on provider failed");

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/account/logout", async (HttpContext context, SignInManager<ApplicationUser> signInManager, IOptions<OidcOptions> oidc) =>
        {
            // Otherwise the provider still knows the person, and the sign-in button lets them, or the next person
            // at the same computer, straight back in as them.
            if (oidc.Value.Enabled && await context.GetTokenAsync(IdentityConstants.ApplicationScheme, OpenIdConnectParameterNames.IdToken) is not null)
            {
                try
                {
                    await context.SignOutAsync(OidcOptions.Scheme, new AuthenticationProperties { RedirectUri = "/" });
                    await signInManager.SignOutAsync();
                    return Results.Empty;
                }
                // The provider is unreachable or has no end-session endpoint; signing out of Lantrn still works.
                catch (InvalidOperationException ex)
                {
                    LogFailure(context, ex);
                }
            }

            await signInManager.SignOutAsync();
            // Search is the home page; when it isn't public it sends the visitor on to sign in.
            return Results.LocalRedirect("~/");
        }).WithMetadata(new RequireAntiforgeryTokenAttribute());

        if (app.ServiceProvider.GetRequiredService<IOptions<OidcOptions>>().Value.Enabled)
        {
            MapExternalLoginEndpoints(app);
        }

        return app;
    }

    private static void MapExternalLoginEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/account/external-login", async (
            [FromForm] string? returnUrl, [FromForm] bool? switchAccount, HttpContext context, SignInManager<ApplicationUser> signInManager) =>
        {
            var callback = $"/account/external-login/callback?returnUrl={Uri.EscapeDataString(ReturnUrl.Safe(returnUrl))}";
            var properties = signInManager.ConfigureExternalAuthenticationProperties(OidcOptions.Scheme, callback);
            if (switchAccount == true)
            {
                properties.SetParameter(OpenIdConnectParameterNames.Prompt, "login");
            }
            try
            {
                await context.ChallengeAsync(OidcOptions.Scheme, properties);
            }
            // The provider's discovery document couldn't be fetched: it's down, or the authority is wrong.
            catch (InvalidOperationException ex)
            {
                LogFailure(context, ex);
                context.Response.Redirect($"/account/login?error={ExternalSignInError.Failed}");
            }
        });

        app.MapGet("/account/external-login/callback", async (
            string? returnUrl, HttpContext context, SignInManager<ApplicationUser> signInManager, AccountService accounts) =>
        {
            if (await signInManager.GetExternalLoginInfoAsync() is not { } info)
            {
                return Results.LocalRedirect($"~/account/login?error={ExternalSignInError.Failed}");
            }

            var (user, error, email) = await accounts.SignInExternalAsync(info);
            await context.SignOutAsync(IdentityConstants.ExternalScheme);
            if (user is null)
            {
                var emailQuery = email is null ? "" : $"&email={Uri.EscapeDataString(email)}";
                return Results.LocalRedirect($"~/account/login?error={error}{emailQuery}");
            }

            var properties = new AuthenticationProperties();
            properties.StoreTokens(info.AuthenticationTokens?.Where(t => t.Name == OpenIdConnectParameterNames.IdToken) ?? []);
            await signInManager.SignInAsync(user, properties, info.LoginProvider);
            return Results.LocalRedirect(ReturnUrl.Safe(returnUrl));
        });
    }
}
