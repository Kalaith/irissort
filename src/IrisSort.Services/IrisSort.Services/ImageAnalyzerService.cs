using IrisSort.Core.Models;
using IrisSort.Services.Configuration;
using IrisSort.Services.Exceptions;

namespace IrisSort.Services;

/// <summary>
/// Orchestrates image analysis using the LM Studio vision service.
/// </summary>
public class ImageAnalyzerService
{
    private readonly LmStudioVisionService _visionService;
    private readonly FolderScannerService _folderScanner;
    private readonly Dictionary<string, ImageAnalysisResult> _cache = new();

    public ImageAnalyzerService(LmStudioVisionService visionService, FolderScannerService? folderScanner = null)
    {
        _visionService = visionService ?? throw new ArgumentNullException(nameof(visionService));
        _folderScanner = folderScanner ?? new FolderScannerService();
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

            // Read image data
            var imageData = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var mimeType = FolderScannerService.GetMimeType(filePath);

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

            // Cache result
            _cache[result.FileHash] = result;

            return result;
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
            cancellationToken.ThrowIfCancellationRequested();

            var filePath = files[i];
            var fileName = Path.GetFileName(filePath);

            progress?.Report((i + 1, total, fileName));

            var result = await AnalyzeImageAsync(filePath, cancellationToken);
            results.Add(result);
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
}
