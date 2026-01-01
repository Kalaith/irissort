using System.Text.Json.Serialization;

namespace IrisSort.Services.Models;

/// <summary>
/// OpenAI-compatible chat completion response from LM Studio.
/// </summary>
public class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public ChatChoice[]? Choices { get; set; }

    [JsonPropertyName("usage")]
    public TokenUsage? Usage { get; set; }
}

/// <summary>
/// A choice in the chat completion response.
/// </summary>
public class ChatChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

/// <summary>
/// A message in the chat completion.
/// </summary>
public class ChatMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

/// <summary>
/// Token usage statistics.
/// </summary>
public class TokenUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

/// <summary>
/// Expected structured output from LM Studio vision analysis.
/// Includes expanded metadata fields for image properties.
/// </summary>
public class ImageAnalysisApiResponse
{
    /// <summary>Suggested filename (without extension)</summary>
    [JsonPropertyName("suggested_filename")]
    public string SuggestedFilename { get; set; } = string.Empty;

    /// <summary>Keywords for categorization and search</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>Brief description of the image content</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>A concise title for the image</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Subject matter of the image</summary>
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    /// <summary>General comments/observations about the image</summary>
    [JsonPropertyName("comments")]
    public string Comments { get; set; } = string.Empty;

    /// <summary>Author/creator if visible in image (watermark, signature)</summary>
    [JsonPropertyName("authors")]
    public string Authors { get; set; } = string.Empty;

    /// <summary>Copyright info if visible in image</summary>
    [JsonPropertyName("copyright")]
    public string Copyright { get; set; } = string.Empty;

    /// <summary>Date visible in image (if any text shows a date)</summary>
    [JsonPropertyName("visible_date")]
    public string VisibleDate { get; set; } = string.Empty;
}

/// <summary>
/// Models list response for connection check.
/// </summary>
public class ModelsListResponse
{
    [JsonPropertyName("data")]
    public ModelInfo[]? Data { get; set; }
}

/// <summary>
/// Individual model information.
/// </summary>
public class ModelInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }
}
