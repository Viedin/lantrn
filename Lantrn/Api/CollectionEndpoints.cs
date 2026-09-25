using Lantrn.Services;
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

        group.MapGet("/{name}", GetAsync)
            .WithName("GetCollection")
            .WithSummary("Get a collection")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .WithName("CreateCollection")
            .WithSummary("Create a collection")
            .WithDescription("Creates an empty collection. Documents are added to it with the ingest endpoints.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{name}", DeleteAsync)
            .WithName("DeleteCollection")
            .WithSummary("Delete a collection")
            .WithDescription(
                $"Deletes the collection with all its documents, vectors and kept originals. The '{DocumentStore.DefaultCollection}' " +
                "collection is synced from the documents folder and can be cleared but not deleted.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{name}/documents", ClearAsync)
            .WithName("ClearCollection")
            .WithSummary("Clear a collection")
            .WithDescription("Removes every document from the collection but keeps the collection itself.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{name}/tags", ListTagsAsync)
            .WithName("ListCollectionTags")
            .WithSummary("List a collection's tags")
            .WithDescription("Every tag used in the collection, with how many documents carry it.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<CollectionResponse>>> ListAsync(
        DocumentStore documents, CancellationToken cancellationToken)
    {
        var collections = await documents.ListCollectionsAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<CollectionResponse>>(collections.Select(CollectionResponse.From).ToList());
    }

    private static async Task<Results<Ok<CollectionResponse>, ProblemHttpResult>> GetAsync(
        string name, DocumentStore documents, CancellationToken cancellationToken) =>
        await documents.GetCollectionAsync(name, cancellationToken) is { } collection
            ? TypedResults.Ok(CollectionResponse.From(collection))
            : ApiProblems.CollectionNotFound(name);

    private static async Task<Results<Created<CollectionResponse>, ProblemHttpResult>> CreateAsync(
        CreateCollectionRequest request, DocumentStore documents, CancellationToken cancellationToken)
    {
        if (await documents.GetCollectionAsync(request.Name, cancellationToken) is not null)
        {
            return ApiProblems.Conflict($"A collection named '{request.Name}' already exists.");
        }

        await documents.CreateCollectionAsync(request.Name, request.Description, cancellationToken);
        var created = await documents.GetCollectionAsync(request.Name, cancellationToken);
        return TypedResults.Created($"/api/v1/collections/{created!.Name}", CollectionResponse.From(created));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string name, DocumentStore documents, CancellationToken cancellationToken)
    {
        if (name == DocumentStore.DefaultCollection)
        {
            return ApiProblems.Conflict($"The '{DocumentStore.DefaultCollection}' collection can be cleared but not deleted.");
        }
        if (await documents.GetCollectionAsync(name, cancellationToken) is null)
        {
            return ApiProblems.CollectionNotFound(name);
        }

        // Not cancellable: stopping halfway would leave the database and Qdrant out of step.
        await documents.DeleteCollectionAsync(name, CancellationToken.None);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ClearAsync(
        string name, DocumentStore documents, CancellationToken cancellationToken)
    {
        if (await documents.GetCollectionAsync(name, cancellationToken) is null)
        {
            return ApiProblems.CollectionNotFound(name);
        }

        await documents.ClearCollectionAsync(name, CancellationToken.None);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<TagResponse>>, ProblemHttpResult>> ListTagsAsync(
        string name, DocumentStore documents, CancellationToken cancellationToken)
    {
        if (await documents.GetCollectionAsync(name, cancellationToken) is null)
        {
            return ApiProblems.CollectionNotFound(name);
        }

        var tags = await documents.ListTagsAsync(name, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<TagResponse>>(tags.Select(t => new TagResponse(t.Name, t.Documents)).ToList());
    }
}
