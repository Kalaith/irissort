# IrisSort Implementation Plan

## Overview

This implementation plan outlines the development of IrisSort MVP - a local AI-assisted image renaming and tagging tool for Windows, modeled after the Howl project structure.

> [!IMPORTANT]
> All changes require user preview and approval before execution. No files are modified without explicit consent.

---

## MVP Scope Recap

- **Platform**: Windows 10+ desktop application
- **Tech Stack**: C#, .NET 8, WPF
- **AI Service**: LM Studio local server (http://127.0.0.1:1234, model: cydonia-22b-v1.3-i1)
- **Supported Formats**: .jpg, .jpeg, .png, .webp
- **Key Features**:
  - Single image or directory processing
  - AI-powered filename and tag suggestions  
  - User review and approval workflow
  - Safe renaming with undo support
  - Metadata tagging (EXIF/XMP)

---

## Project Structure

Following Howl's modular architecture:

```
IrisSort/
├── IrisSort.sln
├── run-irissort.bat              # Launch script (LM Studio check + build + run)
├── README.md
├── src/
│   ├── IrisSort.Core/
│   │   └── IrisSort.Core/
│   │       ├── IrisSort.Core.csproj
│   │       └── Models/
│   │           ├── ImageAnalysisResult.cs
│   │           ├── RenameOperation.cs
│   │           └── ProcessingOptions.cs
│   ├── IrisSort.Services/
│   │   └── IrisSort.Services/
│   │       ├── IrisSort.Services.csproj
│   │       ├── Configuration/
│   │       │   └── GeminiConfiguration.cs
│   │       ├── Exceptions/
│   │       │   └── GeminiApiException.cs
│   │       ├── Models/
│   │       │   └── GeminiModels.cs
│   │       ├── GeminiVisionService.cs
│   │       ├── FolderScannerService.cs
│   │       ├── ImageAnalyzerService.cs
│   │       ├── RenamePlannerService.cs
│   │       ├── MetadataWriterService.cs
│   │       └── UndoManagerService.cs
│   └── IrisSort.Desktop/
│       └── IrisSort.Desktop/
│           ├── IrisSort.Desktop.csproj
│           ├── App.xaml
│           ├── App.xaml.cs
│           ├── MainWindow.xaml
│           ├── MainWindow.xaml.cs
│           └── AssemblyInfo.cs
└── README.md
```

---

## Implementation Phases

### Phase 1: Project Setup & Core Infrastructure

#### 1.1 Solution Structure

##### [NEW] [IrisSort.sln](file:///h:/claude/irissort/IrisSort.sln)
Create Visual Studio solution with three projects following Howl pattern.

##### [NEW] [IrisSort.Core.csproj](file:///h:/claude/irissort/src/IrisSort.Core/IrisSort.Core/IrisSort.Core.csproj)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

##### [NEW] [IrisSort.Services.csproj](file:///h:/claude/irissort/src/IrisSort.Services/IrisSort.Services/IrisSort.Services.csproj)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MetadataExtractor" Version="2.8.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\IrisSort.Core\IrisSort.Core\IrisSort.Core.csproj" />
  </ItemGroup>
</Project>
```

##### [NEW] [IrisSort.Desktop.csproj](file:///h:/claude/irissort/src/IrisSort.Desktop/IrisSort.Desktop/IrisSort.Desktop.csproj)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\IrisSort.Services\IrisSort.Services\IrisSort.Services.csproj" />
  </ItemGroup>
</Project>
```

---

#### 1.2 Core Models

##### [NEW] [ImageAnalysisResult.cs](file:///h:/claude/irissort/src/IrisSort.Core/IrisSort.Core/Models/ImageAnalysisResult.cs)
Primary model for storing analysis results per image.

##### [NEW] [RenameOperation.cs](file:///h:/claude/irissort/src/IrisSort.Core/IrisSort.Core/Models/RenameOperation.cs)
Tracks rename operations for undo functionality.

##### [NEW] [ProcessingOptions.cs](file:///h:/claude/irissort/src/IrisSort.Core/IrisSort.Core/Models/ProcessingOptions.cs)
User-configurable processing settings.

---

### Phase 2: Gemini Vision API Integration

#### 2.1 Configuration

##### [NEW] [LmStudioConfiguration.cs](file:///h:/claude/irissort/src/IrisSort.Services/IrisSort.Services/Configuration/LmStudioConfiguration.cs)
LM Studio connection configuration (endpoint, model, timeout).

##### [NEW] [LmStudioApiException.cs](file:///h:/claude/irissort/src/IrisSort.Services/IrisSort.Services/Exceptions/LmStudioApiException.cs)
Custom exception for API error handling.

#### 2.2 API Service

##### [NEW] [LmStudioModels.cs](file:///h:/claude/irissort/src/IrisSort.Services/IrisSort.Services/Models/LmStudioModels.cs)
OpenAI-compatible request/response DTOs for LM Studio communication.

##### [NEW] [LmStudioVisionService.cs](file:///h:/claude/irissort/src/IrisSort.Services/IrisSort.Services/LmStudioVisionService.cs)
Core service implementing:
- Image upload with base64 data URL encoding
- OpenAI-compatible vision API format
- Structured JSON prompt for filename/tag generation
- Retry logic with exponential backoff
- No API key required (local server)

---

### Phase 3: File System Services

#### 3.1 Folder Scanning

##### [NEW] [FolderScannerService.cs](file:///h:/claude/irissort/src/IrisSort.Services/IrisSort.Services/FolderScannerService.cs)
- Enumerate files by extension
- Optional recursive scanning
- Skip hidden/system folders
- Calculate file hashes for caching

#### 3.2 Image Analysis Pipeline

##### [NEW] [ImageAnalyzerService.cs](file:///h:/claude/irissort/src/IrisSort.Services/IrisSort.Services/ImageAnalyzerService.cs)
Orchestrates:
- File reading and MIME type detection
- LmStudioVisionService calls
- Result caching by file hash
- Batch processing with progress

---

### Phase 4: Rename & Metadata Services

#### 4.1 Safe Rename Operations

##### [NEW] [RenamePlannerService.cs](file:///h:/claude/irissort/src/IrisSort.Services/IrisSort.Services/RenamePlannerService.cs)
- Collision detection and resolution (_1, _2 suffixes)
- Path validation
- Dry-run mode support
- Transaction-style execution

#### 4.2 Metadata Writing

##### [NEW] [MetadataWriterService.cs](file:///h:/claude/irissort/src/IrisSort.Services/IrisSort.Services/MetadataWriterService.cs)
- Write EXIF keywords (JPEG)
- Write XMP Subject tags (PNG/JPEG)
- Preserve existing metadata

#### 4.3 Undo Support

##### [NEW] [UndoManagerService.cs](file:///h:/claude/irissort/src/IrisSort.Services/IrisSort.Services/UndoManagerService.cs)
- Session-based operation logging
- Revert last run capability
- JSON log persistence

---

### Phase 5: WPF Desktop Application

#### 5.1 Application Shell

##### [NEW] [App.xaml](file:///h:/claude/irissort/src/IrisSort.Desktop/IrisSort.Desktop/App.xaml)
Application resources and startup configuration.

##### [NEW] [App.xaml.cs](file:///h:/claude/irissort/src/IrisSort.Desktop/IrisSort.Desktop/App.xaml.cs)
Service initialization and configuration loading.

##### [NEW] [MainWindow.xaml](file:///h:/claude/irissort/src/IrisSort.Desktop/IrisSort.Desktop/MainWindow.xaml)
Main UI with:
- Settings panel (API key, options)
- File/folder selection
- Processing progress view
- Results review table
- Action buttons (Accept, Reject, Apply)

##### [NEW] [MainWindow.xaml.cs](file:///h:/claude/irissort/src/IrisSort.Desktop/IrisSort.Desktop/MainWindow.xaml.cs)
Event handling and view model integration.

---

## Verification Plan

### Automated Tests
- Unit tests for `LmStudioVisionService` with mocked HTTP responses
- Unit tests for `FolderScannerService` with temp directory fixtures
- Unit tests for `RenamePlannerService` collision handling
- Integration tests for end-to-end workflow

### Manual Verification
- Process single image and verify filename suggestion
- Process directory with 10+ images
- Verify undo functionality restores original names
- Verify metadata tags written correctly
- Test collision resolution with duplicate names

---

## Dependencies & Prerequisites

### NuGet Packages
- `MetadataExtractor` (2.8.1) - EXIF/XMP reading/writing
- `Microsoft.Extensions.Configuration` - Settings management
- `System.Text.Json` - JSON serialization (included in .NET 8)

### External Requirements
- .NET 8 SDK
- Windows 10+
- LM Studio with cydonia-22b-v1.3-i1 model loaded

---

## Timeline & Milestones

| Week | Milestone |
|------|-----------|
| 1-2 | Project structure, core models, Gemini service |
| 3-4 | Folder scanning, image analysis pipeline |
| 5-6 | WPF UI, review interface |
| 7-8 | Rename operations, metadata writing, undo |
| 9-10 | Testing, polish, documentation |

---

## Success Criteria

- ✅ Non-technical user can safely rename 500 images
- ✅ Filenames are consistently useful and descriptive
- ✅ No files lost during any operation
- ✅ UI provides clear progress and error feedback
- ✅ All operations can be undone within session

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| API rate limiting | Implement delays and retry logic |
| Large directories | Progress saving, batch limits |
| Memory usage | Stream processing, dispose patterns |
| File permissions | Comprehensive error handling |
| Processing time | Clear progress indicators, cancellation |
| Data loss | Mandatory preview, undo logging |