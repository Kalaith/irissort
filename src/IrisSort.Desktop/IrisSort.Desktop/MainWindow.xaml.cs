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
    private bool _autoApplyEnabled;
    private readonly ObservableCollection<AppliedChangeRecord> _appliedChanges = new();
    private Guid _currentSessionId;

    public MainWindow()
    {
        InitializeComponent();

        // Load persisted configuration
        var savedConfig = ConfigurationService.LoadConfiguration();
        _config = new LmStudioConfiguration();
        ConfigurationService.ApplyToLmStudioConfiguration(savedConfig, _config);

        // Initialize services
        _visionService = new LmStudioVisionService(_config);
        _analyzerService = new ImageAnalyzerService(_visionService, ownsVisionService: true);
        _folderScanner = new FolderScannerService();
        _renamePlanner = new RenamePlannerService();
        _undoManager = new UndoManagerService();

        // Set DataContext for binding
        DataContext = this;

        // Bind results list
        ResultsListView.ItemsSource = _results;
        HistoryListView.ItemsSource = _appliedChanges;

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
        MaxDimTextBox.Text = _config.MaxImageDimension.ToString();
        await CheckConnectionAsync();
    }

    private void MaxDimTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Remove focus to trigger LostFocus logic
            Keyboard.ClearFocus();
        }
    }

    private void MaxDimTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MaxDimTextBox.Text, out int newDim))
        {
            if (newDim < 100) newDim = 100; // Minimum safety
            if (newDim > 10000) newDim = 10000; // Maximum safety

            if (_config.MaxImageDimension != newDim)
            {
                _config.MaxImageDimension = newDim;
                
                // Save configuration
                try
                {
                    var configToSave = ConfigurationService.CreateFromLmStudioConfiguration(_config);
                    ConfigurationService.SaveConfiguration(configToSave);
                    StatusText.Text = $"Max dimension updated to {newDim}px";
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Failed to save settings: {ex.Message}";
                }
            }
            // Update text box to match sanitized value
            MaxDimTextBox.Text = newDim.ToString();
        }
        else
        {
            // Invalid input, revert
            MaxDimTextBox.Text = _config.MaxImageDimension.ToString();
        }
    }

    private async Task CheckConnectionAsync()
    {
        ConnectionStatus.Text = "LM Studio: Checking...";
        ConnectionIndicator.Fill = new SolidColorBrush(Colors.Orange);
        ModelComboBox.IsEnabled = false;

        var isAvailable = await _visionService.IsAvailableAsync();

        if (isAvailable)
        {
            ConnectionStatus.Text = "LM Studio: Connected";
            ConnectionIndicator.Fill = (SolidColorBrush)FindResource("SuccessBrush");
            StatusText.Text = "Ready - LM Studio connected";

            // Load available models
            await LoadModelsAsync();
        }
        else
        {
            ConnectionStatus.Text = "LM Studio: Not Connected";
            ConnectionIndicator.Fill = (SolidColorBrush)FindResource("ErrorBrush");
            StatusText.Text = "Warning: Start LM Studio and load a vision model";
            ModelComboBox.Items.Clear();
            ModelComboBox.IsEnabled = false;
        }
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            var models = await _visionService.GetAvailableModelsAsync();

            ModelComboBox.Items.Clear();

            if (models.Length == 0)
            {
                ModelComboBox.Items.Add("(No models loaded)");
                ModelComboBox.SelectedIndex = 0;
                ModelComboBox.IsEnabled = false;
                StatusText.Text = "Warning: No models loaded in LM Studio";
                return;
            }

            foreach (var model in models)
            {
                ModelComboBox.Items.Add(model);
            }

            // Select current model if it's in the list, otherwise select first
            var currentModel = _config.Model;
            var matchIndex = -1;

            for (int i = 0; i < ModelComboBox.Items.Count; i++)
            {
                if (ModelComboBox.Items[i].ToString() == currentModel)
                {
                    matchIndex = i;
                    break;
                }
            }

            if (matchIndex >= 0)
            {
                ModelComboBox.SelectedIndex = matchIndex;
            }
            else if (ModelComboBox.Items.Count > 0)
            {
                ModelComboBox.SelectedIndex = 0;
            }

            ModelComboBox.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ModelComboBox.Items.Clear();
            ModelComboBox.Items.Add("(Error loading models)");
            ModelComboBox.SelectedIndex = 0;
            ModelComboBox.IsEnabled = false;
            StatusText.Text = $"Error loading models: {ex.Message}";
        }
    }

    private void ModelComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ModelComboBox.SelectedItem == null)
            return;

        var selectedModel = ModelComboBox.SelectedItem.ToString();

        if (string.IsNullOrEmpty(selectedModel) || selectedModel.StartsWith("("))
            return; // Ignore placeholder items

        // Update configuration
        _config.Model = selectedModel;

        // Save configuration to disk
        try
        {
            var configToSave = ConfigurationService.CreateFromLmStudioConfiguration(_config);
            ConfigurationService.SaveConfiguration(configToSave);
            StatusText.Text = $"Model changed to: {selectedModel}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Model changed but failed to save: {ex.Message}";
        }
    }

    private async void RefreshModelsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshModelsButton.IsEnabled = false;
        StatusText.Text = "Refreshing model list...";

        try
        {
            await LoadModelsAsync();
            StatusText.Text = "Model list refreshed";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error refreshing models: {ex.Message}";
        }
        finally
        {
            RefreshModelsButton.IsEnabled = true;
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
            Filter = "Image files (*.jpg;*.jpeg;*.png;*.webp;*.gif)|*.jpg;*.jpeg;*.png;*.webp;*.gif|All files (*.*)|*.*"
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

        // Initialize for auto-apply if enabled
        if (_autoApplyEnabled)
        {
            _currentSessionId = Guid.NewGuid();
            _appliedChanges.Clear();
            _lastSession = null;
        }

        _cancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<(int current, int total, string fileName)>(p =>
        {
            AnalysisProgress.Maximum = p.total;
            AnalysisProgress.Value = p.current;
            ProgressText.Text = $"Analyzing {p.current} of {p.total}...";
            ProgressDetail.Text = p.fileName;
        });

        List<ImageAnalysisResult>? results = null;
        bool wasCancelled = false;

        try
        {
            try
            {
                if (_isSingleFile)
                {
                    ProgressText.Text = "Analyzing image...";
                    var result = await _analyzerService.AnalyzeImageAsync(_selectedPath, _cancellationTokenSource.Token);
                    results = new List<ImageAnalysisResult> { result };

                    // Auto-apply for single file if enabled
                    if (_autoApplyEnabled && result.Status == AnalysisStatus.Success)
                    {
                        await ApplySingleResultAsync(result);
                    }
                }
                else
                {
                    // Setup auto-apply callback if enabled
                    Func<ImageAnalysisResult, Task>? callback = null;
                    if (_autoApplyEnabled)
                    {
                        callback = ApplySingleResultAsync;
                    }

                    results = await _analyzerService.AnalyzeDirectoryAsync(
                        _selectedPath,
                        recursive: false,
                        progress,
                        callback,
                        _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
                // results may be null or partial - we'll handle it below
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Analysis failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Analysis failed";
                ProgressState.Visibility = Visibility.Collapsed;

                // Show results if we have any, otherwise show empty state
                if (_results.Count > 0)
                {
                    ResultsState.Visibility = Visibility.Visible;
                }
                else
                {
                    EmptyState.Visibility = Visibility.Visible;
                }

                return;
            }

        // Process results if we have any (even partial results from cancellation)
        if (results != null && results.Count > 0)
        {
            // Check if auto-apply is enabled
            // If auto-apply was enabled, changes were already applied in real-time via callback
            // So we just show the history summary instead of applying again
            if (_autoApplyEnabled && !wasCancelled && _appliedChanges.Count > 0)
            {
                // Show history view with real-time applied changes
                ShowAutoApplyHistory();
                return;
            }
            else if (_autoApplyEnabled && !wasCancelled)
            {
                // Fallback: bulk auto-apply if callback didn't work for some reason
                await AutoApplyChangesAsync(results);
                return;
            }

            // Update existing results or populate new ones
            if (hasExistingResults)
            {
                // Update existing results
                var resultsByPath = results
                    .GroupBy(r => r.OriginalPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

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
            var pendingCount = _results.Count(r => r.Result.Status == AnalysisStatus.Pending);

            if (wasCancelled)
            {
                ResultsSummary.Text = $"Analysis cancelled: {successCount} completed" +
                                      (failedCount > 0 ? $", {failedCount} failed" : "") +
                                      (pendingCount > 0 ? $", {pendingCount} not analyzed" : "");
            }
            else
            {
                ResultsSummary.Text = $"{successCount} images analyzed" +
                                      (failedCount > 0 ? $", {failedCount} failed" : "");
            }

            // Switch to results state
            ProgressState.Visibility = Visibility.Collapsed;
            ResultsState.Visibility = Visibility.Visible;

            // Apply button will be enabled when user checks items (via ResultViewModel_ApprovalChanged)

            // Show error details if any failed
            if (wasCancelled)
            {
                StatusText.Text = $"Analysis cancelled: {successCount} images completed successfully" +
                                  (pendingCount > 0 ? $", {pendingCount} not analyzed" : "");
            }
            else if (failedCount > 0)
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
        else if (wasCancelled)
        {
            // Cancelled with no results
            StatusText.Text = "Analysis cancelled - no images were analyzed";
            ProgressState.Visibility = Visibility.Collapsed;

            // Show results if we already had some, otherwise show empty state
            if (_results.Count > 0)
            {
                ResultsState.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyState.Visibility = Visibility.Visible;
            }
        }
        else
        {
            // No results and not cancelled - shouldn't happen but handle gracefully
            StatusText.Text = "No results returned";
            ProgressState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }
        }
        catch (Exception ex)
        {
            // Catch any unexpected exceptions during result processing
            MessageBox.Show($"Error processing results: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = $"Error: {ex.Message}";
            ProgressState.Visibility = Visibility.Collapsed;

            // Show results if we have any, otherwise show empty state
            if (_results.Count > 0)
            {
                ResultsState.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyState.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            // Cleanup: always re-enable buttons and dispose cancellation token
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
                var resultsByPath = approvedResults
                    .GroupBy(r => r.OriginalPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
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

    private async Task ApplySingleResultAsync(ImageAnalysisResult result)
    {
        // This is called immediately after each file is analyzed
        // Apply changes (metadata + rename) in real-time

        if (_currentSessionId == Guid.Empty)
        {
            _currentSessionId = Guid.NewGuid();
        }

        try
        {
            // Mark as approved (required for PlanRenames)
            result.IsApproved = true;

            // Write metadata first
            var metadataWriter = new MetadataWriterService();
            var metadataSuccess = await metadataWriter.WriteMetadataAsync(result, result.OriginalPath, CancellationToken.None);

            // Plan and execute rename if needed
            var operations = _renamePlanner.PlanRenames(new[] { result });

            if (operations.Count > 0)
            {
                var operation = operations[0];
                var resultsByPath = new Dictionary<string, ImageAnalysisResult>(StringComparer.OrdinalIgnoreCase)
                {
                    { result.OriginalPath, result }
                };

                var session = await _renamePlanner.ExecuteRenamesAsync(operations, resultsByPath, writeMetadata: false);

                if (session.SuccessCount > 0)
                {
                    var op = session.Operations[0];
                    _appliedChanges.Add(new AppliedChangeRecord
                    {
                        OriginalFilename = Path.GetFileName(op.OriginalPath),
                        NewFilename = Path.GetFileName(op.NewPath),
                        OriginalPath = op.OriginalPath,
                        NewPath = op.NewPath,
                        WasRenamed = true,
                        MetadataWritten = metadataSuccess,
                        AppliedAt = DateTime.Now,
                        SessionId = _currentSessionId
                    });

                    // Update the session for undo
                    if (_lastSession == null)
                    {
                        _lastSession = session;
                        await _undoManager.SaveSessionAsync(session);
                    }
                    else
                    {
                        _lastSession.Operations.AddRange(session.Operations);
                        await _undoManager.SaveSessionAsync(_lastSession);
                    }
                }
            }
            else
            {
                // No rename needed, just metadata
                _appliedChanges.Add(new AppliedChangeRecord
                {
                    OriginalFilename = Path.GetFileName(result.OriginalPath),
                    NewFilename = Path.GetFileName(result.OriginalPath),
                    OriginalPath = result.OriginalPath,
                    NewPath = result.OriginalPath,
                    WasRenamed = false,
                    MetadataWritten = metadataSuccess,
                    AppliedAt = DateTime.Now,
                    SessionId = _currentSessionId
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Real-time auto-apply failed for {result.OriginalFilename}: {ex.Message}");
        }
    }

    private void ShowAutoApplyHistory()
    {
        // Show history view after real-time auto-apply
        EmptyState.Visibility = Visibility.Collapsed;
        ProgressState.Visibility = Visibility.Collapsed;
        ResultsState.Visibility = Visibility.Collapsed;
        HistoryState.Visibility = Visibility.Visible;

        var renamedCount = _appliedChanges.Count(c => c.WasRenamed);
        var metadataOnlyCount = _appliedChanges.Count(c => !c.WasRenamed);

        HistorySummary.Text = $"{_appliedChanges.Count} changes applied: {renamedCount} renamed, {_appliedChanges.Count} metadata written";
        StatusText.Text = $"Auto-applied: {renamedCount} renamed, {metadataOnlyCount} metadata-only";
        UndoButton.IsEnabled = renamedCount > 0;

        // Show summary message
        var summaryMessage = $"Auto-apply completed:\n\n" +
                           $"✓ {renamedCount} file(s) renamed\n" +
                           $"✓ {_appliedChanges.Count} file(s) metadata written\n";

        if (metadataOnlyCount > 0)
        {
            summaryMessage += $"\n{metadataOnlyCount} file(s) kept their original names (already correct or no change needed)";
        }

        summaryMessage += $"\n\nChanges were applied in real-time as each file was analyzed.\n\nYou can select and undo specific changes if needed.";

        MessageBox.Show(summaryMessage, "Auto-Apply Complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AutoApplyCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Auto-apply mode will automatically rename files and write metadata as soon as the LLM returns results, WITHOUT asking for confirmation.\n\n" +
            "⚠️ This will immediately change your files!\n\n" +
            "You will be able to review and undo changes afterwards.\n\n" +
            "Are you sure you want to enable auto-apply mode?",
            "Enable Auto-Apply Mode",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            AutoApplyCheckBox.IsChecked = false;
            return;
        }

        _autoApplyEnabled = true;
        StatusText.Text = "Auto-apply mode enabled - changes will be applied automatically";
    }

    private void AutoApplyCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        _autoApplyEnabled = false;
        StatusText.Text = "Auto-apply mode disabled";
    }

    private async Task AutoApplyChangesAsync(List<ImageAnalysisResult> results)
    {
        _currentSessionId = Guid.NewGuid();
        _appliedChanges.Clear();

        var successfulResults = results.Where(r => r.Status == AnalysisStatus.Success).ToList();
        if (successfulResults.Count == 0)
        {
            StatusText.Text = "Auto-apply: No successful results to apply";
            return;
        }

        // CRITICAL FIX: Auto-approve all successful results
        // PlanRenames only processes approved results, but in auto-apply mode
        // we want to apply ALL successful results automatically
        foreach (var result in successfulResults)
        {
            result.IsApproved = true;
        }

        StatusText.Text = "Auto-applying changes...";

        try
        {
            var metadataWriter = new MetadataWriterService();
            int metadataSuccessCount = 0;
            int metadataFailCount = 0;

            // Write metadata to ALL successful results
            foreach (var result in successfulResults)
            {
                try
                {
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
                    System.Diagnostics.Debug.WriteLine($"Metadata write failed for {result.OriginalPath}: {ex.Message}");
                }
            }

            // Rename files that need it
            var operations = _renamePlanner.PlanRenames(successfulResults);

            if (operations.Count > 0)
            {
                var resultsByPath = successfulResults
                    .GroupBy(r => r.OriginalPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                StatusText.Text = $"Renaming {operations.Count} file(s)...";
                var session = await _renamePlanner.ExecuteRenamesAsync(operations, resultsByPath, writeMetadata: false);
                await _undoManager.SaveSessionAsync(session);
                _lastSession = session;

                // Record changes for history view
                foreach (var op in session.Operations.Where(o => o.WasSuccessful))
                {
                    _appliedChanges.Add(new AppliedChangeRecord
                    {
                        OriginalFilename = Path.GetFileName(op.OriginalPath),
                        NewFilename = Path.GetFileName(op.NewPath),
                        OriginalPath = op.OriginalPath,
                        NewPath = op.NewPath,
                        WasRenamed = true,
                        MetadataWritten = true,
                        AppliedAt = DateTime.Now,
                        SessionId = _currentSessionId
                    });
                }
            }

            // Record metadata-only changes (files that didn't need renaming)
            var renamedPaths = operations.Select(o => o.OriginalPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var result in successfulResults.Where(r => !renamedPaths.Contains(r.OriginalPath)))
            {
                _appliedChanges.Add(new AppliedChangeRecord
                {
                    OriginalFilename = Path.GetFileName(result.OriginalPath),
                    NewFilename = Path.GetFileName(result.OriginalPath),
                    OriginalPath = result.OriginalPath,
                    NewPath = result.OriginalPath,
                    WasRenamed = false,
                    MetadataWritten = true,
                    AppliedAt = DateTime.Now,
                    SessionId = _currentSessionId
                });
            }

            // Show history view
            EmptyState.Visibility = Visibility.Collapsed;
            ProgressState.Visibility = Visibility.Collapsed;
            ResultsState.Visibility = Visibility.Collapsed;
            HistoryState.Visibility = Visibility.Visible;

            var renamedCount = _appliedChanges.Count(c => c.WasRenamed);
            var metadataOnlyCount = _appliedChanges.Count(c => !c.WasRenamed);

            HistorySummary.Text = $"{_appliedChanges.Count} changes applied: {renamedCount} renamed, {metadataSuccessCount} metadata written";
            StatusText.Text = $"Auto-applied: {renamedCount} renamed, {metadataOnlyCount} metadata-only" +
                             (metadataFailCount > 0 ? $" ({metadataFailCount} metadata failed)" : "");
            UndoButton.IsEnabled = renamedCount > 0;

            // Show summary message
            var summaryMessage = $"Auto-apply completed:\n\n" +
                               $"✓ {renamedCount} file(s) renamed\n" +
                               $"✓ {metadataSuccessCount} file(s) metadata written\n";

            if (metadataOnlyCount > 0)
            {
                summaryMessage += $"\n{metadataOnlyCount} file(s) kept their original names (already correct or no change needed)";
            }

            if (metadataFailCount > 0)
            {
                summaryMessage += $"\n\n⚠ {metadataFailCount} file(s) had metadata write errors";
            }

            summaryMessage += $"\n\nYou can select and undo specific changes if needed.";

            MessageBox.Show(summaryMessage, "Auto-Apply Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Auto-apply failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = $"Auto-apply error: {ex.Message}";
        }
    }

    private void SelectAllHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var change in _appliedChanges)
        {
            change.IsSelectedForUndo = true;
        }
        UpdateUndoSelectionText();
        HistoryListView.Items.Refresh();
    }

    private void DeselectAllHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var change in _appliedChanges)
        {
            change.IsSelectedForUndo = false;
        }
        UpdateUndoSelectionText();
        HistoryListView.Items.Refresh();
    }

    private void CloseHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryState.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        _results.Clear();
        _selectedPath = null;
        AnalyzeButton.IsEnabled = false;
        StatusText.Text = "Ready";
    }

    private void HistoryItem_CheckChanged(object sender, RoutedEventArgs e)
    {
        UpdateUndoSelectionText();
    }

    private void UpdateUndoSelectionText()
    {
        var selectedCount = _appliedChanges.Count(c => c.IsSelectedForUndo);
        UndoSelectionText.Text = $"{selectedCount} item(s) selected";
        UndoSelectedButton.IsEnabled = selectedCount > 0;
    }

    private void UndoSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedChanges = _appliedChanges.Where(c => c.IsSelectedForUndo && c.WasRenamed).ToList();

        if (selectedChanges.Count == 0)
        {
            MessageBox.Show("No renamed files selected for undo.\n\nNote: Only file renames can be undone. Metadata changes are permanent.",
                "Nothing to Undo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"This will undo {selectedChanges.Count} file rename(s).\n\nNote: Metadata changes cannot be undone.\n\nContinue?",
            "Confirm Undo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        UndoSelectedButton.IsEnabled = false;
        StatusText.Text = "Undoing selected changes...";

        try
        {
            int revertedCount = 0;

            foreach (var change in selectedChanges)
            {
                try
                {
                    if (File.Exists(change.NewPath))
                    {
                        File.Move(change.NewPath, change.OriginalPath);
                        revertedCount++;
                        _appliedChanges.Remove(change);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to undo {change.NewPath}: {ex.Message}");
                }
            }

            HistoryListView.Items.Refresh();
            UpdateUndoSelectionText();

            HistorySummary.Text = $"{_appliedChanges.Count} changes remaining";
            StatusText.Text = $"Reverted {revertedCount} file(s)";

            if (_appliedChanges.Count == 0)
            {
                CloseHistoryButton_Click(this, new RoutedEventArgs());
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to undo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            UndoSelectedButton.IsEnabled = true;
        }
    }
}
