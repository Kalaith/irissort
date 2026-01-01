using IrisSort.Core.Models;

namespace IrisSort.Services;

/// <summary>
/// Service for scanning directories and finding image files.
/// </summary>
public class FolderScannerService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    /// <summary>
    /// Scans a directory for supported image files.
    /// </summary>
    /// <param name="path">Directory path to scan.</param>
    /// <param name="recursive">Whether to scan subdirectories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of image file paths.</returns>
    public Task<List<string>> ScanDirectoryAsync(
        string path, 
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var files = new List<string>();

            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"Directory not found: {path}");
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var extension in SupportedExtensions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pattern = $"*{extension}";
                try
                {
                    files.AddRange(Directory.GetFiles(path, pattern, searchOption)
                        .Where(f => !IsHiddenOrSystem(f)));
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip directories we don't have access to
                }
            }

            return files.OrderBy(f => f).ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Checks if a single file is a supported image.
    /// </summary>
    public bool IsSupportedImage(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// Gets the MIME type for an image file.
    /// </summary>
    public static string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Calculates a SHA256 hash of the file for caching.
    /// </summary>
    public static async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool IsHiddenOrSystem(string filePath)
    {
        try
        {
            var attributes = File.GetAttributes(filePath);
            return (attributes & FileAttributes.Hidden) == FileAttributes.Hidden ||
                   (attributes & FileAttributes.System) == FileAttributes.System;
        }
        catch
        {
            return false;
        }
    }
}
