# IrisSort

**AI-powered image renaming and tagging using local LLM**

IrisSort scans a folder of images, analyzes each using a local vision-capable LLM (via LM Studio), and suggests intelligent filenames and tags based on image contents.

## Features

- 🖼️ **AI-Powered Analysis**: Uses vision LLM to understand image contents
- 📝 **Smart Filenames**: Generates descriptive, filesystem-safe names
- 🏷️ **Automatic Tagging**: Suggests relevant metadata tags
- ✅ **User Approval**: Preview all changes before applying
- ↩️ **Undo Support**: Revert operations if needed
- 🔒 **Local Processing**: No cloud API keys required

## Requirements

- Windows 10+
- .NET 8 SDK
- [LM Studio](https://lmstudio.ai/) with a vision-capable model loaded

## Quick Start

1. **Install LM Studio** and download a vision-capable model (e.g., `cydonia-22b-v1.3-i1`)

2. **Start Local Server** in LM Studio:
   - Load a vision model
   - Click the "Local Server" tab
   - Start the server (default: http://127.0.0.1:1234)

3. **Run IrisSort**:
   ```batch
   run-irissort.bat
   ```

4. **Select images** and click "Analyze"

5. **Review suggestions** and accept/reject

6. **Apply changes** to rename files

## Project Structure

```
IrisSort/
├── IrisSort.sln              # Visual Studio solution
├── run-irissort.bat          # Quick launch script
├── src/
│   ├── IrisSort.Core/        # Domain models
│   ├── IrisSort.Services/    # Business logic & LM Studio integration
│   └── IrisSort.Desktop/     # WPF UI application
```

## Supported Image Types

- JPEG (.jpg, .jpeg)
- PNG (.png)
- WebP (.webp)

## Configuration

LM Studio settings in `LmStudioConfiguration.cs`:
- **BaseUrl**: `http://127.0.0.1:1234/v1` (default)
- **Model**: `cydonia-22b-v1.3-i1` (configurable)
- **Timeout**: 120 seconds (vision models need time)

## Development

Build the solution:
```batch
dotnet build
```

Run the application:
```batch
dotnet run --project src/IrisSort.Desktop/IrisSort.Desktop
```

## Safety Features

- All renames require explicit approval
- Collision detection with automatic suffix (_1, _2, etc.)
- Session-based undo support
- No files modified without confirmation

## License

MIT License
