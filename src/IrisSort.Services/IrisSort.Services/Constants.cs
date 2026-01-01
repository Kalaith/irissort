namespace IrisSort.Services;

/// <summary>
/// Application-wide constants.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Maximum filename length for sanitized filenames.
    /// </summary>
    public const int MaxFilenameLength = 100;

    /// <summary>
    /// Maximum length for error message preview in UI (short format).
    /// </summary>
    public const int MaxErrorMessagePreviewLength = 50;

    /// <summary>
    /// Maximum length for error message in status text (long format).
    /// </summary>
    public const int MaxErrorMessageStatusLength = 80;

    /// <summary>
    /// Maximum length for response content preview in logs.
    /// </summary>
    public const int MaxResponsePreviewLength = 500;

    /// <summary>
    /// Maximum length for JSON content preview in logs.
    /// </summary>
    public const int MaxJsonPreviewLength = 300;

    /// <summary>
    /// Maximum number of tags to display in UI preview.
    /// </summary>
    public const int MaxTagsDisplayCount = 5;

    /// <summary>
    /// Maximum image file size in bytes before resizing is triggered (4MB).
    /// </summary>
    public const long MaxImageSizeBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Maximum width or height for resized images (maintains aspect ratio).
    /// </summary>
    public const int MaxImageDimension = 2048;

    /// <summary>
    /// Quality setting for resized JPEG/WebP images (0-100).
    /// </summary>
    public const int ResizedImageQuality = 85;
}
