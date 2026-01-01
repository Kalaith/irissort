namespace IrisSort.Core.Models;

/// <summary>
/// Tracks a file rename operation for undo capability.
/// </summary>
public class RenameOperation
{
    /// <summary>
    /// Original file path before rename.
    /// </summary>
    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>
    /// New file path after rename.
    /// </summary>
    public string NewPath { get; set; } = string.Empty;

    /// <summary>
    /// When the rename was executed.
    /// </summary>
    public DateTime ExecutedAt { get; set; }

    /// <summary>
    /// Whether the rename completed successfully.
    /// </summary>
    public bool WasSuccessful { get; set; }

    /// <summary>
    /// Error message if rename failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Tags that were written to metadata (if any).
    /// </summary>
    public List<string>? WrittenTags { get; set; }

    /// <summary>
    /// Whether metadata was updated.
    /// </summary>
    public bool MetadataUpdated { get; set; }
}

/// <summary>
/// A session containing multiple rename operations.
/// </summary>
public class RenameSession
{
    /// <summary>
    /// Unique identifier for the session.
    /// </summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the session was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// List of operations in this session.
    /// </summary>
    public List<RenameOperation> Operations { get; set; } = new();

    /// <summary>
    /// Whether this session has been undone.
    /// </summary>
    public bool IsUndone { get; set; }

    /// <summary>
    /// Total number of successful operations.
    /// </summary>
    public int SuccessCount => Operations.Count(o => o.WasSuccessful);

    /// <summary>
    /// Total number of failed operations.
    /// </summary>
    public int FailedCount => Operations.Count(o => !o.WasSuccessful);
}
