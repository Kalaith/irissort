using System.Collections.Concurrent;
using IrisSort.Core.Models;
using IrisSort.Services.Configuration;
using IrisSort.Services.Configuration;
using IrisSort.Services.Exceptions;
using SkiaSharp;

namespace IrisSort.Services;

/// <summary>
/// Orchestrates image analysis using the LM Studio vision service.
/// </summary>
public class ImageAnalyzerService : IDisposable
{
    private readonly LmStudioVisionService _visionService;
    private readonly FolderScannerService _folderScanner;
    private readonly ImageResizerService _imageResizer;
    private readonly bool _ownsVisionService;
    private readonly ConcurrentDictionary<string, ImageAnalysisResult> _cache = new();
    private bool _disposed;

    public ImageAnalyzerService(LmStudioVisionService visionService, FolderScannerService? folderScanner = null, bool ownsVisionService = false)
    {
        _visionService = visionService ?? throw new ArgumentNullException(nameof(visionService));
        _folderScanner = folderScanner ?? new FolderScannerService();
        _imageResizer = new ImageResizerService();
        _ownsVisionService = ownsVisionService;
    }

    /// <summary>
    /// Analyzes a single image file.
    /// </summary>
    public async Task<ImageAnalysisResult> AnalyzeImageAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new ImageAnalysisResult
            {
                OriginalPath = filePath,
                Status = AnalysisStatus.Failed,
                ErrorMessage = "File not found"
            };
        }

        var fileInfo = new FileInfo(filePath);
        var result = new ImageAnalysisResult
        {
            OriginalPath = filePath,
            OriginalFilename = fileInfo.Name,
            Extension = fileInfo.Extension.ToLowerInvariant(),
            FileSizeBytes = fileInfo.Length,
            Status = AnalysisStatus.Analyzing
        };

        try
        {
            // Calculate file hash for caching
            result.FileHash = await FolderScannerService.CalculateFileHashAsync(filePath, cancellationToken);

            // Check cache
            if (_cache.TryGetValue(result.FileHash, out var cached))
            {
                cached.OriginalPath = filePath;
                cached.OriginalFilename = fileInfo.Name;
                return cached;
            }

            // Check if image needs resizing due to size limits OR if it's WebP (needs conversion)
            string? tempResizedPath = null;
            byte[] imageData;
            string mimeType;

            try
            {
                var isWebP = fileInfo.Extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);

                var maxDimension = _visionService.Configuration.MaxImageDimension;
                bool needsResizing = false;

                // Check file size first (fast)
                if (_imageResizer.NeedsResizing(fileInfo.Length))
                {
                    needsResizing = true;
                }
                else
                {
                    // Check dimensions (slower, requires reading header)
                    using var stream = File.OpenRead(filePath);
                    using var codec = SKCodec.Create(stream);
                    if (codec != null)
                    {
                        if (codec.Info.Width > maxDimension || codec.Info.Height > maxDimension)
                        {
                            needsResizing = true;
                        }
                    }
                }

                if (needsResizing || isWebP)
                {
                    // Create resized/converted copy for analysis
                    // ImageResizerService automatically converts WebP to JPEG
                    tempResizedPath = await _imageResizer.CreateResizedCopyAsync(filePath, maxDimension, cancellationToken);
                    imageData = await File.ReadAllBytesAsync(tempResizedPath, cancellationToken);
                    mimeType = FolderScannerService.GetMimeType(tempResizedPath); // Will be image/jpeg for converted WebP
                }
                else
                {
                    imageData = await File.ReadAllBytesAsync(filePath, cancellationToken);
                    mimeType = FolderScannerService.GetMimeType(filePath);
                }

                // Call vision API with original filename for context
                var apiResponse = await _visionService.AnalyzeWithRetryAsync(
                    imageData, mimeType, result.OriginalFilename, cancellationToken);

                // Copy all metadata from API response
                result.SuggestedFilename = apiResponse.SuggestedFilename;
                result.Tags = apiResponse.Tags;
                result.Description = apiResponse.Description;
                result.Title = apiResponse.Title;
                result.Subject = apiResponse.Subject;
                result.Comments = apiResponse.Comments;
                result.Authors = apiResponse.Authors;
                result.Copyright = apiResponse.Copyright;
                result.VisibleDate = apiResponse.VisibleDate;
                result.Status = AnalysisStatus.Success;
                result.AnalyzedAt = DateTime.Now;

                // Cache result (thread-safe)
                _cache.AddOrUpdate(result.FileHash, result, (key, old) => result);

                return result;
            }
            finally
            {
                // Clean up temporary resized file if created
                if (tempResizedPath != null)
                {
                    _imageResizer.DeleteTemporaryFile(tempResizedPath);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Re-throw cancellation to allow batch processing to handle it properly
            throw;
        }
        catch (LmStudioApiException ex)
        {
            result.Status = AnalysisStatus.Failed;
            result.ErrorMessage = ex.Message;
            return result;
        }
        catch (Exception ex)
        {
            result.Status = AnalysisStatus.Failed;
            result.ErrorMessage = $"Unexpected error: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// Analyzes multiple images with progress reporting.
    /// Returns partial results if cancelled or if individual images fail.
    /// </summary>
    public async Task<List<ImageAnalysisResult>> AnalyzeBatchAsync(
        IEnumerable<string> filePaths,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = filePaths.ToList();
        var results = new List<ImageAnalysisResult>();
        var total = files.Count;

        for (int i = 0; i < total; i++)
        {
            // Check for cancellation but don't throw - return partial results instead
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var filePath = files[i];
            var fileName = Path.GetFileName(filePath);

            progress?.Report((i + 1, total, fileName));

            try
            {
                var result = await AnalyzeImageAsync(filePath, cancellationToken);
                results.Add(result);
            }
            catch (OperationCanceledException)
            {
                // Cancellation occurred mid-analysis - break loop and return partial results
                break;
            }
            catch (Exception ex)
            {
                // If individual image fails catastrophically, create error result and continue
                var fileInfo = new FileInfo(filePath);
                results.Add(new ImageAnalysisResult
                {
                    OriginalPath = filePath,
                    OriginalFilename = fileInfo.Name,
                    Extension = fileInfo.Extension.ToLowerInvariant(),
                    FileSizeBytes = fileInfo.Length,
                    Status = AnalysisStatus.Failed,
                    ErrorMessage = $"Analysis failed: {ex.Message}"
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Analyzes all images in a directory.
    /// </summary>
    public async Task<List<ImageAnalysisResult>> AnalyzeDirectoryAsync(
        string directoryPath,
        bool recursive = false,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = await _folderScanner.ScanDirectoryAsync(directoryPath, recursive, cancellationToken);
        return await AnalyzeBatchAsync(files, progress, cancellationToken);
    }

    /// <summary>
    /// Clears the analysis cache.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the service resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _imageResizer?.Dispose();
            if (_ownsVisionService)
            {
                _visionService?.Dispose();
            }
        }

        _disposed = true;
    }
}
