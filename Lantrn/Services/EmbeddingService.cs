using System.Diagnostics;
using Lantrn.Infra;
using Lantrn.Services.Ingestion;
using OpenAI.Embeddings;

namespace Lantrn.Services;

public sealed class EmbeddingService(SettingsStore store, ILogger<EmbeddingService> logger) : IEmbeddingService
{
    public string Model => store.Current.Embeddings.Model;

    public async Task<IReadOnlyList<DocumentChunk>> EmbedChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        string source,
        CancellationToken cancellationToken = default)
    {
        // One snapshot for the whole batch, so a save halfway through cannot mix two models' vectors.
        var settings = store.Current.Embeddings;
        var client = CreateClient(settings);
        var embedded = new List<DocumentChunk>(chunks.Count);
        var stopwatch = Stopwatch.StartNew();

        foreach (var batch in chunks.Chunk(Math.Max(1, settings.BatchSize)))
        {
            var vectors = await EmbedAsync(settings, client, batch.Select(c => c.IndexText(source)).ToList(), cancellationToken);
            embedded.AddRange(batch.Zip(vectors, (chunk, vector) => chunk with { Embedding = vector }));
        }

        logger.LogInformation(
            "Embedded {Chunks} chunks with '{Model}' in {Elapsed:N0} ms ({Dimensions} dims)",
            embedded.Count, settings.Model, stopwatch.ElapsedMilliseconds,
            embedded.FirstOrDefault()?.Embedding.Length ?? 0);

        return embedded;
    }

    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var settings = store.Current.Embeddings;
        logger.LogInformation("Embedding query ({Length} chars) with '{Model}'", query.Length, settings.Model);

        // Instruction-tuned models such as Qwen3-Embedding expect a task instruction on queries, not documents.
        var vectors = await EmbedAsync(settings, CreateClient(settings), [settings.QueryPrefix + query], cancellationToken);
        return vectors[0];
    }

    private static EmbeddingClient CreateClient(EmbeddingSettings settings) =>
        new(settings.Model, OpenAiEndpoint.Credential(settings.ApiKey), OpenAiEndpoint.Options(settings));

    private async Task<float[][]> EmbedAsync(
        EmbeddingSettings settings, EmbeddingClient client, IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        var generation = new EmbeddingGenerationOptions();
        if (settings.Dimensions is { } dimensions)
        {
            generation.Dimensions = dimensions;
        }

        OpenAIEmbeddingCollection result;
        try
        {
            result = (await client.GenerateEmbeddingsAsync(inputs, generation, cancellationToken)).Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Embedding request to {BaseUrl} with model '{Model}' failed", settings.BaseUrl, settings.Model);
            throw new InvalidOperationException(
                $"Could not embed with '{settings.Model}' at {settings.BaseUrl}. Is the server running and the model loaded? ({ex.Message})", ex);
        }

        if (result.Count != inputs.Count)
        {
            throw new InvalidOperationException(
                $"The embedding endpoint returned {result.Count} vectors for {inputs.Count} inputs.");
        }

        return result.OrderBy(e => e.Index).Select(e => e.ToFloats().ToArray()).ToArray();
    }
}
