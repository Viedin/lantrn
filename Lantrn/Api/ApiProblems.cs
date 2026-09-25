using Microsoft.AspNetCore.Http.HttpResults;

namespace Lantrn.Api;

// The RFC 7807 errors the endpoints return, so the same failure reads the same everywhere.
internal static class ApiProblems
{
    public static ProblemHttpResult CollectionNotFound(string name) =>
        NotFound($"There is no collection named '{name}'.");

    public static ProblemHttpResult DocumentNotFound(Guid id) =>
        NotFound($"There is no document with id {id}.");

    public static ProblemHttpResult NotFound(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not found");

    public static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    public static ProblemHttpResult BadRequest(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest, title: "Bad request");

    public static ProblemHttpResult UnsupportedType(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Unsupported file type");

    // The document could not be read, chunked or embedded; the message says which, as it does on the ingest page.
    public static ProblemHttpResult IngestFailed(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ingest failed");
}
