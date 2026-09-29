using Lantrn.Services.Ingestion;

namespace Lantrn.Services;

public interface IEmbeddingService
{
    string Model { get; }

    Task<IReadOnlyList<DocumentChunk>> EmbedChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        string source,
        CancellationToken cancellationToken = default);

    Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default);
}
