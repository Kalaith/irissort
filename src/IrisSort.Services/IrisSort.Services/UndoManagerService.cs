using System.Text.Json;
using IrisSort.Core.Models;
using IrisSort.Services.Logging;
using Serilog;

namespace IrisSort.Services;

/// <summary>
/// Service for managing undo operations by persisting session logs.
/// </summary>
public class UndoManagerService
{
    private readonly string _logDirectory;
    private readonly ILogger _logger;
    private RenameSession? _lastSession;

    public UndoManagerService(string? logDirectory = null, ILogger? logger = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IrisSort", "logs");
        _logger = logger ?? LoggerFactory.CreateLogger<UndoManagerService>();

        Directory.CreateDirectory(_logDirectory);
    }

    /// <summary>
    /// Saves a session for potential undo.
    /// </summary>
    public async Task SaveSessionAsync(RenameSession session, CancellationToken cancellationToken = default)
    {
        _lastSession = session;

        var fileName = $"session_{session.CreatedAt:yyyyMMdd_HHmmss}_{session.SessionId}.json";
        var filePath = Path.Combine(_logDirectory, fileName);

        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    /// <summary>
    /// Gets the most recent session that can be undone.
    /// </summary>
    public async Task<RenameSession?> GetLastSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_lastSession != null && !_lastSession.IsUndone)
        {
            return _lastSession;
        }

        var files = Directory.GetFiles(_logDirectory, "session_*.json")
            .OrderByDescending(f => f)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var session = JsonSerializer.Deserialize<RenameSession>(json);
                if (session != null && !session.IsUndone)
                {
                    return session;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to read session file {File}", file);
            }
        }

        return null;
    }

    /// <summary>
    /// Lists all saved sessions.
    /// </summary>
    public async Task<List<RenameSession>> GetAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = new List<RenameSession>();
        var files = Directory.GetFiles(_logDirectory, "session_*.json")
            .OrderByDescending(f => f);

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var session = JsonSerializer.Deserialize<RenameSession>(json);
                if (session != null)
                {
                    sessions.Add(session);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to read session file {File}", file);
            }
        }

        return sessions;
    }

    /// <summary>
    /// Marks a session as undone.
    /// </summary>
    public async Task MarkSessionUndoneAsync(RenameSession session, CancellationToken cancellationToken = default)
    {
        session.IsUndone = true;

        // Update the persisted file
        var files = Directory.GetFiles(_logDirectory, $"*{session.SessionId}.json");
        if (files.Length > 0)
        {
            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(files[0], json, cancellationToken);
        }
    }

    /// <summary>
    /// Clears all session logs.
    /// </summary>
    public void ClearAllLogs()
    {
        foreach (var file in Directory.GetFiles(_logDirectory, "session_*.json"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete session file {File}", file);
            }
        }
        _lastSession = null;
    }
}
