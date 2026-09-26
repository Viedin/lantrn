namespace Lantrn.Infra;

// The installation's endpoints and models, kept in a single row so they can be changed without a redeploy.
public sealed class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public EmbeddingSettings Embeddings { get; set; } = new();

    public VisionSettings Vision { get; set; } = new();

    public AssistantSettings Assistant { get; set; } = new();

    public AccessSettings Access { get; set; } = new();

    // The cached copy is shared by every circuit, so editors work on their own.
    public AppSettings Copy() => new()
    {
        Id = Id,
        Embeddings = Embeddings.Copy(),
        Vision = Vision.Copy(),
        Assistant = Assistant.Copy(),
        Access = Access.Copy(),
    };
}

public sealed class AccessSettings
{
    // Lets visitors search and read documents without signing in. Off by default.
    public bool PublicSearch { get; set; }

    public AccessSettings Copy() => (AccessSettings)MemberwiseClone();
}

// What every OpenAI-compatible endpoint needs, so settings and clients can treat the three alike.
public interface IEndpointSettings
{
    // Include the version segment, e.g. http://localhost:1234/v1.
    string BaseUrl { get; set; }

    string? ApiKey { get; set; }

    string Model { get; set; }

    int TimeoutSeconds { get; set; }
}

public static class EndpointSettingsExtensions
{
    public static bool IsConfigured(this IEndpointSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.BaseUrl) && !string.IsNullOrWhiteSpace(settings.Model);

    public static bool IsBlank(this IEndpointSettings settings) =>
        string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Model);
}

// Endpoints and models start empty: a fresh install sets them on the Settings page or through configuration.
public sealed class EmbeddingSettings : IEndpointSettings
{
    public string BaseUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string Model { get; set; } = string.Empty;

    // Only sent when set; many local models reject the "dimensions" parameter.
    public int? Dimensions { get; set; }

    public string QueryPrefix { get; set; } = string.Empty;

    public int BatchSize { get; set; } = 32;

    public int TimeoutSeconds { get; set; } = 600;

    public EmbeddingSettings Copy() => (EmbeddingSettings)MemberwiseClone();
}

public sealed class VisionSettings : IEndpointSettings
{
    public string BaseUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string Model { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 600;

    public VisionSettings Copy() => (VisionSettings)MemberwiseClone();
}

// A chat model that answers the query from the top search results. Off by default: it sends document text to the endpoint.
public sealed class AssistantSettings : IEndpointSettings
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string Model { get; set; } = string.Empty;

    // How many of the top results the model reads.
    public int MaxSources { get; set; } = 6;

    public int TimeoutSeconds { get; set; } = 120;

    public AssistantSettings Copy() => (AssistantSettings)MemberwiseClone();
}
