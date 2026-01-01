using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using IrisSort.Core.Models;
using IrisSort.Services;
using IrisSort.Services.Configuration;
using Microsoft.Win32;

namespace IrisSort.Desktop;

/// <summary>
/// View model wrapper for displaying analysis results in the UI.
/// </summary>
public class ResultViewModel : INotifyPropertyChanged
{
    private readonly ImageAnalysisResult _result;

    public ResultViewModel(ImageAnalysisResult result)
    {
        _result = result;
    }

    public string OriginalFilename => _result.OriginalFilename;
    public string SuggestedFilename => _result.SuggestedFilename;
    public string Title => _result.Title;
    public string Subject => _result.Subject;
    public string TagsDisplay => string.Join(", ", _result.Tags.Take(5)) + (_result.Tags.Count > 5 ? "..." : "");
    
    public string StatusDisplay => _result.Status == AnalysisStatus.Failed 
        ? $"Failed: {_result.ErrorMessage?.Substring(0, Math.Min(50, _result.ErrorMessage?.Length ?? 0))}" 
        : _result.Status.ToString();

    public bool IsApproved
    {
        get => _result.IsApproved;
        set
        {
            _result.IsApproved = value;
            OnPropertyChanged();
        }
    }

    public ImageAnalysisResult Result => _result;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Main window for IrisSort application.
/// </summary>
public partial class MainWindow : Window
{
    private readonly LmStudioConfiguration _config;
    private readonly LmStudioVisionService _visionService;
    private readonly ImageAnalyzerService _analyzerService;
    private readonly FolderScannerService _folderScanner;
    private readonly RenamePlannerService _renamePlanner;
    private readonly UndoManagerService _undoManager;

    private CancellationTokenSource? _cancellationTokenSource;
    private string? _selectedPath;
    private bool _isSingleFile;
    private readonly ObservableCollection<ResultViewModel> _results = new();
    private RenameSession? _lastSession;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize services
        _config = new LmStudioConfiguration();
        _visionService = new LmStudioVisionService(_config);
        _analyzerService = new ImageAnalyzerService(_visionService);
        _folderScanner = new FolderScannerService();
        _renamePlanner = new RenamePlannerService();
        _undoManager = new UndoManagerService();

        // Bind results list
        ResultsListView.ItemsSource = _results;

        // Check connection on startup
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckConnectionAsync();
    }

    private async Task CheckConnectionAsync()
    {
        ConnectionStatus.Text = "LM Studio: Checking...";
        ConnectionIndicator.Fill = new SolidColorBrush(Colors.Orange);

        var isAvailable = await _visionService.IsAvailableAsync();

        if (isAvailable)
        {
            ConnectionStatus.Text = "LM Studio: Connected";
            ConnectionIndicator.Fill = (SolidColorBrush)FindResource("SuccessBrush");
            StatusText.Text = "Ready - LM Studio connected";
        }
        else
        {
            ConnectionStatus.Text = "LM Studio: Not Connected";
            ConnectionIndicator.Fill = (SolidColorBrush)FindResource("ErrorBrush");
            StatusText.Text = "Warning: Start LM Studio and load a vision model";
        }
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select folder containing images"
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedPath = dialog.FolderName;
            _isSingleFile = false;
            SelectedPathText.Text = _selectedPath;
            AnalyzeButton.IsEnabled = true;
            StatusText.Text = $"Selected folder: {_selectedPath}";
        }
    }

    private void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select image to analyze",
            Filter = "Image files (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedPath = dialog.FileName;
            _isSingleFile = true;
            SelectedPathText.Text = _selectedPath;
            AnalyzeButton.IsEnabled = true;
            StatusText.Text = $"Selected file: {Path.GetFileName(_selectedPath)}";
        }
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedPath))
        {
            return;
        }

        // Check connection first
        var isAvailable = await _visionService.IsAvailableAsync();
        if (!isAvailable)
        {
            MessageBox.Show(
                "Cannot connect to LM Studio.\n\nPlease ensure:\n1. LM Studio is running\n2. Local server is started\n3. A vision-capable model is loaded",
                "Connection Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Switch to progress state
        EmptyState.Visibility = Visibility.Collapsed;
        ResultsState.Visibility = Visibility.Collapsed;
        ProgressState.Visibility = Visibility.Visible;
        AnalyzeButton.IsEnabled = false;
        SelectFolderButton.IsEnabled = false;
        SelectFileButton.IsEnabled = false;

        _results.Clear();
        _cancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<(int current, int total, string fileName)>(p =>
        {
            AnalysisProgress.Maximum = p.total;
            AnalysisProgress.Value = p.current;
            ProgressText.Text = $"Analyzing {p.current} of {p.total}...";
            ProgressDetail.Text = p.fileName;
        });

        try
        {
            List<ImageAnalysisResult> results;

            if (_isSingleFile)
            {
                ProgressText.Text = "Analyzing image...";
                var result = await _analyzerService.AnalyzeImageAsync(_selectedPath, _cancellationTokenSource.Token);
                results = new List<ImageAnalysisResult> { result };
            }
            else
            {
                results = await _analyzerService.AnalyzeDirectoryAsync(
                    _selectedPath,
                    recursive: false,
                    progress,
                    _cancellationTokenSource.Token);
            }

            // Populate results
            foreach (var result in results)
            {
                _results.Add(new ResultViewModel(result));
            }

            // Update summary
            var successCount = results.Count(r => r.Status == AnalysisStatus.Success);
            var failedCount = results.Count(r => r.Status == AnalysisStatus.Failed);
            ResultsSummary.Text = $"{successCount} images analyzed" + (failedCount > 0 ? $", {failedCount} failed" : "");

            // Switch to results state
            ProgressState.Visibility = Visibility.Collapsed;
            ResultsState.Visibility = Visibility.Visible;
            
            // Show error details if any failed
            if (failedCount > 0)
            {
                var firstError = results.FirstOrDefault(r => r.Status == AnalysisStatus.Failed)?.ErrorMessage ?? "";
                // Truncate long error messages
                if (firstError.Length > 80)
                {
                    firstError = firstError.Substring(0, 80) + "...";
                }
                StatusText.Text = $"Analysis complete: {successCount} ok, {failedCount} failed. Last error: {firstError}";
            }
            else
            {
                StatusText.Text = $"Analysis complete: {successCount} images processed";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Analysis cancelled";
            ProgressState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Analysis failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Analysis failed";
            ProgressState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
            SelectFolderButton.IsEnabled = true;
            SelectFileButton.IsEnabled = true;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
    }

    private void AcceptAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var result in _results)
        {
            if (result.Result.Status == AnalysisStatus.Success)
            {
                result.IsApproved = true;
            }
        }
    }

    private void RejectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var result in _results)
        {
            result.IsApproved = false;
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var approvedResults = _results.Where(r => r.IsApproved).Select(r => r.Result).ToList();

        if (approvedResults.Count == 0)
        {
            MessageBox.Show("No images selected for renaming.", "Nothing to Apply", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"This will rename {approvedResults.Count} image(s).\n\nContinue?",
            "Confirm Rename",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        ApplyButton.IsEnabled = false;
        StatusText.Text = "Applying changes...";

        try
        {
            var operations = _renamePlanner.PlanRenames(approvedResults);
            
            // Build lookup for metadata writing
            var resultsByPath = approvedResults.ToDictionary(r => r.OriginalPath, r => r);
            var session = await _renamePlanner.ExecuteRenamesAsync(operations, resultsByPath, writeMetadata: true);

            await _undoManager.SaveSessionAsync(session);
            _lastSession = session;

            StatusText.Text = $"Renamed {session.SuccessCount} files" + 
                              (session.FailedCount > 0 ? $" ({session.FailedCount} failed)" : "");

            UndoButton.IsEnabled = true;

            // Clear results
            _results.Clear();
            ResultsState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSession == null)
        {
            _lastSession = await _undoManager.GetLastSessionAsync();
        }

        if (_lastSession == null)
        {
            MessageBox.Show("No operations to undo.", "Nothing to Undo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"This will revert {_lastSession.SuccessCount} rename(s).\n\nContinue?",
            "Confirm Undo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        UndoButton.IsEnabled = false;
        StatusText.Text = "Undoing changes...";

        try
        {
            var reverted = await _renamePlanner.RevertSessionAsync(_lastSession);
            await _undoManager.MarkSessionUndoneAsync(_lastSession);

            StatusText.Text = $"Reverted {reverted} files";
            _lastSession = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to undo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            UndoButton.IsEnabled = true;
        }
    }

    private void ResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsListView.SelectedItem is ResultViewModel vm && vm.Result.Status == AnalysisStatus.Failed)
        {
            var error = vm.Result.ErrorMessage ?? "No error message";
            Clipboard.SetText(error);
            StatusText.Text = "Error copied to clipboard";
        }
    }

    private void CopyErrorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsListView.SelectedItem is ResultViewModel vm)
        {
            var error = vm.Result.ErrorMessage ?? "No error";
            Clipboard.SetText(error);
            StatusText.Text = "Error copied to clipboard";
        }
    }

    private void CopyAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsListView.SelectedItem is ResultViewModel vm)
        {
            var result = vm.Result;
            var details = $@"File: {result.OriginalFilename}
Status: {result.Status}
Suggested Name: {result.SuggestedFilename}
Title: {result.Title}
Subject: {result.Subject}
Tags: {string.Join(", ", result.Tags)}
Description: {result.Description}
Comments: {result.Comments}
Error: {result.ErrorMessage ?? "None"}";
            
            Clipboard.SetText(details);
            StatusText.Text = "All details copied to clipboard";
        }
    }
}
