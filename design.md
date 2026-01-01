# IrisSort — Design Document

**Version:** 1.0
**Last Updated:** 2026-01-01
**Status:** Initial Design

---

## 1. Executive Summary

### 1.1 Product Overview

**IrisSort** is a desktop application that uses AI vision to analyze images and suggest meaningful filenames and metadata tags based on their contents.

**Tagline:** See your images. Name them right.

### 1.2 Core Value Proposition

Most image organizers sort. Some tools batch rename. **IrisSort understands.**

IrisSort leverages LM Studio (local vision LLM) to understand image contents, generating intelligent filenames and tags without manual effort or tedious captioning.

### 1.3 Target Users

- Photographers organizing photo libraries
- Content creators managing asset collections
- Digital artists cataloging their work
- General users with cluttered download folders
- Social media managers organizing visual content
- Stock photographers preparing submissions

---

## 2. System Architecture

### 2.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Desktop Application                   │
│                      (WPF - Windows)                     │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │   Folder     │  │   Image      │  │   Rename     │  │
│  │   Scanner    │→ │   Analyzer   │→ │   Planner    │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│         ↓                  ↓                   ↓        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ File System  │  │  LM Studio   │  │  Metadata    │  │
│  │ Operations   │  │  Local API   │  │  Writer      │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│                          ↓                              │
│                   ┌──────────────┐                      │
│                   │    Undo      │                      │
│                   │   Manager    │                      │
│                   └──────────────┘                      │
└─────────────────────────────────────────────────────────┘
                           ↓
                  ┌─────────────────┐
                  │ Local File      │
                  │ System          │
                  └─────────────────┘
```

### 2.2 Core Components

1. **Folder Scanner**: Enumerates image files in selected directory with optional recursion
2. **Image Analyzer**: Orchestrates LM Studio API calls and parses results
3. **Rename Planner**: Handles name collision resolution and safe file operations
4. **Metadata Writer**: Writes tags to EXIF/XMP metadata
5. **Undo Manager**: Tracks operations for session-based rollback

### 2.3 Technology Stack

- **Desktop Framework**: WPF (.NET 8)
- **Language**: C# 12
- **AI/LLM**: LM Studio local server (OpenAI-compatible API)
- **HTTP Client**: HttpClient (.NET)
- **Image Metadata**: MetadataExtractor NuGet package
- **Storage**: Local file system
- **Configuration**: User secrets + encrypted local storage (DPAPI)

---

## 3. Functional Requirements

### 3.1 Input Capabilities (MVP)

#### File Selection
- Single image file selection
- Directory selection with file enumeration
- Optional recursive scanning
- Hidden/system folder exclusion

#### Supported Formats
- JPEG (.jpg, .jpeg)
- PNG (.png)
- WebP (.webp)

#### Non-Goals (v1)
- ❌ HEIC support
- ❌ RAW format support
- ❌ Video files
- ❌ Multi-folder batch operations
- ❌ Cloud storage integration

### 3.2 Processing Pipeline

#### Stage 1: File Discovery
- Enumerate files in selected path
- Filter by supported extensions
- Build processing queue
- Calculate file hashes for caching

#### Stage 2: Image Analysis
- Send image to LM Studio Vision API
- Parse structured JSON response
- Extract suggested filename, tags, description
- Cache results by file hash

#### Stage 3: User Review
- Display results in table format
- Allow individual accept/reject/edit
- Support bulk operations
- Provide preview thumbnails

#### Stage 4: Apply Changes
- Execute approved renames
- Write metadata tags
- Handle collisions automatically
- Log all operations for undo

### 3.3 Output Format

#### File Naming
- Filesystem-safe characters only
- Configurable case style (lowercase/TitleCase)
- Underscore word separators
- No emojis or special characters
- Collision handling with numeric suffixes (_1, _2)

#### Metadata Tags
- Write to EXIF Keywords (JPEG)
- Write to XMP Subject (PNG/JPEG)
- Preserve existing metadata
- Configurable max tags per image

---

## 4. User Experience Flow

### 4.1 Primary User Journey

1. **Open IrisSort** → Desktop app launches
2. **Start LM Studio** (first run) → Ensure local server is running
3. **Select folder or image** → File/folder picker dialog
4. **"Analyzing your images…"** → Progress indicator with preview
5. **Review suggestions** → Table view with thumbnails ✨ *Magic moment*
6. **Accept/Edit/Reject** → Approve changes
7. **Apply changes** → Safe rename with undo support
8. **View summary** → Operation report with statistics

### 4.2 Design Principles

1. **Never Surprise** — User approves every change before execution
2. **Safe by Default** — Dry-run mode, collision prevention, undo support
3. **Useful Names, Not Poetic Nonsense** — Descriptive, filesystem-friendly output
4. **Minimal Friction** — No API keys, just start LM Studio and go

---

## 5. Technical Design

### 5.1 Core Data Models

```csharp
// Primary result model
public class ImageAnalysisResult
{
    public string OriginalPath { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public string SuggestedFilename { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public AnalysisStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; }
}

public enum AnalysisStatus
{
    Pending,
    Analyzing,
    Success,
    Failed,
    Skipped
}

// Rename operation tracking
public class RenameOperation
{
    public string OriginalPath { get; set; } = string.Empty;
    public string NewPath { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public bool WasSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}

// Processing options
public class ProcessingOptions
{
    public bool RecursiveScan { get; set; } = false;
    public bool SkipExistingTags { get; set; } = false;
    public bool DryRunMode { get; set; } = true;
    public FilenameStyle FilenameStyle { get; set; } = FilenameStyle.Lowercase;
    public int MaxTagsPerImage { get; set; } = 10;
}

public enum FilenameStyle
{
    Lowercase,
    TitleCase
}
```

### 5.2 Filename Generation Rules

**Prime Directive:** *Names should be useful, not clever.*

#### Filename Requirements
- Filesystem-safe (no / \ : * ? " < > |)
- No emojis or special characters
- Underscore for word separation
- Configurable case style
- Max 100 characters (excluding extension)
- Must be unique within directory

#### Tag Requirements
- Standard keywords format
- Lowercase by default
- No special characters except hyphens
- Relevant to image content
- Hierarchical where appropriate (e.g., "dog", "golden retriever")

### 5.3 LM Studio Integration

**Server:** LM Studio local server (OpenAI-compatible API)
**Model:** cydonia-22b-v1.3-i1
**Endpoint:** `http://127.0.0.1:1234/v1/chat/completions`
**API Key:** Not required

#### Configuration

```csharp
public class LmStudioConfiguration
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:1234/v1";
    public string Model { get; set; } = "cydonia-22b-v1.3-i1";
    public int MaxRetries { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 120; // Vision models need more time
    public double Temperature { get; set; } = 0.2; // Lower for consistent naming
    public int MaxTokens { get; set; } = 512;

    public string ChatCompletionsEndpoint => $"{BaseUrl}/chat/completions";
}
```

#### Service Implementation

LM Studio provides an OpenAI-compatible API, making integration straightforward:

```csharp
public class LmStudioVisionService
{
    private readonly LmStudioConfiguration _config;
    private readonly HttpClient _httpClient;

    public LmStudioVisionService(LmStudioConfiguration config, HttpClient httpClient)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
    }

    public async Task<ImageAnalysisResponse> AnalyzeImageAsync(
        byte[] imageData,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        string base64Image = Convert.ToBase64String(imageData);
        string dataUrl = $"data:{mimeType};base64,{base64Image}";
        
        // OpenAI-compatible vision API format
        var payload = new
        {
            model = _config.Model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = GetPrompt() },
                        new 
                        { 
                            type = "image_url",
                            image_url = new { url = dataUrl }
                        }
                    }
                }
            },
            temperature = _config.Temperature,
            max_tokens = _config.MaxTokens,
            response_format = new { type = "json_object" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, _config.ChatCompletionsEndpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
        
        // No API key header needed for LM Studio

        var response = await _httpClient.SendAsync(request, cancellationToken);
        // Parse and return response...
    }

    private string GetPrompt()
    {
        return @"Analyze this image and provide a JSON response with the following structure:

{
  ""suggested_filename"": ""descriptive_filename_here"",
  ""tags"": [""tag1"", ""tag2"", ""tag3""],
  ""description"": ""Brief description of the image content.""
}

Rules for suggested_filename:
- Use only lowercase letters, numbers, and underscores
- Be descriptive but concise (max 50 characters)
- No emojis, special characters, or spaces
- Focus on the main subject of the image
- Do NOT include file extension

Rules for tags:
- Provide 3-10 relevant tags
- Use lowercase
- Include both specific and general terms
- Order from most to least relevant

Rules for description:
- One sentence describing the main content
- Factual and objective
- Max 100 characters";
    }
}
```

#### Request/Response Models

```csharp
// OpenAI-compatible API response models (LM Studio)
public class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("choices")]
    public ChatChoice[]? Choices { get; set; }
}

public class ChatChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

// Expected structured output from LM Studio
public class ImageAnalysisResponse
{
    [JsonPropertyName("suggested_filename")]
    public string SuggestedFilename { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

// Custom exception
public class LmStudioApiException : Exception
{
    public int? StatusCode { get; }

    public LmStudioApiException(string message) : base(message) { }

    public LmStudioApiException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public LmStudioApiException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

#### Error Handling Strategy

```csharp
public async Task<ImageAnalysisResponse> AnalyzeWithRetryAsync(
    byte[] imageData,
    string mimeType,
    CancellationToken cancellationToken = default)
{
    int attempt = 0;
    Exception? lastException = null;

    while (attempt < _config.MaxRetries)
    {
        try
        {
            return await AnalyzeImageAsync(imageData, mimeType, cancellationToken);
        }
        catch (LmStudioApiException ex) when (ex.StatusCode >= 500)
        {
            // Retry on server errors
            lastException = ex;
            attempt++;
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
        }
        catch (LmStudioApiException ex) when (ex.StatusCode == 429)
        {
            // Rate limit - wait longer
            lastException = ex;
            attempt++;
            await Task.Delay(TimeSpan.FromSeconds(10 * attempt), cancellationToken);
        }
        catch (LmStudioApiException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
        {
            // Client errors - don't retry
            throw;
        }
    }

    throw new LmStudioApiException(
        $"Failed after {_config.MaxRetries} attempts",
        lastException!);
}
```

#### Configuration in App Settings

**appsettings.json:**
```json
{
  "LmStudio": {
    "BaseUrl": "http://127.0.0.1:1234/v1",
    "Model": "cydonia-22b-v1.3-i1",
    "MaxRetries": 3,
    "TimeoutSeconds": 120,
    "Temperature": 0.2,
    "MaxTokens": 512
  },
  "Processing": {
    "DefaultRecursive": false,
    "DefaultDryRun": true,
    "DefaultFilenameStyle": "Lowercase",
    "MaxTagsPerImage": 10,
    "SupportedExtensions": [".jpg", ".jpeg", ".png", ".webp"]
  }
}
```

**No API key needed** - LM Studio runs locally.

---

## 6. Data Flow

### 6.1 End-to-End Process

```
┌──────────────┐
│ User Selects │
│ Folder/File  │
└──────┬───────┘
       │
       ▼
┌────────────────────────┐
│  Folder Scanner        │
│  - Enumerate files     │
│  - Filter by extension │
│  - Calculate hashes    │
└──────┬─────────────────┘
       │
       ▼
┌────────────────────────┐
│  Image Analyzer        │
│  - Load image bytes    │
│  - Call LM Studio API │
│  - Parse response      │
└──────┬─────────────────┘
       │
       ▼
┌────────────────────────┐
│  Review UI             │
│  - Display results     │
│  - User approves       │
│  - Edit if needed      │
└──────┬─────────────────┘
       │
       ▼
┌────────────────────────┐
│  Rename Planner        │
│  - Resolve collisions  │
│  - Plan operations     │
│  - Validate paths      │
└──────┬─────────────────┘
       │
       ▼
┌────────────────────────┐
│  Apply Changes         │
│  - Rename files        │
│  - Write metadata      │
│  - Log for undo        │
└──────┬─────────────────┘
       │
       ▼
┌────────────────────────┐
│  Summary Report        │
└────────────────────────┘
```

### 6.2 Cache Structure

```
%LOCALAPPDATA%\IrisSort\
  cache\
    {file_hash}.json     # Cached analysis results
  logs\
    session_{datetime}.json   # Undo log per session
  settings\
    user_preferences.json     # User settings
```

---

## 7. Non-Functional Requirements

### 7.1 Performance

- Image analysis should complete within 30 seconds per image
- UI should remain responsive during batch processing
- Thumbnail generation should not block main thread
- Support directories with up to 500 images

### 7.2 Reliability

- All rename operations logged for undo capability
- Graceful handling of API failures
- Resume capability for interrupted batch operations
- Original files never modified without explicit approval

### 7.3 Usability

- First-time user can process images in < 3 minutes
- LM Studio must be running for analysis
- No API key setup required
- Preview before any file modification
- Clear progress indication during processing

### 7.4 Privacy & Security

- Images processed locally via LM Studio
- No external API calls or cloud services
- No telemetry or usage tracking
- All processing logged locally only
- Option to clear cache and logs

---

## 8. Differentiators

### 8.1 Competitive Advantages

1. **AI Vision Understanding** — Not just OCR or pattern matching
2. **Safe by Design** — Preview, approval, undo built-in
3. **Metadata Integration** — Tags written to standard EXIF/XMP
4. **Local First** — No cloud accounts, your files stay yours

### 8.2 Magic Moment

*Seeing AI-generated filenames and tags that perfectly describe your images.*

This is where trust is earned. The suggestions must be so accurate that users are delighted.

---

## 9. Future Extensions (Not MVP)

**Do not build these yet.**

- Local model fallback (offline mode)
- Style presets (Photography, Art, Screenshots, Memes)
- Rules engine ("always include date if present")
- Auto-foldering by tag
- CLI version
- Batch prompt tuning
- HEIC and RAW format support
- Watch folder mode
- Integration with photo management software

---

## 10. Implementation Phases

### Phase 1: Core MVP
- [x] Design document complete
- [ ] WPF application shell
- [ ] Settings panel (LM Studio connection status)
- [ ] LM Studio Vision API service
- [ ] Folder scanning service
- [ ] Basic image analysis pipeline
- [ ] Results table UI
- [ ] Safe rename operations

### Phase 2: Polish & Safety
- [ ] Thumbnail preview generation
- [ ] Collision resolution UI
- [ ] Undo manager implementation
- [ ] Metadata writing (EXIF/XMP)
- [ ] Error handling & recovery
- [ ] User preferences persistence

### Phase 3: Enhancements
- [ ] Batch processing optimizations
- [ ] Result caching
- [ ] Progress resumption
- [ ] Advanced filtering options
- [ ] Keyboard shortcuts
- [ ] Help documentation

---

## 11. Success Metrics

### 11.1 Quality Metrics
- **Filename Accuracy**: >90% of suggestions are usable without editing
- **Tag Relevance**: >80% of tags accurately describe image content
- **Safety**: Zero unintended file loss or corruption

### 11.2 Usage Metrics
- Time to first successful rename: < 5 minutes
- User able to process 500 images without errors
- All operations reversible via undo

### 11.3 Technical Metrics
- API response time: < 30 seconds per image
- UI responsiveness: < 100ms for user actions
- Batch success rate: > 99%

---

## 12. Open Questions & Decisions Needed

### Decided:
1. ✅ **LLM Provider**: LM Studio local server with cydonia-22b-v1.3-i1 model
2. ✅ **UI Framework**: WPF for Windows desktop
3. ✅ **Scope**: MVP focuses on rename + tags, no folder organization
4. ✅ **No API Key**: Using local LM Studio, no external API costs

### Still Open:
1. **Concurrent Processing**: How many parallel API requests to LM Studio?
2. **Cache Expiration**: How long to cache analysis results?

---

## 13. Appendix

### 13.1 Use Cases

**Photo Library Organization**
> Photographer imports 500 vacation photos with DSC_0001 names, processes through IrisSort, gets descriptive names like "sunset_beach_palm_trees" and tags like "beach, sunset, vacation, palm tree".

**Screenshot Management**
> Developer's screenshot folder filled with "Screenshot 2024-01-15" files becomes organized with names like "error_dialog_network_timeout" and tags like "error, dialog, network, debug".

**Asset Library Tagging**
> Content creator's asset folder gets metadata tags applied for easier searching in creative tools.

### 13.2 Example Output

**Input:** `IMG_2847.jpg` (photo of a golden retriever in a park)

**Output:**
```json
{
  "suggested_filename": "golden_retriever_playing_park",
  "tags": ["dog", "golden retriever", "park", "outdoors", "pet", "grass", "sunny"],
  "description": "A golden retriever running across a grassy park on a sunny day."
}
```

**Resulting file:** `golden_retriever_playing_park.jpg`
**EXIF Keywords:** dog, golden retriever, park, outdoors, pet, grass, sunny

---

**End of Design Document**
