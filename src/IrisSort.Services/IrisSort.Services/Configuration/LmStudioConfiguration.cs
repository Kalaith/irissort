namespace IrisSort.Services.Configuration;

/// <summary>
/// Configuration for LM Studio local server connection.
/// </summary>
public class LmStudioConfiguration
{
    /// <summary>
    /// Base URL for LM Studio API (default: http://127.0.0.1:1234/v1).
    /// </summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:1234/v1";

    /// <summary>
    /// Model to use for vision analysis.
    /// </summary>
    public string Model { get; set; } = "zai-org/glm-4.6v-flash";

    /// <summary>
    /// Maximum number of retry attempts for failed requests.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Request timeout in seconds (vision models need longer).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Temperature for generation (lower = more consistent).
    /// </summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>
    /// Maximum tokens to generate.
    /// </summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    /// Gets the chat completions endpoint URL.
    /// </summary>
    public string ChatCompletionsEndpoint => $"{BaseUrl}/chat/completions";

    /// <summary>
    /// Gets the models list endpoint URL (for connection check).
    /// </summary>
    public string ModelsEndpoint => $"{BaseUrl}/models";
}
