namespace IrisSort.Core.Models;

/// <summary>
/// User-configurable processing options.
/// NOTE: This class is currently planned for future use. Most options are not yet implemented in the UI.
/// Some functionality (RecursiveScan, WriteMetadata) is currently hard-coded in the application.
/// </summary>
public class ProcessingOptions
{
    /// <summary>
    /// Whether to scan subdirectories recursively.
    /// </summary>
    public bool RecursiveScan { get; set; } = false;

    /// <summary>
    /// Whether to skip images that already have metadata tags.
    /// </summary>
    public bool SkipExistingTags { get; set; } = false;

    /// <summary>
    /// Whether to run in dry-run mode (preview only, no changes).
    /// </summary>
    public bool DryRunMode { get; set; } = true;

    /// <summary>
    /// Style for generated filenames.
    /// </summary>
    public FilenameStyle FilenameStyle { get; set; } = FilenameStyle.Lowercase;

    /// <summary>
    /// Maximum number of tags to generate per image.
    /// </summary>
    public int MaxTagsPerImage { get; set; } = 10;

    /// <summary>
    /// Whether to write tags to image metadata.
    /// </summary>
    public bool WriteMetadata { get; set; } = true;

    /// <summary>
    /// Supported file extensions.
    /// </summary>
    public List<string> SupportedExtensions { get; set; } = new()
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };
}

/// <summary>
/// Style for generated filenames.
/// </summary>
public enum FilenameStyle
{
    /// <summary>All lowercase with underscores (golden_retriever_park).</summary>
    Lowercase,

    /// <summary>Title case with underscores (Golden_Retriever_Park).</summary>
    TitleCase
}
