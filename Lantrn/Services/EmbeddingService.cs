using System.Diagnostics;
using Lantrn.Infra;
using Lantrn.Services.Ingestion;
using OpenAI.Embeddings;

namespace Lantrn.Services;

public sealed class EmbeddingService(SettingsStore store, ILogger<EmbeddingService> logger)
{
    private Connection? connection;

    public string Model => store.Current.Embeddings.Model;

    public async Task<IReadOnlyList<DocumentChunk>> EmbedChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        string source,
        CancellationToken cancellationToken = default)
    {
        // One snapshot for the whole batch, so a save halfway through cannot mix two models' vectors.
        var current = Connect();
        var embedded = new List<DocumentChunk>(chunks.Count);
        var stopwatch = Stopwatch.StartNew();

        foreach (var batch in chunks.Chunk(Math.Max(1, current.Settings.BatchSize)))
        {
            var vectors = await EmbedAsync(current, batch.Select(c => c.IndexText(source)).ToList(), cancellationToken);
            embedded.AddRange(batch.Zip(vectors, (chunk, vector) => chunk with { Embedding = vector }));
        }

        logger.LogInformation(
            "Embedded {Chunks} chunks with '{Model}' in {Elapsed:N0} ms ({Dimensions} dims)",
            embedded.Count, current.Settings.Model, stopwatch.ElapsedMilliseconds,
            embedded.FirstOrDefault()?.Embedding.Length ?? 0);

        return embedded;
    }

    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var current = Connect();
        logger.LogInformation("Embedding query ({Length} chars) with '{Model}'", query.Length, current.Settings.Model);

        // Instruction-tuned models such as Qwen3-Embedding expect a task instruction on queries, not documents.
        var vectors = await EmbedAsync(current, [current.Settings.QueryPrefix + query], cancellationToken);
        return vectors[0];
    }

    // The SDK client fixes endpoint and model at construction, so it is rebuilt when the settings are saved.
    private Connection Connect()
    {
        var settings = store.Current.Embeddings;
        return connection is { } cached && ReferenceEquals(cached.Settings, settings)
            ? cached
            : connection = new Connection(settings, new EmbeddingClient(
                settings.Model, OpenAiEndpoint.Credential(settings.ApiKey), OpenAiEndpoint.Options(settings)));
    }

    private async Task<float[][]> EmbedAsync(Connection current, IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        var settings = current.Settings;
        var generation = new EmbeddingGenerationOptions();
        if (settings.Dimensions is { } dimensions)
        {
            generation.Dimensions = dimensions;
        }

        OpenAIEmbeddingCollection result;
        try
        {
            result = (await current.Client.GenerateEmbeddingsAsync(inputs, generation, cancellationToken)).Value;
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

    private sealed record Connection(EmbeddingSettings Settings, EmbeddingClient Client);
}
