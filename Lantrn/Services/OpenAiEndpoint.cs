using System.ClientModel;
using Lantrn.Infra;
using OpenAI;

namespace Lantrn.Services;

// Client setup shared by everything that talks to an OpenAI-compatible server.
public static class OpenAiEndpoint
{
    // Local servers ignore the key, but the SDK refuses an empty one.
    public static ApiKeyCredential Credential(string? apiKey) =>
        new(string.IsNullOrEmpty(apiKey) ? "not-needed" : apiKey);

    public static OpenAIClientOptions Options(Uri endpoint, TimeSpan timeout) =>
        new() { Endpoint = endpoint, NetworkTimeout = timeout };

    public static OpenAIClientOptions Options(IEndpointSettings settings) =>
        Options(new Uri(settings.BaseUrl), TimeSpan.FromSeconds(settings.TimeoutSeconds));
}
