using System.Text;
using IrisSort.Core.Models;
using IrisSort.Services.Logging;
using Serilog;
using TagLib;
using TagLib.Xmp;

namespace IrisSort.Services;

/// <summary>
/// Specialized metadata writer for JPEG files using XMP.
/// Ensures proper UTF-8 encoding for all text fields.
/// </summary>
public class JpegXmpMetadataWriter
{
    private readonly ILogger _logger;

    public JpegXmpMetadataWriter(ILogger? logger = null)
    {
        _logger = logger ?? LoggerFactory.CreateLogger<JpegXmpMetadataWriter>();
    }

    /// <summary>
    /// Writes metadata to JPEG file using XMP to ensure proper UTF-8 encoding.
    /// </summary>
    public async Task<bool> WriteMetadataAsync(
        ImageAnalysisResult result,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(targetPath).ToLowerInvariant();

        if (extension != ".jpg" && extension != ".jpeg")
        {
            _logger.Warning("JpegXmpMetadataWriter called for non-JPEG file: {Extension}", extension);
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                _logger.Debug("Writing XMP metadata to JPEG file: {TargetPath}", targetPath);

                using var file = TagLib.File.Create(targetPath);

                // Force creation of XMP tag if it doesn't exist
                var xmpTag = file.GetTag(TagTypes.XMP, true) as XmpTag;

                if (xmpTag == null)
                {
                    _logger.Error("Failed to create XMP tag for JPEG: {TargetPath}", targetPath);
                    return false;
                }

                int fieldsWritten = 0;

                // Write Title (dc:title)
                if (!string.IsNullOrEmpty(result.Title))
                {
                    xmpTag.Title = result.Title;
                    _logger.Debug("Set XMP Title: {Title}", result.Title);
                    fieldsWritten++;
                }

                // Write Description/Subject as UserComment (exif:UserComment) with proper UTF-8
                var description = result.Description;
                if (!string.IsNullOrEmpty(result.Subject))
                {
                    description = string.IsNullOrEmpty(description)
                        ? result.Subject
                        : $"{result.Subject} - {description}";
                }

                // Add additional comments if present
                if (!string.IsNullOrEmpty(result.Comments))
                {
                    description = string.IsNullOrEmpty(description)
                        ? result.Comments
                        : $"{description}\n\n{result.Comments}";
                }

                if (!string.IsNullOrEmpty(description))
                {
                    // Write to XMP Comment field (properly encoded as UTF-8)
                    xmpTag.Comment = description;
                    _logger.Debug("Set XMP Comment (UTF-8): {Comment}", description);
                    fieldsWritten++;
                }

                // Write Keywords (dc:subject)
                if (result.FinalTags.Count > 0)
                {
                    xmpTag.Keywords = result.FinalTags.ToArray();
                    _logger.Debug("Set XMP Keywords: {Tags}", string.Join(", ", result.FinalTags));
                    fieldsWritten++;
                }

                // Write Creator/Author (dc:creator)
                if (!string.IsNullOrEmpty(result.Authors))
                {
                    xmpTag.Creator = result.Authors;
                    _logger.Debug("Set XMP Creator: {Authors}", result.Authors);
                    fieldsWritten++;
                }

                // Write Copyright (dc:rights)
                if (!string.IsNullOrEmpty(result.Copyright))
                {
                    xmpTag.Copyright = result.Copyright;
                    _logger.Debug("Set XMP Copyright: {Copyright}", result.Copyright);
                    fieldsWritten++;
                }

                if (fieldsWritten == 0)
                {
                    _logger.Warning("No metadata fields to write for {TargetPath}", targetPath);
                    return false;
                }

                _logger.Debug("Saving {FieldCount} XMP metadata fields to JPEG...", fieldsWritten);

                file.Save();

                _logger.Information("Successfully saved {FieldCount} XMP metadata fields to {TargetPath}",
                    fieldsWritten, targetPath);

                // Verify the save worked
                try
                {
                    using var verifyFile = TagLib.File.Create(targetPath);
                    var verifyXmp = verifyFile.GetTag(TagTypes.XMP) as XmpTag;

                    bool hasData = verifyXmp != null && (
                        !string.IsNullOrEmpty(verifyXmp.Title) ||
                        !string.IsNullOrEmpty(verifyXmp.Comment) ||
                        (verifyXmp.Keywords != null && verifyXmp.Keywords.Length > 0));

                    if (!hasData)
                    {
                        _logger.Warning("Verification failed: No XMP metadata found after save for {TargetPath}", targetPath);
                        return false;
                    }

                    _logger.Debug("Verification passed: XMP metadata confirmed in {TargetPath}", targetPath);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Could not verify XMP metadata write for {TargetPath}", targetPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error writing XMP metadata to JPEG {TargetPath}", targetPath);
                return false;
            }
        }, cancellationToken);
    }
}
