using IrisSort.Services.Logging;
using Serilog;
using SkiaSharp;

namespace IrisSort.Services;

/// <summary>
/// Service for resizing large images to fit within context window limits.
/// </summary>
public class ImageResizerService : IDisposable
{
    private readonly ILogger _logger = LoggerFactory.CreateLogger<ImageResizerService>();
    private readonly List<string> _tempFiles = new();
    private bool _disposed;

    /// <summary>
    /// Checks if an image needs resizing based on file size.
    /// Note: This only checks file size. For LM Studio compatibility,
    /// always check dimensions using CheckDimensionsNeedResizing.
    /// </summary>
    public bool NeedsResizing(long fileSizeBytes, long maxSizeBytes = Constants.MaxImageSizeBytes)
    {
        return fileSizeBytes > maxSizeBytes;
    }

    /// <summary>
    /// Checks if an image needs resizing based on actual dimensions.
    /// This requires opening the file but ensures dimension limits are enforced.
    /// </summary>
    public bool CheckDimensionsNeedResizing(string imagePath, int maxDimension)
    {
        try
        {
            using var stream = File.OpenRead(imagePath);
            using var bitmap = SKBitmap.Decode(stream);

            if (bitmap == null)
            {
                _logger.Warning("Failed to decode image for dimension check: {Path}", imagePath);
                return false;
            }

            bool needsResize = bitmap.Width > maxDimension || bitmap.Height > maxDimension;

            if (needsResize)
            {
                _logger.Debug("Image dimensions {Width}x{Height} exceed max {MaxDim}, resize needed",
                    bitmap.Width, bitmap.Height, maxDimension);
            }

            return needsResize;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error checking image dimensions for {Path}", imagePath);
            return false;
        }
    }

    /// <summary>
    /// Creates a resized copy of the image in a temporary location.
    /// Maintains aspect ratio and preserves the original format.
    /// </summary>
    /// <param name="originalPath">Path to the original image.</param>
    /// <param name="maxDimension">Maximum width or height.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the temporary resized image.</returns>
    public async Task<string> CreateResizedCopyAsync(
        string originalPath,
        int maxDimension,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(originalPath).ToLowerInvariant();

        // Force PNG, WebP, and GIF to be converted to JPEG for better API compatibility
        // LM Studio can fail to decode PNGs with certain features (interlacing, 16-bit color, ICC profiles)
        // and has issues with WebP format. GIFs are animated, so we extract the first frame.
        if (extension == ".webp" || extension == ".png" || extension == ".gif")
        {
            extension = ".jpg";
        }

        var format = GetImageFormat(extension);

        var isGif = Path.GetExtension(originalPath).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        _logger.Information("Resizing/Converting image: {Path} -> {Extension}{GifNote}",
            Path.GetFileName(originalPath), extension, isGif ? " (extracting first frame)" : "");

        // Read original image
        var originalData = await File.ReadAllBytesAsync(originalPath, cancellationToken);

        using var inputStream = new MemoryStream(originalData);
        SKBitmap? originalBitmap;

        // For GIFs, extract only the first frame
        var originalExtension = Path.GetExtension(originalPath).ToLowerInvariant();
        if (originalExtension == ".gif")
        {
            _logger.Debug("Extracting first frame from GIF");
            using var codec = SKCodec.Create(inputStream);
            if (codec == null)
            {
                throw new InvalidOperationException($"Failed to decode GIF: {originalPath}");
            }

            // Create bitmap for the first frame
            var info = codec.Info;
            originalBitmap = new SKBitmap(info.Width, info.Height, info.ColorType, info.AlphaType);

            // Decode only the first frame (index 0)
            var result = codec.GetPixels(info, originalBitmap.GetPixels());
            if (result != SKCodecResult.Success)
            {
                originalBitmap.Dispose();
                throw new InvalidOperationException($"Failed to extract first frame from GIF: {originalPath} (result: {result})");
            }

            _logger.Debug("Extracted first frame from GIF with {Width}x{Height}", originalBitmap.Width, originalBitmap.Height);
        }
        else
        {
            originalBitmap = SKBitmap.Decode(inputStream);
            if (originalBitmap == null)
            {
                throw new InvalidOperationException($"Failed to decode image: {originalPath}");
            }
        }

        _logger.Debug("Original dimensions: {Width}x{Height}", originalBitmap.Width, originalBitmap.Height);

        try
        {
            // Calculate new dimensions maintaining aspect ratio
            var (newWidth, newHeight) = CalculateNewDimensions(
                originalBitmap.Width,
                originalBitmap.Height,
                maxDimension);

            _logger.Debug("Resized dimensions: {Width}x{Height}", newWidth, newHeight);

            // Resize the image
            using var resizedBitmap = originalBitmap.Resize(
                new SKImageInfo(newWidth, newHeight),
                SKFilterQuality.High);

            if (resizedBitmap == null)
            {
                throw new InvalidOperationException($"Failed to resize image: {originalPath}");
            }

            // Encode to the appropriate format
            using var image = SKImage.FromBitmap(resizedBitmap);
            using var encodedData = EncodeImage(image, format);

            // Create temp file
            var tempFileName = $"irissort_resize_{Guid.NewGuid()}{extension}";
            var tempPath = Path.Combine(Path.GetTempPath(), tempFileName);

            await File.WriteAllBytesAsync(tempPath, encodedData.ToArray(), cancellationToken);

            // Track temp file for cleanup
            lock (_tempFiles)
            {
                _tempFiles.Add(tempPath);
            }

            var originalSize = originalData.Length;
            var newSize = encodedData.Size;
            _logger.Information("Image resized: {OriginalSize:N0} bytes → {NewSize:N0} bytes ({Reduction:P0} reduction)",
                originalSize, newSize, 1.0 - ((double)newSize / originalSize));

            return tempPath;
        }
        finally
        {
            // Ensure bitmap is disposed properly
            originalBitmap?.Dispose();
        }
    }

    /// <summary>
    /// Deletes a temporary resized file.
    /// </summary>
    /// <param name="tempFilePath">Path to the temporary file to delete.</param>
    public void DeleteTemporaryFile(string tempFilePath)
    {
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
                _logger.Debug("Deleted temporary file: {Path}", tempFilePath);
            }

            lock (_tempFiles)
            {
                _tempFiles.Remove(tempFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to delete temporary file: {Path}", tempFilePath);
        }
    }

    /// <summary>
    /// Cleans up all tracked temporary files.
    /// </summary>
    public void CleanupAllTempFiles()
    {
        List<string> filesToClean;
        lock (_tempFiles)
        {
            filesToClean = new List<string>(_tempFiles);
            _tempFiles.Clear();
        }

        foreach (var file in filesToClean)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                    _logger.Debug("Cleaned up temp file: {Path}", file);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to cleanup temp file: {Path}", file);
            }
        }
    }

    private static SKEncodedImageFormat GetImageFormat(string extension)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".png" => SKEncodedImageFormat.Png,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Jpeg // Default to JPEG
        };
    }

    private static SKData EncodeImage(SKImage image, SKEncodedImageFormat format)
    {
        // PNG doesn't use quality parameter
        if (format == SKEncodedImageFormat.Png)
        {
            return image.Encode(format, 100);
        }
        
        return image.Encode(format, Constants.ResizedImageQuality);
    }

    private static (int width, int height) CalculateNewDimensions(
        int originalWidth, 
        int originalHeight, 
        int maxDimension)
    {
        // If already within limits, return original dimensions
        if (originalWidth <= maxDimension && originalHeight <= maxDimension)
        {
            return (originalWidth, originalHeight);
        }

        double ratio;
        if (originalWidth > originalHeight)
        {
            ratio = (double)maxDimension / originalWidth;
        }
        else
        {
            ratio = (double)maxDimension / originalHeight;
        }

        return ((int)(originalWidth * ratio), (int)(originalHeight * ratio));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            CleanupAllTempFiles();
        }

        _disposed = true;
    }
}
