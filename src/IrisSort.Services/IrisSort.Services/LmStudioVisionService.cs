using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IrisSort.Services.Configuration;
using IrisSort.Services.Exceptions;
using IrisSort.Services.Logging;
using IrisSort.Services.Models;
using Serilog;

namespace IrisSort.Services;

/// <summary>
/// Service for analyzing images using LM Studio's vision-capable local LLM.
/// </summary>
public class LmStudioVisionService : IDisposable
{
    private readonly LmStudioConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    /// Gets the current configuration.
    /// </summary>
    public LmStudioConfiguration Configuration => _config;

    public LmStudioVisionService(LmStudioConfiguration config, HttpClient? httpClient = null, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
        _logger = logger ?? LoggerFactory.CreateLogger<LmStudioVisionService>();
    }

    /// <summary>
    /// Checks if LM Studio is running and accessible.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(_config.ModelsEndpoint, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the list of available models from LM Studio.
    /// </summary>
    public async Task<string[]> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(_config.ModelsEndpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<string>();
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var modelsResponse = JsonSerializer.Deserialize<ModelsListResponse>(content);
            return modelsResponse?.Data?.Select(m => m.Id ?? "").Where(id => !string.IsNullOrEmpty(id)).ToArray() 
                   ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Analyzes an image and returns suggested filename, tags, and description.
    /// </summary>
    public async Task<ImageAnalysisApiResponse> AnalyzeImageAsync(
        byte[] imageData,
        string mimeType,
        string originalFilename = "",
        CancellationToken cancellationToken = default)
    {
        string base64Image = Convert.ToBase64String(imageData);
        string dataUrl = $"data:{mimeType};base64,{base64Image}";

        // Build OpenAI-compatible vision API payload
        var payload = new
        {
            model = _config.Model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = GetPrompt(originalFilename) },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = dataUrl }
                        }
                    }
                }
            },
            temperature = _config.Temperature,
            max_tokens = _config.MaxTokens
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        _logger.Debug("Sending request to {Endpoint}", _config.ChatCompletionsEndpoint);
        _logger.Debug("Model: {Model}", _config.Model);
        _logger.Debug("Image size: {Size} bytes, MIME: {MimeType}", imageData.Length, mimeType);

        var request = new HttpRequestMessage(HttpMethod.Post, _config.ChatCompletionsEndpoint)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.Debug("Response status: {StatusCode}", response.StatusCode);
            _logger.Debug("Response: {Response}", responseContent.Substring(0, Math.Min(Constants.MaxResponsePreviewLength, responseContent.Length)));

            if (!response.IsSuccessStatusCode)
            {
                throw new LmStudioApiException(
                    $"LM Studio API returned {response.StatusCode}: {responseContent}",
                    (int)response.StatusCode);
            }

            return ParseResponse(responseContent);
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "HTTP error communicating with LM Studio");
            throw new LmStudioApiException($"Failed to communicate with LM Studio: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.Warning("Request to LM Studio timed out or was cancelled");
            throw new LmStudioApiException("Request to LM Studio timed out", ex);
        }
        catch (LmStudioApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error during LM Studio API call");
            throw new LmStudioApiException($"Unexpected error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Analyzes an image with automatic retry on failure.
    /// </summary>
    public async Task<ImageAnalysisApiResponse> AnalyzeWithRetryAsync(
        byte[] imageData,
        string mimeType,
        string originalFilename = "",
        CancellationToken cancellationToken = default)
    {
        int attempt = 0;
        Exception? lastException = null;

        while (attempt < _config.MaxRetries)
        {
            try
            {
                return await AnalyzeImageAsync(imageData, mimeType, originalFilename, cancellationToken);
            }
            catch (LmStudioApiException ex) when (ex.StatusCode >= 500)
            {
                // Retry on server errors
                lastException = ex;
                attempt++;
                _logger.Warning("Server error on attempt {Attempt}/{MaxRetries}: {Message}", attempt, _config.MaxRetries, ex.Message);
                if (attempt < _config.MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
            }
            catch (LmStudioApiException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
            {
                // Client errors - don't retry
                _logger.Error("Client error (no retry): {Message}", ex.Message);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Network or other errors - retry
                lastException = ex;
                attempt++;
                _logger.Warning("Error on attempt {Attempt}/{MaxRetries}: {Message}", attempt, _config.MaxRetries, ex.Message);
                if (attempt < _config.MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
            }
        }

        throw new LmStudioApiException(
            $"Failed after {_config.MaxRetries} attempts. Last error: {lastException?.Message}",
            lastException!);
    }

    private ImageAnalysisApiResponse ParseResponse(string responseContent)
    {
        _logger.Debug("Parsing response");

        ChatCompletionResponse? chatResponse;
        try
        {
            chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent);
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to parse outer API response");
            throw new LmStudioApiException($"Failed to parse API response: {ex.Message}", ex);
        }

        if (chatResponse?.Choices == null || chatResponse.Choices.Length == 0)
        {
            _logger.Error("No choices in API response");
            throw new LmStudioApiException("No choices returned from LM Studio");
        }

        var messageContent = chatResponse.Choices[0].Message?.Content;
        _logger.Debug("Raw response length: {Length}", messageContent?.Length ?? 0);
        
        if (string.IsNullOrEmpty(messageContent))
        {
            throw new LmStudioApiException("Empty response from LM Studio");
        }

        // Try to extract JSON from the response (model might include extra text)
        var jsonContent = ExtractJson(messageContent);
        _logger.Debug("Extracted JSON length: {Length}", jsonContent.Length);
        _logger.Debug("Extracted JSON preview: {Preview}", jsonContent.Substring(0, Math.Min(Constants.MaxJsonPreviewLength, jsonContent.Length)));

        try
        {
            var options = new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            var analysisResponse = JsonSerializer.Deserialize<ImageAnalysisApiResponse>(jsonContent, options);
            if (analysisResponse == null)
            {
                throw new LmStudioApiException("Failed to parse analysis response - null result");
            }

            // Sanitize the filename
            analysisResponse.SuggestedFilename = SanitizeFilename(analysisResponse.SuggestedFilename);

            _logger.Information("Parsed successfully - Filename: {Filename}, Title: {Title}, Tags: {Tags}",
                analysisResponse.SuggestedFilename,
                analysisResponse.Title,
                string.Join(", ", analysisResponse.Tags ?? new List<string>()));

            return analysisResponse;
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "JSON parse error. Content: {JsonContent}", jsonContent);
            throw new LmStudioApiException($"JSON parse failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Attempts to extract JSON object from a string that might contain extra text.
    /// Handles <think> tags, markdown code blocks, and other wrapper text.
    /// </summary>
    private string ExtractJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        // Remove <think>...</think> blocks (some models use this for reasoning)
        var thinkEnd = content.LastIndexOf("</think>");
        if (thinkEnd >= 0)
        {
            content = content.Substring(thinkEnd + 8).Trim();
        }

        // Remove markdown code block markers
        content = content.Replace("```json", "").Replace("```", "").Trim();

        // Find the first { and last } with proper nesting
        var start = content.IndexOf('{');
        if (start < 0)
        {
            return content;
        }

        // Count braces to find matching closing brace
        int depth = 0;
        int end = -1;
        for (int i = start; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}') depth--;

            if (depth == 0)
            {
                end = i;
                break;
            }
        }

        string json;
        if (end > start)
        {
            json = content.Substring(start, end - start + 1);
        }
        else
        {
            // JSON appears truncated - try to repair
            json = content.Substring(start);
            json = RepairTruncatedJson(json);
        }

        return json;
    }

    /// <summary>
    /// Attempts to repair truncated JSON by closing unclosed brackets.
    /// </summary>
    private string RepairTruncatedJson(string json)
    {
        _logger.Warning("Attempting to repair truncated JSON");
        
        // Count unclosed brackets
        int braces = 0;
        int brackets = 0;
        bool inString = false;
        char prevChar = '\0';

        foreach (char c in json)
        {
            if (c == '"' && prevChar != '\\')
            {
                inString = !inString;
            }
            else if (!inString)
            {
                if (c == '{') braces++;
                else if (c == '}') braces--;
                else if (c == '[') brackets++;
                else if (c == ']') brackets--;
            }
            prevChar = c;
        }

        // Close unclosed string if in middle of one
        if (inString)
        {
            json += "\"";
        }

        // Close unclosed brackets
        for (int i = 0; i < brackets; i++)
        {
            json += "]";
        }

        // Close unclosed braces
        for (int i = 0; i < braces; i++)
        {
            json += "}";
        }

        _logger.Debug("Repaired JSON (added {Brackets} ] and {Braces} }})", brackets, braces);
        return json;
    }

    private static string SanitizeFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return "unnamed_image";
        }

        // Remove any file extension the model might have added
        var dotIndex = filename.LastIndexOf('.');
        if (dotIndex > 0)
        {
            filename = filename.Substring(0, dotIndex);
        }

        // Replace invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            filename = filename.Replace(c, '_');
        }

        // Replace spaces with underscores
        filename = filename.Replace(' ', '_');

        // Remove consecutive underscores using regex (more efficient)
        filename = Regex.Replace(filename, "_+", "_");

        // Trim underscores from start and end
        filename = filename.Trim('_');

        // Limit length
        if (filename.Length > Constants.MaxFilenameLength)
        {
            filename = filename.Substring(0, Constants.MaxFilenameLength).TrimEnd('_');
        }

        return string.IsNullOrEmpty(filename) ? "unnamed_image" : filename.ToLowerInvariant();
    }

    private static string GetPrompt(string originalFilename)
    {
        var filenameContext = string.IsNullOrEmpty(originalFilename) 
            ? "" 
            : $"\nOriginal filename: \"{originalFilename}\"\nIf the original filename is already descriptive and matches the image content, you may suggest keeping it (cleaned up if needed). If it's a random string or doesn't describe the image, suggest a better name.\n";

        return @"Analyze this image and respond with ONLY a valid JSON object. Extract as much metadata as you can reliably determine from the image content.
" + filenameContext + @"
{
  ""suggested_filename"": ""descriptive_name"",
  ""title"": ""A Concise Title"",
  ""subject"": ""Main subject of the image"",
  ""description"": ""Brief description of what the image shows"",
  ""tags"": [""keyword1"", ""keyword2"", ""keyword3""],
  ""comments"": ""Additional observations about style, mood, composition, or notable elements"",
  ""authors"": """",
  ""copyright"": """",
  ""visible_date"": """"
}

RULES:
- suggested_filename: lowercase, underscores, max 50 chars, NO extension. If original name is descriptive, keep it similar.
- title: Short, descriptive title (like a photo title), max 60 chars
- subject: What the image is about (person, place, object, scene), max 80 chars
- description: Factual description of content, max 150 chars
- tags: 5-15 lowercase keywords for categorization and search
- comments: Artistic/technical observations (lighting, composition, mood), max 200 chars
- authors: ONLY fill if creator name is VISIBLE in image (watermark, signature), otherwise empty string
- copyright: ONLY fill if copyright notice is VISIBLE in image, otherwise empty string
- visible_date: ONLY fill if a date is VISIBLE in the image (timestamp, text), otherwise empty string

DO NOT GUESS authors, copyright, or visible_date. Only include if clearly visible in the image.
Respond with ONLY the JSON object, no markdown, no explanation.";
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the service resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing && _ownsHttpClient)
        {
            _httpClient?.Dispose();
        }

        _disposed = true;
    }
}
