using IrisSort.Core.Models;
using IrisSort.Services.Logging;
using Serilog;
using TagLib;
using TagLib.Image;

namespace IrisSort.Services;

/// <summary>
/// Service for reading and writing image metadata using TagLib#.
/// </summary>
public class MetadataWriterService
{
    private readonly ILogger _logger;
    private readonly PngWebpMetadataWriter _pngWebpWriter;
    private readonly JpegXmpMetadataWriter _jpegXmpWriter;

    public MetadataWriterService(ILogger? logger = null)
    {
        _logger = logger ?? LoggerFactory.CreateLogger<MetadataWriterService>();
        _pngWebpWriter = new PngWebpMetadataWriter(logger);
        _jpegXmpWriter = new JpegXmpMetadataWriter(logger);
    }

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
            _logger.Warning("File not found: {TargetPath}", targetPath);
            return false;
        }

        var extension = Path.GetExtension(targetPath).ToLowerInvariant();
        _logger.Debug("Writing metadata to {Extension} file: {TargetPath}", extension, targetPath);

        // Use specialized writer for PNG and WEBP files
        if (extension == ".png" || extension == ".webp")
        {
            _logger.Information("Using specialized PNG/WEBP XMP writer for {Extension}", extension);
            return await _pngWebpWriter.WriteMetadataAsync(result, targetPath, cancellationToken);
        }

        // Use specialized XMP writer for JPEG files to ensure proper UTF-8 encoding
        if (extension == ".jpg" || extension == ".jpeg")
        {
            _logger.Information("Using specialized JPEG XMP writer for {Extension}", extension);
            return await _jpegXmpWriter.WriteMetadataAsync(result, targetPath, cancellationToken);
        }

        // GIF files have limited metadata support - warn user
        if (extension == ".gif")
        {
            _logger.Warning("GIF metadata support is limited - metadata may not persist or be readable by all applications");
        }

        return await Task.Run(() =>
        {
            try
            {
                using var file = TagLib.File.Create(targetPath);

                _logger.Debug("File type: {MimeType}, TagTypes: {TagTypes}",
                    file.MimeType, file.TagTypes);

                // Get or create image tag
                var imageTag = file.Tag as CombinedImageTag;

                if (file.Tag == null)
                {
                    _logger.Error("File.Tag is null for {TargetPath}, cannot write metadata", targetPath);
                    return false;
                }

                if (imageTag != null)
                {
                    _logger.Debug("Image tag type: {TagType}", imageTag.GetType().Name);
                }

                int fieldsWritten = 0;

                // CRITICAL FIX: For JPEGs, TagLib# doesn't automatically create XMP segments 
                // if they don't exist. We must explicitly force creation.
                if (extension == ".jpg" || extension == ".jpeg" || file.MimeType?.Contains("jpeg") == true)
                {
                    try 
                    {
                        var xmp = file.GetTag(TagTypes.XMP, true);
                        if (xmp != null) _logger.Debug("Ensured XMP tag segment exists");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Failed to force create XMP tag: {Message}", ex.Message);
                    }
                }

                // Write Title
                if (!string.IsNullOrEmpty(result.Title))
                {
                    file.Tag.Title = result.Title;
                    _logger.Debug("Set Title: {Title}", result.Title);
                    fieldsWritten++;
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
                    _logger.Debug("Set Comment: {Comment}", description);
                    fieldsWritten++;
                }

                // Write Tags/Keywords
                if (result.FinalTags.Count > 0)
                {
                    // For image files, use Keywords property if available
                    if (imageTag != null)
                    {
                        imageTag.Keywords = result.FinalTags.ToArray();
                        _logger.Debug("Set Keywords (image-specific): {Tags}", string.Join(", ", result.FinalTags));
                    }
                    // Also try to set as genres (works as fallback for some formats)
                    file.Tag.Genres = result.FinalTags.ToArray();
                    _logger.Debug("Set Genres (fallback): {Tags}", string.Join(", ", result.FinalTags));
                    fieldsWritten++;
                }

                // Write Authors/Artists
                if (!string.IsNullOrEmpty(result.Authors))
                {
                    file.Tag.Performers = new[] { result.Authors };
                    if (imageTag != null)
                    {
                        imageTag.Creator = result.Authors;
                    }
                    _logger.Debug("Set Authors: {Authors}", result.Authors);
                    fieldsWritten++;
                }

                // Write Copyright
                if (!string.IsNullOrEmpty(result.Copyright))
                {
                    file.Tag.Copyright = result.Copyright;
                    _logger.Debug("Set Copyright: {Copyright}", result.Copyright);
                    fieldsWritten++;
                }

                // Add comments to extended comment if not empty
                if (!string.IsNullOrEmpty(result.Comments) && imageTag != null)
                {
                    // Append to comment if we have additional comments
                    var existingComment = file.Tag.Comment ?? "";
                    if (!string.IsNullOrEmpty(existingComment))
                    {
                        file.Tag.Comment = $"{existingComment}\n\n{result.Comments}";
                        _logger.Debug("Appended Comments to existing comment");
                    }
                    else
                    {
                        file.Tag.Comment = result.Comments;
                        _logger.Debug("Set Comments: {Comments}", result.Comments);
                    }
                    fieldsWritten++;
                }

                if (fieldsWritten == 0)
                {
                    _logger.Warning("No metadata fields to write for {TargetPath}", targetPath);
                    return false;
                }

                _logger.Debug("Saving {FieldCount} metadata fields to file...", fieldsWritten);

                file.Save();

                _logger.Information("Successfully saved {FieldCount} metadata fields to {TargetPath} ({Extension})",
                    fieldsWritten, targetPath, extension);

                // Verify the save actually worked by reading back
                try
                {
                    using var verifyFile = TagLib.File.Create(targetPath);
                    bool hasData = !string.IsNullOrEmpty(verifyFile.Tag.Title) ||
                                   !string.IsNullOrEmpty(verifyFile.Tag.Comment) ||
                                   (verifyFile.Tag.Genres != null && verifyFile.Tag.Genres.Length > 0);

                    if (!hasData)
                    {
                        _logger.Warning("Verification failed: No metadata found after save for {TargetPath}", targetPath);
                        return false;
                    }

                    _logger.Debug("Verification passed: Metadata confirmed in {TargetPath}", targetPath);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Could not verify metadata write for {TargetPath}", targetPath);
                    // Still return true since the save appeared to succeed
                }

                return true;
            }
            catch (UnsupportedFormatException ex)
            {
                _logger.Warning(ex, "Unsupported format for {TargetPath}", targetPath);
                return false;
            }
            catch (CorruptFileException ex)
            {
                _logger.Error(ex, "Corrupt file: {TargetPath}", targetPath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error writing metadata to {TargetPath}", targetPath);
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
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to read tags from {FilePath}", filePath);
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
