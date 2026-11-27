namespace MyFashion.Infrastructure.Ollama;

/// <summary>Settings for the local Ollama server.</summary>
public sealed record OllamaOptions
{
    /// <summary>Must be a loopback address: photos never leave this computer.</summary>
    public Uri BaseUrl { get; init; } = new("http://127.0.0.1:11434");

    public string VisionModel { get; init; } = "qwen3-vl:8b";

    /// <summary>Time limit for one request. The first request also loads the model into memory.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>How long Ollama keeps the model in memory between requests.</summary>
    public string KeepAlive { get; init; } = "15m";

    /// <summary>Attempts per image for transient failures (network, 5xx, unusable output).</summary>
    public int MaxAttempts { get; init; } = 2;

    /// <summary>Output token limit for one image analysis; a runaway answer stops here instead of at the timeout.</summary>
    public int MaxAnalysisTokens { get; init; } = 1024;

    /// <summary>Output token limit for the shopping list and outfit suggestions.</summary>
    public int MaxRecommendationTokens { get; init; } = 3072;
}
