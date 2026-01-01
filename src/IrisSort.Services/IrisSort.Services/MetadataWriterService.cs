using IrisSort.Core.Models;
using TagLib;
using TagLib.Image;

namespace IrisSort.Services;

/// <summary>
/// Service for reading and writing image metadata using TagLib#.
/// </summary>
public class MetadataWriterService
{
    /// <summary>
    /// Writes metadata to an image file.
    /// </summary>
    public async Task<bool> WriteMetadataAsync(
        ImageAnalysisResult result,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        if (!System.IO.File.Exists(targetPath))
        {
            Console.WriteLine($"[MetadataWriter] File not found: {targetPath}");
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                Console.WriteLine($"[MetadataWriter] Writing metadata to: {targetPath}");

                using var file = TagLib.File.Create(targetPath);

                // Get or create image tag
                var imageTag = file.Tag as CombinedImageTag;
                
                // Write Title
                if (!string.IsNullOrEmpty(result.Title))
                {
                    file.Tag.Title = result.Title;
                    Console.WriteLine($"[MetadataWriter] Title: {result.Title}");
                }

                // Write Subject/Description - map to Comment since JPEG doesn't have Subject
                var description = result.Description;
                if (!string.IsNullOrEmpty(result.Subject))
                {
                    description = string.IsNullOrEmpty(description) 
                        ? result.Subject 
                        : $"{result.Subject} - {description}";
                }
                if (!string.IsNullOrEmpty(description))
                {
                    file.Tag.Comment = description;
                    Console.WriteLine($"[MetadataWriter] Comment: {description}");
                }

                // Write Tags/Keywords
                if (result.FinalTags.Count > 0)
                {
                    // For image files, use Keywords property if available
                    if (imageTag != null)
                    {
                        imageTag.Keywords = result.FinalTags.ToArray();
                    }
                    // Also try to set as genres (works as fallback for some formats)
                    file.Tag.Genres = result.FinalTags.ToArray();
                    Console.WriteLine($"[MetadataWriter] Tags: {string.Join(", ", result.FinalTags)}");
                }

                // Write Authors/Artists
                if (!string.IsNullOrEmpty(result.Authors))
                {
                    file.Tag.Performers = new[] { result.Authors };
                    if (imageTag != null)
                    {
                        imageTag.Creator = result.Authors;
                    }
                    Console.WriteLine($"[MetadataWriter] Authors: {result.Authors}");
                }

                // Write Copyright
                if (!string.IsNullOrEmpty(result.Copyright))
                {
                    file.Tag.Copyright = result.Copyright;
                    Console.WriteLine($"[MetadataWriter] Copyright: {result.Copyright}");
                }

                // Add comments to extended comment if not empty
                if (!string.IsNullOrEmpty(result.Comments) && imageTag != null)
                {
                    // Append to comment if we have additional comments
                    var existingComment = file.Tag.Comment ?? "";
                    if (!string.IsNullOrEmpty(existingComment))
                    {
                        file.Tag.Comment = $"{existingComment}\n\n{result.Comments}";
                    }
                    else
                    {
                        file.Tag.Comment = result.Comments;
                    }
                }

                file.Save();
                Console.WriteLine($"[MetadataWriter] Successfully saved metadata");
                return true;
            }
            catch (UnsupportedFormatException ex)
            {
                Console.WriteLine($"[MetadataWriter] Unsupported format: {ex.Message}");
                return false;
            }
            catch (CorruptFileException ex)
            {
                Console.WriteLine($"[MetadataWriter] Corrupt file: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MetadataWriter] Error: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Reads existing tags from an image file.
    /// </summary>
    public List<string> ReadTags(string filePath)
    {
        var tags = new List<string>();

        if (!System.IO.File.Exists(filePath))
        {
            return tags;
        }

        try
        {
            using var file = TagLib.File.Create(filePath);
            
            // Try Keywords first (for images)
            if (file.Tag is CombinedImageTag imageTag && imageTag.Keywords != null)
            {
                tags.AddRange(imageTag.Keywords);
            }
            
            // Fall back to Genres
            if (tags.Count == 0 && file.Tag.Genres != null)
            {
                tags.AddRange(file.Tag.Genres);
            }
        }
        catch
        {
            // Ignore errors when reading
        }

        return tags;
    }

    /// <summary>
    /// Checks if an image already has tags.
    /// </summary>
    public bool HasExistingTags(string filePath)
    {
        return ReadTags(filePath).Count > 0;
    }
}
