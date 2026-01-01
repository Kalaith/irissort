namespace IrisSort.Core.Models;

/// <summary>
/// Result of analyzing an image with the LM Studio Vision API.
/// Includes expanded metadata fields for Windows image properties.
/// </summary>
public class ImageAnalysisResult
{
    /// <summary>Full path to the original image file.</summary>
    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>Original filename without path.</summary>
    public string OriginalFilename { get; set; } = string.Empty;

    /// <summary>AI-suggested filename (without extension).</summary>
    public string SuggestedFilename { get; set; } = string.Empty;

    /// <summary>AI-generated tags/keywords for the image.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Brief AI-generated description of the image content.</summary>
    public string Description { get; set; } = string.Empty;

    // ===== Expanded Metadata Fields =====

    /// <summary>A concise title for the image.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Subject matter of the image.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>General comments/observations about the image.</summary>
    public string Comments { get; set; } = string.Empty;

    /// <summary>Author/creator if visible in image.</summary>
    public string Authors { get; set; } = string.Empty;

    /// <summary>Copyright info if visible in image.</summary>
    public string Copyright { get; set; } = string.Empty;

    /// <summary>Date visible in the image (if any text shows a date).</summary>
    public string VisibleDate { get; set; } = string.Empty;

    // ===== Processing Status =====

    /// <summary>Current status of the analysis.</summary>
    public AnalysisStatus Status { get; set; } = AnalysisStatus.Pending;

    /// <summary>Error message if analysis failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>SHA256 hash of the file for caching.</summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>When the image was analyzed.</summary>
    public DateTime AnalyzedAt { get; set; }

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>File extension including the dot (e.g., ".jpg").</summary>
    public string Extension { get; set; } = string.Empty;

    // ===== User Editing =====

    /// <summary>Whether the user has approved this rename.</summary>
    public bool IsApproved { get; set; }

    /// <summary>User-edited filename (if different from suggested).</summary>
    public string? EditedFilename { get; set; }

    /// <summary>User-edited tags (if different from suggested).</summary>
    public List<string>? EditedTags { get; set; }

    /// <summary>Gets the final filename to use (edited or suggested).</summary>
    public string FinalFilename => EditedFilename ?? SuggestedFilename;

    /// <summary>Gets the final tags to use (edited or suggested).</summary>
    public List<string> FinalTags => EditedTags ?? Tags;
}

/// <summary>
/// Status of an image analysis operation.
/// </summary>
public enum AnalysisStatus
{
    /// <summary>Image is queued for analysis.</summary>
    Pending,

    /// <summary>Analysis is in progress.</summary>
    Analyzing,

    /// <summary>Analysis completed successfully.</summary>
    Success,

    /// <summary>Analysis failed with an error.</summary>
    Failed,

    /// <summary>Image was skipped (e.g., already has tags).</summary>
    Skipped
}
