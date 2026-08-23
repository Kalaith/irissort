using System.Text.RegularExpressions;

namespace IrisSort.Services;

/// <summary>
/// Converts model- or user-provided names into safe file-name components.
/// </summary>
public static partial class FilenameSanitizer
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Sanitizes a filename without an extension.
    /// </summary>
    public static string Sanitize(string? value, string fallback = "unnamed_image")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var filename = Path.GetFileName(value.Trim());
        filename = Path.GetFileNameWithoutExtension(filename);

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            filename = filename.Replace(invalidCharacter, '_');
        }

        // Treat separators explicitly as invalid even when running on a non-Windows host.
        filename = filename.Replace('/', '_').Replace('\\', '_');
        filename = WhitespaceRegex().Replace(filename, "_");
        filename = ConsecutiveUnderscoreRegex().Replace(filename, "_");
        filename = filename.Trim(' ', '.', '_');

        if (filename.Length > Constants.MaxFilenameLength)
        {
            filename = filename[..Constants.MaxFilenameLength].TrimEnd(' ', '.', '_');
        }

        if (string.IsNullOrEmpty(filename))
        {
            return fallback;
        }

        if (ReservedNames.Contains(filename.TrimEnd('.')))
        {
            filename = $"_{filename}";
        }

        return filename.ToLowerInvariant();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"_+")]
    private static partial Regex ConsecutiveUnderscoreRegex();
}
