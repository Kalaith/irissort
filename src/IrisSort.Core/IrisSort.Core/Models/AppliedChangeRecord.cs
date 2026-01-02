using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IrisSort.Core.Models;

/// <summary>
/// Record of a change that was automatically applied.
/// Used for displaying history and selective undo.
/// </summary>
public class AppliedChangeRecord : INotifyPropertyChanged
{
    private bool _isSelectedForUndo;

    /// <summary>
    /// Original filename before changes.
    /// </summary>
    public string OriginalFilename { get; set; } = string.Empty;

    /// <summary>
    /// New filename after changes.
    /// </summary>
    public string NewFilename { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the original file (before rename).
    /// </summary>
    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the new file (after rename).
    /// </summary>
    public string NewPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether this change included a file rename.
    /// </summary>
    public bool WasRenamed { get; set; }

    /// <summary>
    /// Whether metadata was written.
    /// </summary>
    public bool MetadataWritten { get; set; }

    /// <summary>
    /// When this change was applied.
    /// </summary>
    public DateTime AppliedAt { get; set; }

    /// <summary>
    /// Whether this change is selected for undo.
    /// </summary>
    public bool IsSelectedForUndo
    {
        get => _isSelectedForUndo;
        set
        {
            _isSelectedForUndo = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Session ID for grouping related changes.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Display text for the type of change.
    /// </summary>
    public string ChangeTypeDisplay =>
        WasRenamed ? "Renamed + Metadata" : "Metadata Only";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
