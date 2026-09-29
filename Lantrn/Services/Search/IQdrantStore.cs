using Lantrn.Infra;
using Lantrn.Services.Ingestion;

namespace Lantrn.Services.Search;

public interface IQdrantStore
{
    Task<int> ReplaceDocumentAsync(
        Guid collectionId,
        string sourceFile,
        Guid documentId,
        DocumentKind kind,
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default);

    Task<SearchResults> SearchAsync(
        Guid collectionId,
        string queryText,
        float[] queryVector,
        IReadOnlyList<string> tags,
        DocumentKind? kind = null,
        ulong limit = 5,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PassageChunk>> GetPassageAsync(
        Guid collectionId,
        Guid documentId,
        long chunkIndex,
        int radius,
        CancellationToken cancellationToken = default);

    Task DeleteCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(Guid collectionId, Guid documentId, CancellationToken cancellationToken = default);

    Task SetTagsAsync(Guid collectionId, Guid documentId, IReadOnlyList<string> tags, CancellationToken cancellationToken = default);
}
