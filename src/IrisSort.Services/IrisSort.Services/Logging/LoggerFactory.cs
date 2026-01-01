using Serilog;
using Serilog.Events;

namespace IrisSort.Services.Logging;

/// <summary>
/// Factory for creating and configuring Serilog loggers.
/// </summary>
public static class LoggerFactory
{
    private static bool _initialized;

    /// <summary>
    /// Initializes the global logger configuration.
    /// </summary>
    public static void Initialize(string? logFilePath = null, LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        if (_initialized)
            return;

        var logPath = logFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IrisSort", "logs", "irissort.log");

        var logDir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _initialized = true;
    }

    /// <summary>
    /// Creates a logger for a specific type.
    /// </summary>
    public static ILogger CreateLogger<T>()
    {
        if (!_initialized)
            Initialize();

        return Log.ForContext<T>();
    }

    /// <summary>
    /// Creates a logger for a specific type.
    /// </summary>
    public static ILogger CreateLogger(Type type)
    {
        if (!_initialized)
            Initialize();

        return Log.ForContext(type);
    }

    /// <summary>
    /// Closes and flushes the logger.
    /// </summary>
    public static void CloseAndFlush()
    {
        Log.CloseAndFlush();
    }
}
