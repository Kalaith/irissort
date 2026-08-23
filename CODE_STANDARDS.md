# C# Coding Standards for IrisSort

**Framework**: .NET 9 (WPF)  
**Language**: C# 12  
**Architecture**: Clean Architecture + MVVM  

This document defines the coding standards for the IrisSort project. Our goal is to create a maintainable, robust, and user-friendly desktop application.

## 1. Core Philosophy

### 1.1 Clean Architecture
We follow a strict separation of concerns:
- **Core**: Domain models and interfaces. No dependencies on UI or external implementation details.
- **Services**: Business logic, AI integration, file I/O, and OS interactions. Depends only on Core.
- **Desktop**: The WPF user interface. Depends on proper abstraction layers.

### 1.2 User Experience First
- **Non-blocking UI**: All I/O and heavy processing must be async. The UI thread must never freeze.
- **Magic Moment**: AI features should feel magical but reliable.
- **Error Handling**: Fail gracefully. Users should never see a raw stack trace.

### 1.3 Consistency
- Follow standard C# naming conventions (Microsoft Guidelines).
- Use `var` when type is obvious.
- Prefer pattern matching and modern C# features.

## 2. Project Structure Rules

### 2.1 Solution Layout
The solution is organized into three main projects:

**1. IrisSort.Core** (Class Library)
- **Responsibility**: pure domain logic, data models, and interface definitions.
- **Dependencies**: None (pure C#).
- **Contents**:
  - `Models/`: Immutable data structures (e.g., `ImageMetadata`, `FolderItem`).
  - `Interfaces/`: Service contracts (e.g., `IImageAnalyzerService`).
  - `Enums/`: Shared enumerations.

**2. IrisSort.Services** (Class Library)
- **Responsibility**: Implementation of interfaces, external integrations.
- **Dependencies**: `IrisSort.Core`, External libraries (e.g., image processing libraries).
- **Contents**:
  - `Analysis/`: Image analysis and AI vision integrations.
  - `Processing/`: Image resizing, metadata handling.
  - `Storage/`: File system operations, metadata writing.
  - `Configuration/`: Configuration classes.

**3. IrisSort.Desktop** (WPF Application)
- **Responsibility**: User interaction, visual presentation.
- **Dependencies**: `IrisSort.Core`, `IrisSort.Services`.
- **Contents**:
  - `Views/`: XAML windows and controls.
  - `ViewModels/`: Logic binding View to Model (e.g., `MainViewModel`).
  - `Converters/`: Value converters for XAML bindings.
  - `App.xaml`: Entry point and dependency injection setup.

## 3. Naming Conventions

### 3.1 General Rules
- **PascalCase**: Classes, Methods, Properties, Enums, Namespaces, Public Fields.
- **camelCase**: Local variables, Method arguments.
- **_camelCase**: Private fields (prefix with underscore).
- **ISomeInterface**: Interface names must start with 'I'.

### 3.2 Specific Examples
```csharp
public class ScreenRecorder : IScreenRecorder // Class and Interface
{
    private readonly ILogger _logger; // Private field

    public int FrameCount { get; private set; } // Property

    public async Task StartRecordingAsync(string outputPath) // Method
    {
        var localVariable = 10; // Local variable
        // ...
    }
}
```

### 3.3 Async Methods
- Methods returning `Task` or `Task<T>` should end with the suffix `Async` (e.g., `SaveProjectAsync`).

## 4. Coding Style & Best Practices

### 4.1 Dependency Injection
- Use Constructor Injection for all dependencies.
- Avoid structured static access (Singletons) unless absolutely necessary.
- Register services in `App.xaml.cs` or a dedicated `Bootstrapper`.

### 4.2 Async/Await
- Use `async`/`await` for all I/O bound operations.
- Avoid `.Result` or `.Wait()`. Use `await` all the way up.
- Use `ConfigureAwait(false)` in library code (`IrisSort.Core`, `IrisSort.Services`) to avoid capturing synchronization context.
- **Exception**: In `IrisSort.Desktop` (UI layer), do NOT use `ConfigureAwait(false)` as we need to return to the UI thread.

### 4.3 Data & Models
- Prefer `record` types for immutable data models.
- Use `ObservableCollection<T>` for lists bound to the UI.
- Implement `INotifyPropertyChanged` in ViewModels (use the `CommunityToolkit.Mvvm` base classes if available).

### 4.4 Null Safety
- Enable Nullable Reference Types (`<Nullable>enable</Nullable>`) in all projects.
- Explicitly handle nulls or design them out.
- Use `?` for optional values.

## 5. MVVM Guidelines

### 5.1 No Logic in Code-Behind
- `View.xaml.cs` should only contain `InitializeComponent()` and purely visual logic (e.g., specific animation triggers that can't be done in XAML).
- Application logic belongs in the ViewModel.

### 5.2 Binding
- Use `Command` pattern for buttons and actions (`RelayCommand`).
- Use DataBinding for displaying data.
- Use behaviors or attached properties for complex interactions.

## 6. AI Integration Standards

### 6.1 Prompts
- Store system prompts as `const string` or in resource files.
- Use `PromptBuilder` services to construct dynamic prompts.
- Do not hardcode prompts inside API calling methods.

### 6.2 Reliability
- Always wrap AI calls in Retry policies (exponential backoff).
- Handle JSON parsing errors gracefully (AI can output malformed JSON).
- Validate AI output before using it in the application.
- **Model Output**: Always expect potential `<think>` blocks if using reasoning models (like DeepSeek) and strip them out. Prefer structured JSON output for vision analysis results.

## 7. Documentation

### 7.1 Code Comments
- **Summary**: Public methods and classes should have XML documentation (`/// <summary>`).
- **Why, not What**: Comments should explain intent, not restate the code.

```csharp
/// <summary>
/// Captures a screenshot of the active window.
/// </summary>
/// <returns>Bitmap of the window.</returns>
public async Task<Bitmap> CaptureAsync() { ... }
```

## 8. Git & Version Control
- **Commits**: Use imperative mood ("Add feature", not "Added feature").
- **Branches**: Feature branches (`feature/recorder-fix`) merged into `main`.