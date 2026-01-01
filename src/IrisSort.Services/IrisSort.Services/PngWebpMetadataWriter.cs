using System.Text;
using IrisSort.Core.Models;
using IrisSort.Services.Logging;
using Serilog;
using TagLib;
using System.Security; // Added for SecurityElement.Escape

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
    /// Manually constructs XMP XML to satisfy Windows Explorer quirks.
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

                // Generate Windows-compatible XMP String
                string xmpString = GenerateWindowsCompatibleXmp(result);
                _logger.Debug("Generated XMP metadata ({Length} bytes)", xmpString.Length);

                // For PNG files, inject XMP iTXt
                if (extension == ".png")
                {
                    if (InjectPngMetadata(targetPath, xmpString))
                    {
                        _logger.Information("Successfully wrote metadata to PNG: {TargetPath}", targetPath);
                        return true;
                    }
                    else
                    {
                        _logger.Error("Failed to inject metadata into PNG: {TargetPath}", targetPath);
                        return false;
                    }
                }
                // For WEBP files, we use TagLib# fallback as before
                else if (extension == ".webp")
                {
                    _logger.Warning("WEBP XMP writing not fully supported - attempting basic simple write.");
                    return TryWriteWebpBasic(targetPath, result);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error writing metadata to {TargetPath}", targetPath);
                return false;
            }
        }, cancellationToken);
    }

    private string GenerateWindowsCompatibleXmp(ImageAnalysisResult result)
    {
        // Windows Explorer is very picky about XMP structure in PNGs.
        // It prefers separate rdf:Description blocks for different namespaces.
        // We manually build this string to ensure maximum compatibility.

        var sb = new StringBuilder();
        string uuid = "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b"; // Standard generic UUID

        sb.Append("<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?>");
        sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");

        // 1. User Comment / Description (exif:UserComment)
        var fullDescription = result.Description;
        if (!string.IsNullOrEmpty(result.Comments))
        {
            fullDescription = string.IsNullOrEmpty(fullDescription)
                ? result.Comments
                : $"{fullDescription}\n\n{result.Comments}";
        }

        if (!string.IsNullOrEmpty(fullDescription))
        {
            // Sanitize XML
            string cleanDesc = System.Security.SecurityElement.Escape(fullDescription);
            
            sb.Append($"<rdf:Description rdf:about=\"{uuid}\" xmlns:exif=\"http://ns.adobe.com/exif/1.0/\">");
            sb.Append("<exif:UserComment><rdf:Alt xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
            sb.Append($"<rdf:li xml:lang=\"x-default\">{cleanDesc}</rdf:li>");
            sb.Append("</rdf:Alt></exif:UserComment>");
            sb.Append("</rdf:Description>");
        }

        // 2. Keywords / Subject (dc:subject)
        if (result.FinalTags != null && result.FinalTags.Count > 0)
        {
            sb.Append($"<rdf:Description rdf:about=\"{uuid}\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
            sb.Append("<dc:subject><rdf:Bag xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
            foreach (var tag in result.FinalTags)
            {
                sb.Append($"<rdf:li>{System.Security.SecurityElement.Escape(tag)}</rdf:li>");
            }
            sb.Append("</rdf:Bag></dc:subject>");
            sb.Append("</rdf:Description>");
        }

        // 3. Title (dc:title)
        if (!string.IsNullOrEmpty(result.Title))
        {
            sb.Append($"<rdf:Description rdf:about=\"{uuid}\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
            sb.Append("<dc:title><rdf:Alt xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
            sb.Append($"<rdf:li xml:lang=\"x-default\">{System.Security.SecurityElement.Escape(result.Title)}</rdf:li>");
            sb.Append("</rdf:Alt></dc:title>");
            sb.Append("</rdf:Description>");
        }
        
        // 4. Author / Creator (dc:creator)
        if (!string.IsNullOrEmpty(result.Authors))
        {
             sb.Append($"<rdf:Description rdf:about=\"{uuid}\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
             sb.Append("<dc:creator><rdf:Seq xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
             sb.Append($"<rdf:li>{System.Security.SecurityElement.Escape(result.Authors)}</rdf:li>");
             sb.Append("</rdf:Seq></dc:creator>");
             sb.Append("</rdf:Description>");
        }
        
        // 5. Rights / Copyright (dc:rights)
        if (!string.IsNullOrEmpty(result.Copyright))
        {
            sb.Append($"<rdf:Description rdf:about=\"{uuid}\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
            sb.Append("<dc:rights><rdf:Alt xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
            sb.Append($"<rdf:li xml:lang=\"x-default\">{System.Security.SecurityElement.Escape(result.Copyright)}</rdf:li>");
            sb.Append("</rdf:Alt></dc:rights>");
            sb.Append("</rdf:Description>");
        }

        sb.Append("</rdf:RDF></x:xmpmeta>");
        sb.Append("<?xpacket end='w'?>");

        return sb.ToString();
    }

    /// <summary>
    /// Injects XMP (iTXt) chunk into a PNG file.
    /// Inserts it before the first IDAT chunk for compatibility.
    /// </summary>
    private bool InjectPngMetadata(string filePath, string xmpString)
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
            bool metadataInserted = false;

            // Parse PNG chunks
            while (position < fileData.Length)
            {
                if (position + 12 > fileData.Length) break;

                uint chunkLengthUni = ((uint)fileData[position] << 24) | 
                                      ((uint)fileData[position + 1] << 16) |
                                      ((uint)fileData[position + 2] << 8) | 
                                      (uint)fileData[position + 3];
                int chunkLength = (int)chunkLengthUni;
                
                if (chunkLength < 0) return false;

                int totalChunkSize = 12 + chunkLength;
                if (position + totalChunkSize > fileData.Length) return false;

                string chunkType = Encoding.ASCII.GetString(fileData, position + 4, 4);

                // Check for existing iTXt XMP to remove/skip
                bool skipChunk = false;

                if (chunkType == "iTXt")
                {
                     // Read keyword to check if we should replace it
                     int maxLen = Math.Min(chunkLength, 80); 
                     byte[] keywordData = new byte[maxLen];
                     Array.Copy(fileData, position + 8, keywordData, 0, maxLen);
                     
                     // Find null separator
                     int nullIdx = Array.IndexOf(keywordData, (byte)0);
                     if (nullIdx > 0)
                     {
                         string keyword = Encoding.ASCII.GetString(keywordData, 0, nullIdx);
                         
                         // If it's XMP, skip it (we will write our own)
                         if (keyword.StartsWith("XML:com.adobe.xmp")) skipChunk = true;
                     }
                }

                // Critical: Insert Metadata BEFORE the first IDAT chunk
                if (chunkType == "IDAT" && !metadataInserted)
                {
                    // Write XMP iTXt
                    WriteITXtChunk(outputStream, "XML:com.adobe.xmp", xmpString);
                    metadataInserted = true;
                    _logger.Debug("Inserted XMP iTXt chunk before IDAT");
                }

                if (!skipChunk)
                {
                    outputStream.Write(fileData, position, totalChunkSize);
                }
                else
                {
                    _logger.Debug("Removing existing metadata chunk: {Type}", chunkType);
                }

                position += totalChunkSize;

                // If we hit IEND (and for some reason IDAT wasn't found before it, e.g. text-only PNG? unlikely)
                if (chunkType == "IEND" && !metadataInserted)
                {
                     // Technically should be before IEND?
                }
            }
            
            // Edge case: empty image or no IDAT found? 
            if (!metadataInserted)
            {
                WriteITXtChunk(outputStream, "XML:com.adobe.xmp", xmpString);
            }

            System.IO.File.WriteAllBytes(filePath, outputStream.ToArray());
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to inject metadata into PNG {FilePath}", filePath);
            return false;
        }
    }

    private void WriteITXtChunk(Stream stream, string keyword, string text)
    {
        byte[] keywordBytes = Encoding.UTF8.GetBytes(keyword);
        byte[] textBytes = Encoding.UTF8.GetBytes(text);

        using var chunkData = new MemoryStream();
        chunkData.Write(keywordBytes, 0, keywordBytes.Length);
        chunkData.WriteByte(0); // Null
        chunkData.WriteByte(0); // Compression flag (0)
        chunkData.WriteByte(0); // Compression method (0)
        chunkData.WriteByte(0); // Lang tag (empty, null)
        chunkData.WriteByte(0); // Trans keyword (empty, null)
        chunkData.Write(textBytes, 0, textBytes.Length);

        WriteChunk(stream, "iTXt", chunkData.ToArray());
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        // Length
        byte[] len = BitConverter.GetBytes((uint)data.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(len);
        stream.Write(len, 0, 4);

        // Type
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes, 0, 4);

        // Data
        stream.Write(data, 0, data.Length);

        // CRC
        uint crc = CalculateCrc(typeBytes, data);
        byte[] crcBytes = BitConverter.GetBytes(crc);
        if (BitConverter.IsLittleEndian) Array.Reverse(crcBytes);
        stream.Write(crcBytes, 0, 4);
    }

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
    private bool TryWriteWebpBasic(string targetPath, ImageAnalysisResult result)
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
