using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lantrn.Services;
using Lantrn.Services.Accounts;
using Lantrn.Services.Search;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Lantrn.Api;

// The MCP server's tools: AI assistants search and read the library with an admin's API key, and change nothing.
[McpServerToolType]
public sealed class LibraryTools(
    IHttpContextAccessor http,
    DocumentStore documents,
    SearchService search,
    ILogger<LibraryTools> logger)
{
    public const string Instructions =
        "Lantrn is a library of the user's documents, split into collections. Use list_collections to find the " +
        "collection that fits the question, search it for matching passages, and get_document to read a whole document.";

    // The SDK's defaults, which carry the type resolver it needs, with enums as names like in the API.
    public static readonly JsonSerializerOptions SerializerOptions = new(McpJsonUtilities.DefaultOptions)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [McpServerTool(Name = "list_collections", Title = "List collections", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the collections you can search, with their ids, descriptions and how many documents they hold.")]
    public async Task<IReadOnlyList<CollectionResponse>> ListCollectionsAsync(CancellationToken cancellationToken)
    {
        var user = http.HttpContext?.User ?? throw new InvalidOperationException("MCP tools only run within an HTTP request.");
        var collections = await documents.ListCollectionsAsync(user, CollectionAccess.Read, cancellationToken);
        return collections.Select(CollectionResponse.From).ToList();
    }

    [McpServerTool(Name = "search", Title = "Search a collection", ReadOnly = true, OpenWorld = false)]
    [Description(
        "Searches one collection by meaning and keywords, and returns the best matching passages with the id of " +
        "the document each comes from.")]
    public async Task<SearchResponse> SearchAsync(
        [Description("The id of the collection to search, from list_collections.")] Guid collectionId,
        [Description("What to look for, in words or as a question.")] string query,
        [Description("Only passages from documents that carry any of these tags.")] string[]? tags = null,
        [Description("How many passages to return, from 1 to 50.")] int limit = CollectionEndpoints.DefaultSearchLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("Give a query to search for.");
        }
        if (limit is < 1 or > CollectionEndpoints.MaxSearchLimit)
        {
            throw new McpException($"The limit must be between 1 and {CollectionEndpoints.MaxSearchLimit}.");
        }
        if (!await documents.CollectionExistsAsync(collectionId, cancellationToken))
        {
            throw new McpException($"There is no collection with id {collectionId}. Use list_collections to find one.");
        }

        try
        {
            var results = await search.SearchAsync(
                collectionId, query.Trim(), DocumentStore.ParseTags(string.Join(',', tags ?? [])), null, limit, cancellationToken);
            return SearchResponse.From(results);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "MCP search in collection {CollectionId} failed", collectionId);
            throw new McpException($"The search failed: {ex.Message}", ex);
        }
    }

    [McpServerTool(Name = "get_document", Title = "Read a document", ReadOnly = true, OpenWorld = false)]
    [Description("Returns a whole document as markdown, such as one a search result came from.")]
    public async Task<DocumentDetailResponse> GetDocumentAsync(
        [Description("The document's id, from a search result.")] Guid documentId,
        CancellationToken cancellationToken = default) =>
        await documents.GetAsync(documentId, cancellationToken) is { } document
            ? DocumentDetailResponse.From(document)
            : throw new McpException($"There is no document with id {documentId}.");
}
