using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IrisSort.Services.Logging;
using Serilog;

namespace IrisSort.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize Serilog logging
        LoggerFactory.Initialize();

        // Global exception handlers to catch silent crashes
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        
        var message = GetUserFriendlyMessage(e.Exception);
        MessageBox.Show(
            $"An error occurred:\n\n{message}\n\nThe error has been logged.",
            "IrisSort Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        
        e.Handled = true; // Prevent crash, allow app to continue
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved(); // Prevent crash
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        Log.Fatal(exception, "Fatal unhandled exception (IsTerminating: {IsTerminating})", e.IsTerminating);
        
        if (exception != null)
        {
            var message = GetUserFriendlyMessage(exception);
            MessageBox.Show(
                $"A fatal error occurred:\n\n{message}\n\nThe application will close.",
                "IrisSort Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string GetUserFriendlyMessage(Exception ex)
    {
        return ex switch
        {
            OutOfMemoryException => "The image is too large to process. Try using a smaller image or freeing up memory.",
            HttpRequestException => $"Network error: {ex.Message}",
            TaskCanceledException => "The operation was cancelled or timed out.",
            _ => ex.Message
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Flush and close logs
        LoggerFactory.CloseAndFlush();
        base.OnExit(e);
    }
}
