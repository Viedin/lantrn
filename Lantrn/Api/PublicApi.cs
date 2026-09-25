using System.Text.Json.Serialization;
using Lantrn.Infra;
using Lantrn.Services;
using Microsoft.AspNetCore.Authentication;

namespace Lantrn.Api;

// The public API: everything an admin does to collections and documents in the workspace, for scripts and integrations.
// Every endpoint needs an admin's API key; the OpenAPI document and the reference page are public.
public static class PublicApi
{
    public const string Policy = "PublicApi";

    public const string DocumentName = "v1";

    public const string BasePath = "/api/v1";

    public const string SpecPath = $"/api/openapi/{DocumentName}.json";

    public const string ReferencePath = "/api/reference";

    public static IServiceCollection AddPublicApi(this IServiceCollection services)
    {
        services.AddSingleton<ApiKeyService>();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

        // Only the key counts: a signed-in browser's cookie must not be able to call the API without antiforgery.
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
                .RequireRole(Roles.Admin));

        services.AddProblemDetails();
        services.AddValidation();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddOpenApi(DocumentName, options =>
        {
            // The site's own endpoints (sign-out, the original viewer) are not part of the API.
            options.ShouldInclude = description => description.RelativePath?.StartsWith("api/v1/") == true;
            options.AddDocumentTransformer<PublicApiDocumentTransformer>();
            options.AddOperationTransformer<PublicApiOperationTransformer>();
        });

        return services;
    }

    public static bool IsApiRequest(HttpContext context) => context.Request.Path.StartsWithSegments(BasePath);

    public static IEndpointRouteBuilder MapPublicApi(this IEndpointRouteBuilder app)
    {
        app.MapOpenApi(SpecPath.Replace(DocumentName, "{documentName}"));

        // Keys, not cookies, so there is no form post to forge.
        var api = app.MapGroup(BasePath)
            .RequireAuthorization(Policy)
            .DisableAntiforgery();

        api.MapGroup("/collections")
            .WithTags(PublicApiDocumentTransformer.CollectionsTag)
            .MapCollectionEndpoints();

        api.MapGroup("")
            .WithTags(PublicApiDocumentTransformer.DocumentsTag)
            .MapDocumentEndpoints();

        return app;
    }
}
