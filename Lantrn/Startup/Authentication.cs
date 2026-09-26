using Lantrn.Infra;
using Lantrn.Services.Accounts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Lantrn.Startup;

public static class Authentication
{
    public static IServiceCollection AddSiteAuthentication(this IServiceCollection services)
    {
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, RevalidatingAuthenticationStateProvider>();
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();
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

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/account/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            // Search is the home page; when it isn't public it sends the visitor on to sign in.
            return Results.LocalRedirect("~/");
        }).WithMetadata(new RequireAntiforgeryTokenAttribute());

        return app;
    }
}
