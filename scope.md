# IrisSort

Local AI-assisted image renaming & tagging tool

## 1. Purpose & Goals

IrisSort scans a user-selected folder of images, analyzes each image using an LLM vision model (Gemini initially), and suggests improved filenames and metadata tags based on image contents.

The user remains in control:

- Nothing is renamed automatically
- All changes are previewed
- Changes apply only after explicit approval

### Core Goals

- Fast, local, installable Windows app
- Safe, reversible operations
- Useful naming, not poetic nonsense
- Minimal setup friction beyond API key entry

## 2. Target Platform & Stack

### Platform

- Windows 10+
- Desktop application

### Language / Framework

- C#
- .NET 8

### UI

- WPF (recommended for long-term flexibility)
- WinUI 3 also acceptable if you want a more modern shell

### External Services

- Gemini Vision API (image + text prompt)
- API keys stored locally, encrypted

## 3. High-Level Workflow

1. User launches IrisSort
2. User enters API key (first run or settings)
3. User selects a folder
4. IrisSort scans supported image files
5. For each image:
   - Generate analysis via Gemini
   - Produce:
     - Suggested filename
     - Suggested tags
     - Optional short description
6. User reviews results in a list/table
7. User:
   - Accepts per image, bulk accepts, or edits suggestions
8. IrisSort applies changes
9. Summary report shown

## 4. Supported Image Types (Initial)

- .jpg, .jpeg
- .png
- .webp

(HEIC and RAW formats explicitly out of MVP)

## 5. Core Features (MVP)

### 5.1 Folder Scanning

- Recursive: optional (checkbox)
- Ignore hidden/system folders
- Skip files already processed (based on metadata flag or cache)

### 5.2 Image Analysis

Each image is sent to Gemini with:

- The image file
- A structured prompt requesting machine-usable output

#### Expected Model Output (JSON)

```json
{
  "suggested_filename": "golden_retriever_playing_fetch",
  "tags": ["dog", "golden retriever", "outdoors", "ball", "pet"],
  "description": "A golden retriever running across grass while playing fetch."
}
```

#### Rules

- `suggested_filename` must be filesystem-safe
- No emojis
- No punctuation except underscores
- Lowercase by default (configurable later)

### 5.3 Preview & Review UI

Main table view:

| Preview | Current Name | Suggested Name | Tags | Status |

Per-image actions:

- Accept
- Reject
- Edit name
- Edit tags

Bulk actions:

- Accept all
- Accept selected
- Reject selected

### 5.4 Apply Changes

When accepted:

- Rename file on disk
- Write tags to metadata:
  - JPEG/PNG: EXIF / XMP keywords
- Handle name collisions:
  - Append _1, _2, etc.
- Dry-run mode available by default.

## 6. Safety & Trust Features
### Rename Safety

- Preview always required
- No overwrite without confirmation
- Undo support (basic):
  - Session-based rename log
  - "Revert last run" option

### API Safety

- API keys:
  - Stored encrypted (DPAPI)
  - Never logged
- Rate limiting to avoid accidental bill nuking

## 7. Settings Panel (MVP)

- Gemini API key
- Model selection (hardcoded initially)
- Filename style:
  - lowercase / TitleCase
- Max tags per image
- Skip images with existing tags (on/off)
- Dry run default (on/off)

## 8. Non-Goals (Explicitly Out of Scope)

- Cloud syncing
- User accounts
- Auto-running in background
- Face recognition or identity naming
- Video support

This keeps IrisSort a tool, not a platform.

## 9. Internal Architecture (Suggested)

### Core Modules

- FolderScanner
- ImageHasher (dedupe & cache)
- GeminiClient
- PromptBuilder
- ResultParser
- RenamePlanner
- MetadataWriter
- UndoManager

### Data Model

```csharp
class ImageAnalysisResult
{
    string OriginalPath;
    string SuggestedFilename;
    List<string> Tags;
    string Description;
    AnalysisStatus Status;
}
```

## 10. Future Extensions (Not MVP)

- Local model fallback
- Batch prompt tuning
- Style presets (Photography, Art, Screenshots, Memes)
- Rules engine ("always include date if present")
- Auto-foldering by tag
- CLI version

## 11. MVP Success Criteria

IrisSort is "done" when:

- A non-technical user can safely rename 500 images
- Filenames are consistently useful
- No files are lost
- The UI never surprises the user