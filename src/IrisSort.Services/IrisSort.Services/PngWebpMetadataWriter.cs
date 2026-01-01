using System.Text;
using IrisSort.Core.Models;
using IrisSort.Services.Logging;
using Serilog;
using TagLib;
using XmpCore;
using XmpCore.Options;

namespace IrisSort.Services;

/// <summary>
/// Specialized metadata writer for PNG and WEBP files using XMP.
/// These formats require different handling than JPEG/standard image formats.
/// </summary>
public class PngWebpMetadataWriter
{
    private readonly ILogger _logger;

    public PngWebpMetadataWriter(ILogger? logger = null)
    {
        _logger = logger ?? LoggerFactory.CreateLogger<PngWebpMetadataWriter>();
    }

    /// <summary>
    /// Writes metadata to PNG or WEBP file using XMP chunks.
    /// </summary>
    public async Task<bool> WriteMetadataAsync(
        ImageAnalysisResult result,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(targetPath).ToLowerInvariant();

        if (extension != ".png" && extension != ".webp")
        {
            _logger.Warning("PngWebpMetadataWriter called for non-PNG/WEBP file: {Extension}", extension);
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                _logger.Debug("Writing XMP metadata to {Extension} file: {TargetPath}", extension, targetPath);

                // Read the file
                byte[] fileBytes = System.IO.File.ReadAllBytes(targetPath);

                // Create XMP metadata
                var xmp = XmpMetaFactory.Create();

                int fieldsWritten = 0;

                // Write Dublin Core metadata (dc:)
                if (!string.IsNullOrEmpty(result.Title))
                {
                    xmp.SetLocalizedText(XmpConstants.NsDC, "title", XmpConstants.XDefault, XmpConstants.XDefault, result.Title);
                    _logger.Debug("Set XMP dc:title = {Title}", result.Title);
                    fieldsWritten++;
                }

                if (!string.IsNullOrEmpty(result.Subject))
                {
                    xmp.SetLocalizedText(XmpConstants.NsDC, "description", XmpConstants.XDefault, XmpConstants.XDefault, result.Subject);
                    _logger.Debug("Set XMP dc:description = {Subject}", result.Subject);
                    fieldsWritten++;
                }

                // Write tags/keywords
                if (result.FinalTags.Count > 0)
                {
                    foreach (var tag in result.FinalTags)
                    {
                        xmp.AppendArrayItem(XmpConstants.NsDC, "subject", new PropertyOptions { IsArray = true }, tag, null);
                    }
                    _logger.Debug("Set XMP dc:subject (keywords) = {Tags}", string.Join(", ", result.FinalTags));
                    fieldsWritten++;
                }

                // Write rights/copyright
                if (!string.IsNullOrEmpty(result.Copyright))
                {
                    xmp.SetLocalizedText(XmpConstants.NsDC, "rights", XmpConstants.XDefault, XmpConstants.XDefault, result.Copyright);
                    _logger.Debug("Set XMP dc:rights = {Copyright}", result.Copyright);
                    fieldsWritten++;
                }

                // Write creator/author
                if (!string.IsNullOrEmpty(result.Authors))
                {
                    xmp.AppendArrayItem(XmpConstants.NsDC, "creator", new PropertyOptions { IsArray = true }, result.Authors, null);
                    _logger.Debug("Set XMP dc:creator = {Authors}", result.Authors);
                    fieldsWritten++;
                }

                // Write extended description/comments as EXIF UserComment
                var fullDescription = result.Description;
                if (!string.IsNullOrEmpty(result.Comments))
                {
                    fullDescription = string.IsNullOrEmpty(fullDescription)
                        ? result.Comments
                        : $"{fullDescription}\n\n{result.Comments}";
                }

                if (!string.IsNullOrEmpty(fullDescription))
                {
                    xmp.SetProperty(XmpConstants.NsExif, "UserComment", fullDescription);
                    _logger.Debug("Set XMP exif:UserComment (description/comments)");
                    fieldsWritten++;
                }

                if (fieldsWritten == 0)
                {
                    _logger.Warning("No metadata fields to write for {TargetPath}", targetPath);
                    return false;
                }

                // Serialize XMP to string
                var xmpString = XmpMetaFactory.SerializeToString(xmp, new SerializeOptions
                {
                    UseCompactFormat = false,
                    OmitPacketWrapper = false,
                    Indent = "  "
                });

                _logger.Debug("Generated XMP metadata ({Length} bytes)", xmpString.Length);

                // For PNG files, inject XMP as iTXt chunk
                if (extension == ".png")
                {
                    if (InjectPngXmp(targetPath, xmpString))
                    {
                        _logger.Information("Successfully wrote {FieldCount} XMP fields to PNG: {TargetPath}",
                            fieldsWritten, targetPath);
                        return true;
                    }
                    else
                    {
                        _logger.Error("Failed to inject XMP into PNG: {TargetPath}", targetPath);
                        return false;
                    }
                }
                // For WEBP files, we need to use a different approach
                else if (extension == ".webp")
                {
                    _logger.Warning("WEBP XMP writing not fully supported - attempting basic metadata write. File: {TargetPath}", targetPath);
                    // WEBP XMP requires modifying the RIFF chunks - more complex
                    // Try basic approach using TagLib# as fallback
                    return TryWriteWebpBasic(targetPath, result, fieldsWritten);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error writing XMP metadata to {TargetPath}", targetPath);
                return false;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Injects XMP metadata into a PNG file as an iTXt chunk.
    /// </summary>
    private bool InjectPngXmp(string filePath, string xmpString)
    {
        try
        {
            // Read PNG file
            byte[] fileData = System.IO.File.ReadAllBytes(filePath);

            // PNG files start with signature: 89 50 4E 47 0D 0A 1A 0A
            if (fileData.Length < 8 || fileData[0] != 0x89 || fileData[1] != 0x50 ||
                fileData[2] != 0x4E || fileData[3] != 0x47)
            {
                _logger.Error("Invalid PNG signature in {FilePath}", filePath);
                return false;
            }

            using var outputStream = new MemoryStream();

            // Write PNG signature
            outputStream.Write(fileData, 0, 8);

            int position = 8;
            bool xmpInserted = false;

            // Parse PNG chunks
            while (position < fileData.Length - 12)
            {
                // Read chunk length (4 bytes, big-endian)
                int chunkLength = (fileData[position] << 24) | (fileData[position + 1] << 16) |
                                  (fileData[position + 2] << 8) | fileData[position + 3];

                // Read chunk type (4 bytes)
                string chunkType = Encoding.ASCII.GetString(fileData, position + 4, 4);

                int totalChunkSize = 12 + chunkLength; // length(4) + type(4) + data(n) + crc(4)

                // Remove existing XMP chunk if present
                if (chunkType == "iTXt" && chunkLength > 22)
                {
                    // Check if this is an XMP chunk by looking for "XML:com.adobe.xmp"
                    string keyword = Encoding.ASCII.GetString(fileData, position + 8, Math.Min(22, chunkLength));
                    if (keyword.StartsWith("XML:com.adobe.xmp"))
                    {
                        _logger.Debug("Removing existing XMP iTXt chunk");
                        position += totalChunkSize;
                        continue; // Skip this chunk
                    }
                }

                // Insert XMP before IEND chunk
                if (chunkType == "IEND" && !xmpInserted)
                {
                    WriteXmpChunk(outputStream, xmpString);
                    xmpInserted = true;
                    _logger.Debug("Inserted XMP iTXt chunk before IEND");
                }

                // Write this chunk
                outputStream.Write(fileData, position, totalChunkSize);
                position += totalChunkSize;
            }

            // Write the modified file
            System.IO.File.WriteAllBytes(filePath, outputStream.ToArray());

            return xmpInserted;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to inject XMP into PNG file {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Writes an iTXt chunk containing XMP metadata.
    /// </summary>
    private void WriteXmpChunk(Stream stream, string xmpString)
    {
        // Keyword for XMP
        const string keyword = "XML:com.adobe.xmp";
        byte[] keywordBytes = Encoding.UTF8.GetBytes(keyword);
        byte[] xmpBytes = Encoding.UTF8.GetBytes(xmpString);

        // iTXt chunk structure:
        // - Keyword (null-terminated)
        // - Compression flag (1 byte) - 0 for uncompressed
        // - Compression method (1 byte) - 0
        // - Language tag (null-terminated) - empty for XMP
        // - Translated keyword (null-terminated) - empty for XMP
        // - Text (UTF-8)

        using var chunkData = new MemoryStream();
        chunkData.Write(keywordBytes, 0, keywordBytes.Length);
        chunkData.WriteByte(0); // Null terminator
        chunkData.WriteByte(0); // Compression flag (uncompressed)
        chunkData.WriteByte(0); // Compression method
        chunkData.WriteByte(0); // Language tag (null-terminated, empty)
        chunkData.WriteByte(0); // Translated keyword (null-terminated, empty)
        chunkData.Write(xmpBytes, 0, xmpBytes.Length);

        byte[] data = chunkData.ToArray();

        // Write chunk length (big-endian)
        int length = data.Length;
        stream.WriteByte((byte)(length >> 24));
        stream.WriteByte((byte)(length >> 16));
        stream.WriteByte((byte)(length >> 8));
        stream.WriteByte((byte)length);

        // Write chunk type
        byte[] type = Encoding.ASCII.GetBytes("iTXt");
        stream.Write(type, 0, 4);

        // Write chunk data
        stream.Write(data, 0, data.Length);

        // Calculate and write CRC
        uint crc = CalculateCrc(type, data);
        stream.WriteByte((byte)(crc >> 24));
        stream.WriteByte((byte)(crc >> 16));
        stream.WriteByte((byte)(crc >> 8));
        stream.WriteByte((byte)crc);
    }

    /// <summary>
    /// Calculates CRC32 for PNG chunk.
    /// </summary>
    private static uint CalculateCrc(byte[] type, byte[] data)
    {
        uint[] crcTable = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                if ((c & 1) != 0)
                    c = 0xEDB88320 ^ (c >> 1);
                else
                    c = c >> 1;
            }
            crcTable[i] = c;
        }

        uint crc = 0xFFFFFFFF;

        foreach (byte b in type)
        {
            crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in data)
        {
            crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>
    /// Attempts to write WEBP metadata using TagLib# as a fallback.
    /// WEBP support in TagLib# is limited, but we try anyway.
    /// </summary>
    private bool TryWriteWebpBasic(string targetPath, ImageAnalysisResult result, int fieldsWritten)
    {
        try
        {
            using var file = TagLib.File.Create(targetPath);

            if (file.Tag == null)
            {
                _logger.Error("WEBP file has no tag support: {TargetPath}", targetPath);
                return false;
            }

            // Try to write basic fields
            if (!string.IsNullOrEmpty(result.Title))
            {
                file.Tag.Title = result.Title;
            }

            if (!string.IsNullOrEmpty(result.Subject) || !string.IsNullOrEmpty(result.Description))
            {
                var comment = !string.IsNullOrEmpty(result.Subject) ? result.Subject : "";
                if (!string.IsNullOrEmpty(result.Description))
                {
                    comment = string.IsNullOrEmpty(comment) ? result.Description : $"{comment} - {result.Description}";
                }
                file.Tag.Comment = comment;
            }

            if (result.FinalTags.Count > 0)
            {
                file.Tag.Genres = result.FinalTags.ToArray();
            }

            file.Save();

            _logger.Warning("Wrote basic metadata to WEBP (limited support). Fields may not persist: {TargetPath}", targetPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to write basic WEBP metadata: {TargetPath}", targetPath);
            return false;
        }
    }
}
