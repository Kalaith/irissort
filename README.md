# IrisSort

**AI-powered image renaming and tagging tool**

IrisSort is a Windows desktop application that scans folders of images, analyzes each using AI vision models, and suggests intelligent filenames and metadata tags based on image contents. The tool prioritizes user control and safety - all changes require explicit approval before any files are modified.

## Features

- 🖼️ **AI-Powered Analysis**: Uses vision-capable AI models to understand image contents
- 📁 **Flexible Processing**: Process single images or entire directories
- 🗂️ **Recursive Scanning**: Include supported images in subfolders when needed
- ⏭️ **Skip Existing Tags**: Avoid re-sending files that already have keyword metadata
- 📝 **Smart Filenames**: Generates descriptive, filesystem-safe names
- 🏷️ **Automatic Tagging**: Suggests relevant metadata tags
- ✅ **User Approval**: Preview all changes before applying
- 🔄 **Batch Operations**: Accept/reject multiple suggestions at once
- ↩️ **Undo Support**: Revert operations if needed
- 🔒 **Local Processing**: No cloud uploads required
- 📴 **Offline-Friendly Review**: Scan and review folders while LM Studio is unavailable
- 🛡️ **Safe Operations**: Collision detection, dry-run mode, and confirmation dialogs

## Requirements

- Windows 10 or later
- .NET 8 SDK
- [LM Studio](https://lmstudio.ai/) with a vision-capable model loaded (only required for AI analysis)

## Quick Start

1. **Optional: install LM Studio** and download a vision-capable model (e.g., `zai-org/glm-4.6v-flash`) for AI analysis. IrisSort can still scan and preview files while LM Studio is offline.

2. **Start Local Server** in LM Studio:
   - Load a vision model
   - Click the "Local Server" tab
   - Start the server (default: http://127.0.0.1:1234)

3. **Run IrisSort**:
   ```batch
   run-irissort.bat
   ```

4. **Choose Processing Mode**:
   - **Single Image**: Select individual files for immediate processing
   - **Directory**: Scan entire folders with batch processing
   - Enable **Include subfolders** or **Skip tagged** in the action bar for larger libraries

5. **Review AI Suggestions**: Examine proposed filenames and tags

6. **Apply Changes**: Accept suggestions to rename files and add metadata

## Project Structure

```
IrisSort/
├── IrisSort.sln              # Visual Studio solution
├── run-irissort.bat          # Quick launch script
├── scope.md                  # Project scope and requirements
├── implementation_plan.md    # MVP implementation plan
├── src/
│   ├── IrisSort.Core/        # Domain models and core logic
│   ├── IrisSort.Services/    # Business logic & AI integration
│   └── IrisSort.Desktop/     # WPF UI application
```

## Supported Image Types

- JPEG (.jpg, .jpeg)
- PNG (.png)
- WebP (.webp)
- GIF (.gif, first frame analyzed)

## Configuration

LM Studio settings in `LmStudioConfiguration.cs`:
- **BaseUrl**: `http://127.0.0.1:1234/v1` (default)
- **Model**: `zai-org/glm-4.6v-flash` (configurable)
- **Timeout**: 120 seconds (vision models need time)

Processing options are saved in `%APPDATA%\IrisSort\config.json`:
- **Include subfolders**: Recursively scan supported images
- **Skip tagged**: Skip files with existing keyword metadata

## Development

### Prerequisites
- .NET 8 SDK
- Visual Studio 2022 or VS Code with C# extensions

### Building
```batch
dotnet build
```

### Running
```batch
dotnet run --project src/IrisSort.Desktop/IrisSort.Desktop
```

### Testing
```batch
dotnet test
```

## Safety Features

- **Preview Required**: All changes must be reviewed before application
- **Dry-Run Mode**: Test operations without making actual changes
- **Collision Detection**: Automatic handling of filename conflicts
- **Undo Support**: Session-based operation reversal
- **No Overwrites**: Existing files are never overwritten without confirmation

## Contributing

Contributions are welcome! Please see the implementation plan in `implementation_plan.md` for current development priorities.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

### Commercial Use

For commercial licenses or enterprise deployment inquiries, please contact the project owner: Kalaith

## Roadmap

- [ ] CLI version for automation
- [ ] Additional AI model support (Gemini API, local models)
- [ ] Advanced tagging rules and presets
- [ ] Batch processing optimizations
- [ ] Plugin system for custom analysis rules
