using IrisSort.Core.Models;

namespace IrisSort.Services;

/// <summary>
/// Service for planning and executing safe file rename operations.
/// </summary>
public class RenamePlannerService
{
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

            while (usedNames.ContainsKey(fullNewName) || 
                   (File.Exists(Path.Combine(directory, fullNewName)) && 
                    !Path.Combine(directory, fullNewName).Equals(result.OriginalPath, StringComparison.OrdinalIgnoreCase)))
            {
                newFilename = $"{baseName}_{counter}";
                fullNewName = $"{newFilename}{extension}";
                counter++;
            }

            usedNames[fullNewName] = 1;

            var newPath = Path.Combine(directory, fullNewName);

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
                        await metadataWriter.WriteMetadataAsync(result, operation.NewPath, cancellationToken);
                        operation.MetadataUpdated = true;
                    }
                    catch (Exception ex)
                    {
                        // Metadata write failed, but file was renamed
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
                catch
                {
                    // Failed to revert this file, continue with others
                }
            }

            session.IsUndone = true;
            return reverted;
        }, cancellationToken);
    }
}
