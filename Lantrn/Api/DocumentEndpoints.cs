using Lantrn.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Lantrn.Api;

// Ingesting documents into a collection, and listing, retagging and deleting them.
public static class DocumentEndpoints
{
    // The same limit as the ingest page.
    private const long MaxFileSize = 100 * 1024 * 1024;

    public static RouteGroupBuilder MapDocumentEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/collections/{name}/documents", ListAsync)
            .WithName("ListDocuments")
            .WithSummary("List documents")
            .WithDescription("The collection's documents, most recently ingested first.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/collections/{name}/documents", UploadAsync)
            .WithName("UploadDocument")
            .WithSummary("Ingest a file")
            .WithDescription(
                "Uploads one file as multipart/form-data and waits while it is extracted, chunked and embedded. " +
                $"Supported: {string.Join(", ", DocumentExtractor.SupportedTypes.Keys)}. Images are transcribed by the " +
                "vision model. PDFs and images are kept as the document's original. A file with the same name in the " +
                "collection is replaced.")
            .WithMetadata(new RequestSizeLimitAttribute(MaxFileSize + 1024 * 1024))
            .WithFormOptions(multipartBodyLengthLimit: MaxFileSize + 1024 * 1024)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/collections/{name}/documents/markdown", IngestMarkdownAsync)
            .WithName("IngestMarkdown")
            .WithSummary("Ingest markdown")
            .WithDescription(
                "Adds markdown or plain text as a document, for content that doesn't live in a file. " +
                "A document with the same source in the collection is replaced.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/collections/{name}/documents/url", IngestUrlAsync)
            .WithName("IngestUrl")
            .WithSummary("Ingest a web page")
            .WithDescription(
                "Fetches one page (HTML, PDF, markdown or text) and adds it as a document with the URL as its source. " +
                "Links are not followed; use the Crawl page to ingest a whole site.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/documents/{id:guid}", GetAsync)
            .WithName("GetDocument")
            .WithSummary("Get a document")
            .WithDescription("The document with its full markdown.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/documents/{id:guid}/original", GetOriginalAsync)
            .WithName("GetDocumentOriginal")
            .WithSummary("Download a document's original")
            .WithDescription("The uploaded PDF or image, for documents that kept one. Supports range requests.")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/documents/{id:guid}/tags", SetTagsAsync)
            .WithName("SetDocumentTags")
            .WithSummary("Replace a document's tags")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/documents/{id:guid}", DeleteAsync)
            .WithName("DeleteDocument")
            .WithSummary("Delete a document")
            .WithDescription("Removes the document, its vectors and its original.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<DocumentResponse>>, ProblemHttpResult>> ListAsync(
        string name,
        DocumentStore documents,
        CancellationToken cancellationToken,
        [FromQuery] string? tag = null,
        [FromQuery] string? source = null)
    {
        if (await documents.GetCollectionAsync(name, cancellationToken) is null)
        {
            return ApiProblems.CollectionNotFound(name);
        }

        var list = await documents.ListDocumentsAsync(name, tag?.ToLowerInvariant(), source, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<DocumentResponse>>(list.Select(d => DocumentResponse.From(name, d)).ToList());
    }

    private static async Task<Results<Created<DocumentResponse>, ProblemHttpResult>> UploadAsync(
        string name,
        IFormFile file,
        DocumentStore documents,
        DocumentIngestor ingestor,
        ILoggerFactory loggers,
        CancellationToken cancellationToken,
        [FromForm] string? tags = null,
        [FromForm] int? maxCharacters = null,
        [FromForm] int? minCharacters = null,
        [FromForm] int? overlap = null)
    {
        var source = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(source))
        {
            return ApiProblems.BadRequest("The uploaded file has no name.");
        }
        if (!DocumentExtractor.TryGetContentType(source, out _))
        {
            return ApiProblems.UnsupportedType(
                $"'{source}' can't be ingested. Supported: {string.Join(", ", DocumentExtractor.SupportedTypes.Keys)}.");
        }
        if (file.Length > MaxFileSize)
        {
            return TypedResults.Problem($"Files can be at most {MaxFileSize / 1024 / 1024} MB.",
                statusCode: StatusCodes.Status413PayloadTooLarge, title: "File too large");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        return await IngestAsync(
            name, source, DocumentStore.ParseTags(tags), ChunkingOptions.ToOptions(maxCharacters, minCharacters, overlap),
            (options, ct) => ingestor.IngestFileAsync(bytes, source, source, options, ct),
            keepOriginal: kind => DocumentExtractor.KeepsOriginal(source, kind) ? bytes : null,
            documents, loggers, cancellationToken);
    }

    private static Task<Results<Created<DocumentResponse>, ProblemHttpResult>> IngestMarkdownAsync(
        string name,
        IngestMarkdownRequest request,
        DocumentStore documents,
        DocumentIngestor ingestor,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var source = request.Source.Trim();
        return IngestAsync(
            name, source, NormalizeTags(request.Tags), request.Chunking?.ToOptions() ?? new IngestOptions(),
            (options, ct) => ingestor.IngestMarkdownAsync(request.Markdown, source, options, ct),
            keepOriginal: _ => null,
            documents, loggers, cancellationToken);
    }

    private static async Task<Results<Created<DocumentResponse>, ProblemHttpResult>> IngestUrlAsync(
        string name,
        IngestUrlRequest request,
        DocumentStore documents,
        DocumentIngestor ingestor,
        WebCrawler crawler,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (!request.Url.IsAbsoluteUri || (request.Url.Scheme != Uri.UriSchemeHttp && request.Url.Scheme != Uri.UriSchemeHttps))
        {
            return ApiProblems.BadRequest("Give an absolute http or https URL.");
        }

        var source = request.Url.ToString();
        return await IngestAsync(
            name, source, NormalizeTags(request.Tags), request.Chunking?.ToOptions() ?? new IngestOptions(),
            async (options, ct) =>
            {
                var page = await crawler.FetchAsync(request.Url, ct);
                return await ingestor.IngestFileAsync(page.Bytes, page.FileName, source, options, ct);
            },
            keepOriginal: _ => null,
            documents, loggers, cancellationToken);
    }

    private static async Task<Results<Ok<DocumentDetailResponse>, ProblemHttpResult>> GetAsync(
        Guid id, DocumentStore documents, CancellationToken cancellationToken) =>
        await documents.GetAsync(id, cancellationToken) is { } document
            ? TypedResults.Ok(DocumentDetailResponse.From(document))
            : ApiProblems.DocumentNotFound(id);

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> GetOriginalAsync(
        Guid id, DocumentStore documents, CancellationToken cancellationToken) =>
        await documents.GetOriginalAsync(id, cancellationToken) is { } original
            ? TypedResults.PhysicalFile(original.Path, original.ContentType, enableRangeProcessing: true)
            : ApiProblems.NotFound($"Document {id} doesn't exist or has no original.");

    private static async Task<Results<Ok<DocumentResponse>, ProblemHttpResult>> SetTagsAsync(
        Guid id, SetTagsRequest request, DocumentStore documents, CancellationToken cancellationToken)
    {
        if (await documents.GetLocationAsync(id, cancellationToken) is null)
        {
            return ApiProblems.DocumentNotFound(id);
        }

        await documents.SetTagsAsync(id, NormalizeTags(request.Tags), CancellationToken.None);
        var document = await documents.GetAsync(id, cancellationToken);
        return TypedResults.Ok(DocumentResponse.From(document!));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, DocumentStore documents, CancellationToken cancellationToken)
    {
        if (await documents.GetLocationAsync(id, cancellationToken) is null)
        {
            return ApiProblems.DocumentNotFound(id);
        }

        await documents.DeleteAsync(id, CancellationToken.None);
        return TypedResults.NoContent();
    }

    // The shared tail of every ingest: check the target, run the pipeline, then store the result.
    private static async Task<Results<Created<DocumentResponse>, ProblemHttpResult>> IngestAsync(
        string collection,
        string source,
        IReadOnlyList<string> tags,
        IngestOptions options,
        Func<IngestOptions, CancellationToken, Task<IngestResult>> extract,
        Func<Infra.DocumentKind, byte[]?> keepOriginal,
        DocumentStore documents,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (await documents.GetCollectionAsync(collection, cancellationToken) is null)
        {
            return ApiProblems.CollectionNotFound(collection);
        }
        if (options.Validate() is { } invalid)
        {
            return ApiProblems.BadRequest(invalid);
        }

        IngestResult extracted;
        try
        {
            extracted = await extract(options, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            loggers.CreateLogger(typeof(DocumentEndpoints)).LogError(ex, "API ingest of {Source} into '{Collection}' failed", source, collection);
            return ApiProblems.IngestFailed(ex.Message);
        }

        // Not cancellable: a store cut short would leave the database and Qdrant out of step.
        var stored = await documents.StoreAsync(
            collection, source, extracted, tags, keepOriginal(extracted.Kind), cancellationToken: CancellationToken.None);
        var document = await documents.GetAsync(stored.Id, CancellationToken.None);
        return TypedResults.Created($"/api/v1/documents/{stored.Id}", DocumentResponse.From(document!));
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags) =>
        DocumentStore.ParseTags(string.Join(',', tags ?? []));
}
