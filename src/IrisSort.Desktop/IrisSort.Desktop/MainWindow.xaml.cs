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
using Constants = IrisSort.Services.Constants;

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
    public string SuggestedFilename => string.IsNullOrEmpty(_result.SuggestedFilename) ? "(not analyzed)" : _result.SuggestedFilename;
    public string Title => _result.Title;
    public string Subject => _result.Subject;
    public string TagsDisplay => string.Join(", ", _result.Tags.Take(Constants.MaxTagsDisplayCount)) +
                                  (_result.Tags.Count > Constants.MaxTagsDisplayCount ? "..." : "");
    
    public string StatusDisplay
    {
        get
        {
            if (_result.Status == AnalysisStatus.Failed)
            {
                if (string.IsNullOrEmpty(_result.ErrorMessage))
                    return "Failed";

                var maxLength = Math.Min(Constants.MaxErrorMessagePreviewLength, _result.ErrorMessage.Length);
                return $"Failed: {_result.ErrorMessage.Substring(0, maxLength)}";
            }
            else if (_result.Status == AnalysisStatus.Pending)
            {
                return "Ready";
            }
            return _result.Status.ToString();
        }
    }

    public void UpdateAfterRename(string newFilename)
    {
        _result.OriginalFilename = newFilename;
        _result.SuggestedFilename = Path.GetFileNameWithoutExtension(newFilename);
        OnPropertyChanged(nameof(OriginalFilename));
        OnPropertyChanged(nameof(SuggestedFilename));
    }

    public bool IsApproved
    {
        get => _result.IsApproved;
        set
        {
            _result.IsApproved = value;
            OnPropertyChanged();
            ApprovalChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ApprovalChanged;

    public ImageAnalysisResult Result => _result;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged([CallerMemberName] string? name = null)
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

    public string ModelName => _config.Model;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize services
        _config = new LmStudioConfiguration();
        _visionService = new LmStudioVisionService(_config);
        _analyzerService = new ImageAnalyzerService(_visionService, ownsVisionService: true);
        _folderScanner = new FolderScannerService();
        _renamePlanner = new RenamePlannerService();
        _undoManager = new UndoManagerService();

        // Set DataContext for binding
        DataContext = this;

        // Bind results list
        ResultsListView.ItemsSource = _results;

        // Check connection on startup
        Loaded += MainWindow_Loaded;

        // Dispose services on window close
        Closed += (s, e) =>
        {
            _analyzerService?.Dispose();
            _cancellationTokenSource?.Dispose();
        };
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

    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
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

            // Scan and show files immediately
            await ShowFilesInFolderAsync(_selectedPath);
        }
    }

    private async Task ShowFilesInFolderAsync(string folderPath)
    {
        try
        {
            _results.Clear();

            // Scan directory for image files
            var files = await _folderScanner.ScanDirectoryAsync(folderPath, recursive: false);

            if (files.Count == 0)
            {
                StatusText.Text = "No image files found in selected folder";
                EmptyState.Visibility = Visibility.Visible;
                ResultsState.Visibility = Visibility.Collapsed;
                AnalyzeButton.IsEnabled = false;
                return;
            }

            // Show files in results list as "pending"
            foreach (var filePath in files)
            {
                var fileInfo = new FileInfo(filePath);
                var pendingResult = new ImageAnalysisResult
                {
                    OriginalPath = filePath,
                    OriginalFilename = fileInfo.Name,
                    Extension = fileInfo.Extension.ToLowerInvariant(),
                    FileSizeBytes = fileInfo.Length,
                    Status = AnalysisStatus.Pending,
                    SuggestedFilename = "" // Empty until analyzed
                };

                var vm = new ResultViewModel(pendingResult);
                vm.ApprovalChanged += ResultViewModel_ApprovalChanged;
                _results.Add(vm);
            }

            // Show results state
            EmptyState.Visibility = Visibility.Collapsed;
            ResultsState.Visibility = Visibility.Visible;
            ResultsSummary.Text = $"{files.Count} images found - ready to analyze";
            StatusText.Text = $"Found {files.Count} images. Click 'Analyze' to generate metadata.";
            AnalyzeButton.IsEnabled = true;
            ApplyButton.IsEnabled = false; // Disable until analysis is done
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error scanning folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Error scanning folder";
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

            // Show the single file in the list
            _results.Clear();

            var fileInfo = new FileInfo(_selectedPath);
            var pendingResult = new ImageAnalysisResult
            {
                OriginalPath = _selectedPath,
                OriginalFilename = fileInfo.Name,
                Extension = fileInfo.Extension.ToLowerInvariant(),
                FileSizeBytes = fileInfo.Length,
                Status = AnalysisStatus.Pending,
                SuggestedFilename = "" // Empty until analyzed
            };

            var vm = new ResultViewModel(pendingResult);
            vm.ApprovalChanged += ResultViewModel_ApprovalChanged;
            _results.Add(vm);

            // Show results state
            EmptyState.Visibility = Visibility.Collapsed;
            ResultsState.Visibility = Visibility.Visible;
            ResultsSummary.Text = "1 image selected - ready to analyze";
            StatusText.Text = $"Selected file: {Path.GetFileName(_selectedPath)}. Click 'Analyze' to generate metadata.";
            AnalyzeButton.IsEnabled = true;
            ApplyButton.IsEnabled = false; // Disable until analysis is done
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

        // If results are already populated (from folder scan), keep them; otherwise clear
        bool hasExistingResults = _results.Count > 0;

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

            // Update existing results or populate new ones
            if (hasExistingResults)
            {
                // Update existing results
                var resultsByPath = results.ToDictionary(r => r.OriginalPath, r => r);
                foreach (var vm in _results)
                {
                    if (resultsByPath.TryGetValue(vm.Result.OriginalPath, out var newResult))
                    {
                        // Copy all the analyzed data into the existing result
                        vm.Result.SuggestedFilename = newResult.SuggestedFilename;
                        vm.Result.Tags = newResult.Tags;
                        vm.Result.Description = newResult.Description;
                        vm.Result.Title = newResult.Title;
                        vm.Result.Subject = newResult.Subject;
                        vm.Result.Comments = newResult.Comments;
                        vm.Result.Authors = newResult.Authors;
                        vm.Result.Copyright = newResult.Copyright;
                        vm.Result.VisibleDate = newResult.VisibleDate;
                        vm.Result.Status = newResult.Status;
                        vm.Result.ErrorMessage = newResult.ErrorMessage;
                        vm.Result.FileHash = newResult.FileHash;
                        vm.Result.AnalyzedAt = newResult.AnalyzedAt;
                        vm.OnPropertyChanged(nameof(vm.SuggestedFilename));
                        vm.OnPropertyChanged(nameof(vm.Title));
                        vm.OnPropertyChanged(nameof(vm.Subject));
                        vm.OnPropertyChanged(nameof(vm.TagsDisplay));
                        vm.OnPropertyChanged(nameof(vm.StatusDisplay));
                    }
                }
            }
            else
            {
                // Populate results from scratch
                foreach (var result in results)
                {
                    var vm = new ResultViewModel(result);
                    vm.ApprovalChanged += ResultViewModel_ApprovalChanged;
                    _results.Add(vm);
                }
            }

            // Update summary
            var successCount = results.Count(r => r.Status == AnalysisStatus.Success);
            var failedCount = results.Count(r => r.Status == AnalysisStatus.Failed);
            ResultsSummary.Text = $"{successCount} images analyzed" + (failedCount > 0 ? $", {failedCount} failed" : "");

            // Switch to results state
            ProgressState.Visibility = Visibility.Collapsed;
            ResultsState.Visibility = Visibility.Visible;

            // Apply button will be enabled when user checks items (via ResultViewModel_ApprovalChanged)

            // Show error details if any failed
            if (failedCount > 0)
            {
                var firstError = results.FirstOrDefault(r => r.Status == AnalysisStatus.Failed)?.ErrorMessage ?? "";
                // Truncate long error messages
                if (firstError.Length > Constants.MaxErrorMessageStatusLength)
                {
                    firstError = firstError.Substring(0, Constants.MaxErrorMessageStatusLength) + "...";
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
            ApplyButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Analysis failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Analysis failed";
            ProgressState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            ApplyButton.IsEnabled = false;
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
            MessageBox.Show("No images selected for processing.", "Nothing to Apply", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Count how many will be renamed vs just metadata updates
        var operations = _renamePlanner.PlanRenames(approvedResults);
        var metadataOnlyCount = approvedResults.Count - operations.Count;

        var message = operations.Count > 0
            ? $"This will:\n- Rename {operations.Count} file(s)\n- Update metadata for {approvedResults.Count} file(s)\n\nContinue?"
            : $"This will update metadata for {approvedResults.Count} file(s) (no renames).\n\nContinue?";

        var confirm = MessageBox.Show(
            message,
            "Confirm Changes",
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
            var metadataWriter = new MetadataWriterService();
            int metadataSuccessCount = 0;
            int metadataFailCount = 0;

            // First, write metadata to ALL approved files (including those not being renamed)
            foreach (var result in approvedResults)
            {
                try
                {
                    StatusText.Text = $"Writing metadata to {Path.GetFileName(result.OriginalPath)}...";
                    var success = await metadataWriter.WriteMetadataAsync(result, result.OriginalPath, CancellationToken.None);

                    if (success)
                    {
                        metadataSuccessCount++;
                    }
                    else
                    {
                        metadataFailCount++;
                    }
                }
                catch (Exception ex)
                {
                    metadataFailCount++;
                    // Log but continue with other files
                    System.Diagnostics.Debug.WriteLine($"Metadata write failed for {result.OriginalPath}: {ex.Message}");
                }
            }

            // Then, rename the files that need it
            RenameSession? session = null;
            if (operations.Count > 0)
            {
                StatusText.Text = "Renaming files...";

                // Build lookup for metadata writing during rename
                var resultsByPath = approvedResults.ToDictionary(r => r.OriginalPath, r => r);
                session = await _renamePlanner.ExecuteRenamesAsync(operations, resultsByPath, writeMetadata: false); // Metadata already written above

                await _undoManager.SaveSessionAsync(session);
                _lastSession = session;

                UndoButton.IsEnabled = true;

                // Update the displayed filenames to show the new names
                var operationsByOriginalPath = session.Operations
                    .Where(o => o.WasSuccessful)
                    .ToDictionary(o => o.OriginalPath, o => o);

                foreach (var vm in _results)
                {
                    if (operationsByOriginalPath.TryGetValue(vm.Result.OriginalPath, out var operation))
                    {
                        // Update to show the new filename
                        var newFilename = Path.GetFileName(operation.NewPath);
                        vm.UpdateAfterRename(newFilename);

                        // Update the OriginalPath so undo will work
                        vm.Result.OriginalPath = operation.NewPath;

                        // Mark as no longer approved since it's been renamed
                        vm.IsApproved = false;
                    }
                }

                ResultsSummary.Text = $"{session.SuccessCount} files renamed" +
                                      (session.FailedCount > 0 ? $", {session.FailedCount} failed" : "") +
                                      $" | {metadataSuccessCount} metadata updated" +
                                      (metadataFailCount > 0 ? $" ({metadataFailCount} failed)" : "");

                StatusText.Text = $"Renamed {session.SuccessCount} files, updated {metadataSuccessCount} metadata" +
                                  (session.FailedCount > 0 ? $" ({session.FailedCount} rename failed)" : "") +
                                  (metadataFailCount > 0 ? $" ({metadataFailCount} metadata failed)" : "");
            }
            else
            {
                // Metadata-only update, no renames
                ResultsSummary.Text = $"{metadataSuccessCount} metadata updated" +
                                      (metadataFailCount > 0 ? $" ({metadataFailCount} failed)" : "");

                StatusText.Text = $"Updated metadata for {metadataSuccessCount} files" +
                                  (metadataFailCount > 0 ? $" ({metadataFailCount} failed)" : "");

                // Uncheck all since metadata has been written
                foreach (var vm in _results)
                {
                    if (vm.IsApproved)
                    {
                        vm.IsApproved = false;
                    }
                }
            }

            // Keep the results visible to show the updated names
            ResultsState.Visibility = Visibility.Visible;

            // Disable Apply button since all items are now unchecked
            ApplyButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ApplyButton.IsEnabled = true; // Re-enable on error so user can retry
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

    private void ResultViewModel_ApprovalChanged(object? sender, EventArgs e)
    {
        // Enable Apply button if at least one item is approved
        ApplyButton.IsEnabled = _results.Any(r => r.IsApproved && r.Result.Status == AnalysisStatus.Success);
    }
}
