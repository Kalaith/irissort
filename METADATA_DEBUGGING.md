# Metadata Writing Diagnostics

## Issue
Metadata (tags, comments, title, subject) not consistently being written to image files after rename.

## Improvements Made

### 1. Enhanced Logging
All metadata operations now log detailed information at multiple levels:

- **Debug**: Field-by-field operations
- **Info**: Successful operations with counts
- **Warning**: Format issues, verification failures
- **Error**: Exceptions and failures

### 2. File Handle Release
Added 50ms delay between file rename and metadata write to ensure Windows releases file handles properly.

**Location**: `RenamePlannerService.cs:106`

### 3. Metadata Write Verification
After saving metadata, the code now:
1. Re-reads the file
2. Verifies metadata was actually written
3. Returns `false` if verification fails

**Location**: `MetadataWriterService.cs:152-172`

### 4. Detailed Field Tracking
Counts how many metadata fields are written and logs the count.

**Location**: `MetadataWriterService.cs:61-151`

### 5. Format Detection
Logs file extension and MIME type to identify format-specific issues.

**Location**: `MetadataWriterService.cs:35-45`

## Log File Location

Logs are written to:
```
%LOCALAPPDATA%\IrisSort\logs\irissort.log
```

On Windows, this typically resolves to:
```
C:\Users\<YourUsername>\AppData\Local\IrisSort\logs\irissort.log
```

## What to Look For in Logs

### Successful Metadata Write
```
[INFO] Successfully saved 4 metadata fields to C:\path\image.jpg (.jpg)
[DEBUG] Verification passed: Metadata confirmed in C:\path\image.jpg
```

### Failed Metadata Write
```
[WARNING] Metadata write returned false for C:\path\image.jpg. Check logs for format support or file issues.
```

### Format Issues
```
[WARNING] Unsupported format for C:\path\image.webp
```

### Verification Failure
```
[WARNING] Verification failed: No metadata found after save for C:\path\image.jpg
```

### Exception During Write
```
[ERROR] Metadata write exception for C:\path\image.jpg
System.IO.IOException: The process cannot access the file...
```

## Debugging Steps

1. **Check the logs** at `%LOCALAPPDATA%\IrisSort\logs\irissort.log`

2. **Look for patterns**:
   - Are specific file formats failing? (WEBP, PNG vs JPEG)
   - Are all files failing or just some?
   - Do you see "Verification failed" messages?
   - Are there exceptions logged?

3. **Common Issues**:

   **WEBP Format**: TagLib# has limited WEBP support. Metadata may not stick.
   - **Solution**: Convert to JPEG/PNG or use a different metadata tool

   **PNG Format**: PNG uses different metadata standards (XMP, EXIF, iTXt chunks)
   - **Symptom**: Genres/Keywords may not be supported
   - **Solution**: Check if `imageTag.Keywords` is being set

   **File Locking**: Antivirus or file indexing services
   - **Symptom**: "The process cannot access the file" errors
   - **Solution**: Temporarily disable AV, increase delay in RenamePlannerService.cs:106

   **Read-Only Files**: File attributes preventing writes
   - **Symptom**: Access denied errors
   - **Solution**: Check file properties

4. **Test Individual Files**:
   - Try renaming a single JPEG file
   - Check if metadata appears in Windows Explorer (Right-click > Properties > Details)
   - Compare with PNG and WEBP files

## Manual Verification

To manually check if metadata was written:

1. **Windows Explorer**:
   - Right-click the file
   - Properties > Details tab
   - Look for Title, Subject, Tags, Comments

2. **ExifTool** (if installed):
   ```bash
   exiftool image.jpg
   ```

3. **IrisSort ReadTags Method**:
   The `MetadataWriterService.ReadTags()` method can verify tags were written.

## Format Support Matrix

| Format | Title | Comment | Tags/Keywords | Notes |
|--------|-------|---------|---------------|-------|
| JPEG   | ✅    | ✅      | ✅            | Best support via EXIF/IPTC |
| PNG    | ✅    | ✅      | ✅            | **NEW: XMP support via iTXt chunks** |
| WEBP   | ⚠️    | ⚠️      | ⚠️            | Limited - basic metadata only |

Legend:
- ✅ Full support
- ⚠️ Limited/inconsistent support
- ❌ Not supported

### **PNG Metadata - FIXED!**
PNG files now use **XMP metadata** stored in iTXt chunks, which is the same standard Windows Explorer uses. This means:
- Title, Subject, Description, Tags, Copyright, and Author fields will all work
- Metadata is readable by Windows Explorer, Adobe apps, and other XMP-compatible software
- Uses Dublin Core (dc:) and EXIF namespaces for maximum compatibility

## Next Steps if Issues Persist

1. Share the log file from `%LOCALAPPDATA%\IrisSort\logs\`
2. Note which file formats are failing
3. Try with JPEG files only to isolate format issues
4. Check Windows Event Viewer for file system errors
5. Consider using alternative metadata libraries for WEBP (like ExifTool CLI wrapper)
