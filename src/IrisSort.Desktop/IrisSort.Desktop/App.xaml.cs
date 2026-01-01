using System.Windows;
using IrisSort.Services.Logging;

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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Flush and close logs
        LoggerFactory.CloseAndFlush();
        base.OnExit(e);
    }
}
