using Lantrn.Services;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Lantrn.Api;

// Describes the API as a whole: what it is for, and the API key every operation needs.
internal sealed class PublicApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public const string CollectionsTag = "Collections";

    public const string DocumentsTag = "Documents";

    public const string SecurityScheme = "ApiKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Lantrn API",
            Version = PublicApi.DocumentName,
            Description =
                "Manage collections and ingest documents from scripts and integrations. Every request is made as the " +
                "admin who created the API key, and errors are returned as RFC 7807 problem details.",
        };

        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = CollectionsTag, Description = "Separately searchable sets of documents." },
            new() { Name = DocumentsTag, Description = "Adding documents to a collection, and managing the ones it holds." },
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SecurityScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = $"{ApiKeyService.KeyPrefix}…",
            Description = "An API key created under API keys in Lantrn, sent as \"Authorization: Bearer <key>\". " +
                "The key acts as its user, who must be an admin.",
        };

        document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SecurityScheme, document)] = [] }];

        return Task.CompletedTask;
    }
}

// The responses every operation can give because of its key, so each one lists them.
internal sealed class PublicApiOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "The API key is missing, unknown or revoked." });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "The key's user is not an admin." });
        return Task.CompletedTask;
    }
}
