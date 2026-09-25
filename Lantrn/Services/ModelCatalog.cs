using OpenAI.Models;

namespace Lantrn.Services;

// Lists what an OpenAI-compatible endpoint serves (GET /models), so settings can offer a choice instead of a free-text name.
public sealed class ModelCatalog(ILogger<ModelCatalog> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<IReadOnlyList<string>> ListAsync(string baseUrl, string? apiKey, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Enter an http or https endpoint first.");
        }

        var client = new OpenAIModelClient(OpenAiEndpoint.Credential(apiKey), OpenAiEndpoint.Options(endpoint, Timeout));

        try
        {
            var models = (await client.GetModelsAsync(cancellationToken)).Value;
            var ids = models.Select(m => m.Id).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();

            logger.LogInformation("{Endpoint} lists {Count} models", endpoint, ids.Count);
            return ids;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Listing models at {Endpoint} failed", endpoint);
            throw new InvalidOperationException($"Could not list models at {endpoint}: {ex.Message}", ex);
        }
    }
}
