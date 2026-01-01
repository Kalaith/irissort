using IrisSort.Core.Models;
using IrisSort.Services.Logging;
using Serilog;

namespace IrisSort.Services;

/// <summary>
/// Service for planning and executing safe file rename operations.
/// </summary>
public class RenamePlannerService
{
    private readonly ILogger _logger;

    public RenamePlannerService(ILogger? logger = null)
    {
        _logger = logger ?? LoggerFactory.CreateLogger<RenamePlannerService>();
    }

    /// <summary>
    /// Plans rename operations with collision resolution.
    /// </summary>
    public List<RenameOperation> PlanRenames(IEnumerable<ImageAnalysisResult> results)
    {
        var operations = new List<RenameOperation>();
        var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in results.Where(r => r.IsApproved && r.Status == AnalysisStatus.Success))
        {
            var directory = Path.GetDirectoryName(result.OriginalPath) ?? ".";
            var newFilename = result.FinalFilename;
            var extension = result.Extension;

            // Resolve collisions
            var baseName = newFilename;
            var counter = 1;
            var fullNewName = $"{newFilename}{extension}";
            var potentialPath = Path.Combine(directory, fullNewName);

            while (usedNames.ContainsKey(fullNewName) ||
                   (File.Exists(potentialPath) &&
                    !potentialPath.Equals(result.OriginalPath, StringComparison.OrdinalIgnoreCase)))
            {
                newFilename = $"{baseName}_{counter}";
                fullNewName = $"{newFilename}{extension}";
                potentialPath = Path.Combine(directory, fullNewName);
                counter++;
            }

            usedNames[fullNewName] = 1;

            var newPath = potentialPath;

            // Skip if same path
            if (newPath.Equals(result.OriginalPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            operations.Add(new RenameOperation
            {
                OriginalPath = result.OriginalPath,
                NewPath = newPath,
                WrittenTags = result.FinalTags.ToList()
            });
        }

        return operations;
    }

    /// <summary>
    /// Executes planned rename operations.
    /// </summary>
    public async Task<RenameSession> ExecuteRenamesAsync(
        List<RenameOperation> operations,
        Dictionary<string, ImageAnalysisResult>? resultsByPath = null,
        bool writeMetadata = true,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var session = new RenameSession();
        var metadataWriter = writeMetadata ? new MetadataWriterService() : null;
        var total = operations.Count;

        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var operation = operations[i];
            var fileName = Path.GetFileName(operation.OriginalPath);
            progress?.Report((i + 1, total, fileName));

            try
            {
                // Perform rename
                File.Move(operation.OriginalPath, operation.NewPath);
                operation.WasSuccessful = true;
                operation.ExecutedAt = DateTime.Now;

                // Write metadata if requested and we have the analysis result
                if (metadataWriter != null && resultsByPath != null &&
                    resultsByPath.TryGetValue(operation.OriginalPath, out var result))
                {
                    try
                    {
                        // Small delay to ensure file handle is released after rename on Windows
                        await Task.Delay(50, cancellationToken);

                        var metadataWritten = await metadataWriter.WriteMetadataAsync(result, operation.NewPath, cancellationToken);
                        operation.MetadataUpdated = metadataWritten;

                        if (!metadataWritten)
                        {
                            _logger.Warning("Metadata write returned false for {NewPath}. Check logs for format support or file issues.", operation.NewPath);
                            operation.ErrorMessage = $"File renamed but metadata write failed (check logs for details)";
                        }
                        else
                        {
                            _logger.Information("Metadata successfully written for {NewPath}", operation.NewPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Metadata write failed, but file was renamed
                        _logger.Error(ex, "Metadata write exception for {NewPath}", operation.NewPath);
                        operation.ErrorMessage = $"File renamed but metadata write failed: {ex.Message}";
                    }
                }
            }
            catch (Exception ex)
            {
                operation.WasSuccessful = false;
                operation.ErrorMessage = ex.Message;
                operation.ExecutedAt = DateTime.Now;
            }

            session.Operations.Add(operation);
        }

        return session;
    }

    /// <summary>
    /// Reverts a rename session (undo).
    /// </summary>
    public async Task<int> RevertSessionAsync(
        RenameSession session,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var successfulOps = session.Operations
                .Where(o => o.WasSuccessful && File.Exists(o.NewPath))
                .Reverse() // Undo in reverse order
                .ToList();

            var reverted = 0;
            var total = successfulOps.Count;

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = successfulOps[i];
                var fileName = Path.GetFileName(operation.NewPath);
                progress?.Report((i + 1, total, fileName));

                try
                {
                    File.Move(operation.NewPath, operation.OriginalPath);
                    reverted++;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to revert file from {NewPath} to {OriginalPath}",
                        operation.NewPath, operation.OriginalPath);
                }
            }

            session.IsUndone = true;
            return reverted;
        }, cancellationToken);
    }
}
