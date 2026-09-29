using System.Security.Claims;
using Lantrn.Services;
using Lantrn.Services.Accounts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Lantrn.Api;

// /api/v1/collections: creating, inspecting, clearing and deleting collections.
public static class CollectionEndpoints
{
    public static RouteGroupBuilder MapCollectionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", ListAsync)
            .WithName("ListCollections")
            .WithSummary("List collections")
            .WithDescription("Every collection, in name order, with how many documents and chunks it holds.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetCollection")
            .WithSummary("Get a collection")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .WithName("CreateCollection")
            .WithSummary("Create a collection")
            .WithDescription(
                "Creates an empty collection owned by the key's user. Names are unique per owner. " +
                "Documents are added to it with the ingest endpoints.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteCollection")
            .WithSummary("Delete a collection")
            .WithDescription("Deletes the collection with all its documents, vectors and kept originals.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}/documents", ClearAsync)
            .WithName("ClearCollection")
            .WithSummary("Clear a collection")
            .WithDescription("Removes every document from the collection but keeps the collection itself.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/tags", ListTagsAsync)
            .WithName("ListCollectionTags")
            .WithSummary("List a collection's tags")
            .WithDescription("Every tag used in the collection, with how many documents carry it.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<CollectionResponse>>> ListAsync(
        ClaimsPrincipal user, DocumentStore documents, CancellationToken cancellationToken)
    {
        var collections = await documents.ListCollectionsAsync(user, CollectionAccess.Read, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<CollectionResponse>>(collections.Select(CollectionResponse.From).ToList());
    }

    private static async Task<Results<Ok<CollectionResponse>, ProblemHttpResult>> GetAsync(
        Guid id, ClaimsPrincipal user, DocumentStore documents, CancellationToken cancellationToken) =>
        await documents.GetCollectionAsync(id, user, CollectionAccess.Read, cancellationToken) is { } collection
            ? TypedResults.Ok(CollectionResponse.From(collection))
            : ApiProblems.CollectionNotFound(id);

    private static async Task<Results<Created<CollectionResponse>, ProblemHttpResult>> CreateAsync(
        CreateCollectionRequest request, ClaimsPrincipal user, DocumentStore documents, CancellationToken cancellationToken)
    {
        Guid id;
        try
        {
            id = await documents.CreateCollectionAsync(
                request.Name, request.Description, request.IsPrivate, CollectionAccessRules.SignedInUserId(user), cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ApiProblems.Conflict(ex.Message);
        }

        var created = await documents.GetCollectionAsync(id, user, CollectionAccess.Read, cancellationToken);
        return TypedResults.Created($"/api/v1/collections/{id}", CollectionResponse.From(created!));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, ClaimsPrincipal user, DocumentStore documents, CancellationToken cancellationToken)
    {
        if (!await documents.CanAccessAsync(id, user, CollectionAccess.Manage, cancellationToken))
        {
            return ApiProblems.CollectionNotFound(id);
        }

        // Not cancellable: stopping halfway would leave the database and Qdrant out of step.
        await documents.DeleteCollectionAsync(id, CancellationToken.None);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ClearAsync(
        Guid id, ClaimsPrincipal user, DocumentStore documents, CancellationToken cancellationToken)
    {
        if (!await documents.CanAccessAsync(id, user, CollectionAccess.Manage, cancellationToken))
        {
            return ApiProblems.CollectionNotFound(id);
        }

        await documents.ClearCollectionAsync(id, CancellationToken.None);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<TagResponse>>, ProblemHttpResult>> ListTagsAsync(
        Guid id, ClaimsPrincipal user, DocumentStore documents, CancellationToken cancellationToken)
    {
        if (!await documents.CanAccessAsync(id, user, CollectionAccess.Read, cancellationToken))
        {
            return ApiProblems.CollectionNotFound(id);
        }

        var tags = await documents.ListTagsAsync(id, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<TagResponse>>(tags.Select(t => new TagResponse(t.Name, t.Documents)).ToList());
    }
}
