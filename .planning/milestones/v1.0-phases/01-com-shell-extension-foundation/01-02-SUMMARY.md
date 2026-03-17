---
phase: 01-com-shell-extension-foundation
plan: 02
subsystem: preview-handler-core
tags: [com-handler, audio-parsing, lifecycle-management, debouncing]
dependency_graph:
  requires: [com-interfaces, config-manager, logger]
  provides: [audio-preview-handler, header-parsers, wav-parser, mp3-parser, flac-parser]
  affects: [01-03-ui-placeholder]
tech_stack:
  added: []
  patterns: [com-lifecycle, lazy-loading, debouncing, header-parsing, pinvoke]
key_files:
  created:
    - src/Audex/PreviewHandler/AudioPreviewHandler.cs
    - src/Audex/PreviewHandler/PreviewHandlerRegistration.cs
    - src/Audex/FileReader/AudioFileInfo.cs
    - src/Audex/FileReader/AudioHeaderParserFactory.cs
    - src/Audex/FileReader/WavHeaderParser.cs
    - src/Audex/FileReader/Mp3HeaderParser.cs
    - src/Audex/FileReader/FlacHeaderParser.cs
    - src/Audex/FileReader/StreamHelper.cs
  modified: []
decisions:
  - Used System.Threading.Timer for debouncing (automatic disposal, works in low-integrity process)
  - Marshal.ReleaseComObject on IStream in Unload to ensure deterministic cleanup
  - MP3 duration estimation assumes CBR (acceptable for Phase 1, VBR support deferred)
  - Unsupported formats return partial AudioFileInfo with zero metadata (no error state)
metrics:
  duration_minutes: 4.3
  tasks_completed: 2
  files_created: 8
  commits: 1
  completed_date: 2026-02-16
---

# Phase 01 Plan 02: AudioPreviewHandler COM Class Summary

**One-liner:** COM-visible AudioPreviewHandler class with full IPreviewHandler lifecycle (lazy loading, 150ms debounce, deterministic cleanup) plus WAV/MP3/FLAC header parsers extracting sample rate, bit depth, channels, and duration from IStream without external audio libraries

## Objective Achieved

Implemented the complete COM preview handler backbone with all four required interfaces (IPreviewHandler, IInitializeWithStream, IObjectWithSite, IOleWindow). The handler follows Microsoft's preview handler guidelines with lazy loading in DoPreview, debounced file switching (immediate first load, 150ms delay for subsequent), and full resource cleanup in Unload. Created format-specific header parsers for WAV (RIFF), MP3 (frame headers with ID3v2 skipping), and FLAC (STREAMINFO metadata block) that read directly from IStream COM objects.

## Tasks Completed

### Task 1: AudioPreviewHandler COM class with lifecycle management
**Commit:** `e95466d`
**Files:** AudioPreviewHandler.cs, PreviewHandlerRegistration.cs, AudioFileInfo.cs, AudioHeaderParserFactory.cs, WavHeaderParser.cs, Mp3HeaderParser.cs, FlacHeaderParser.cs, StreamHelper.cs

Implemented complete COM-visible class with all interface methods and proper lifecycle management. Initialize() stores IStream reference without reading data (lazy loading per Microsoft guidance). DoPreview() implements debouncing: first load is immediate, subsequent rapid loads wait 150ms using System.Threading.Timer. Unload() releases IStream via Marshal.ReleaseComObject, destroys window, cancels timers, and resets state for next use.

**Key implementation details:**
- All COM methods wrapped in try/catch blocks - never propagate exceptions to prevhost.exe
- P/Invoke declarations for SetParent, SetWindowPos, SetFocus, GetFocus, DestroyWindow
- PreviewHandlerRegistration uses [ComRegisterFunction]/[ComUnregisterFunction] attributes
- Registry writes to HKLM PreviewHandlers (with HKCU fallback if not admin)
- OnPreviewDataReady() is a virtual method stub for Plan 03 to override with actual UI rendering

### Task 2: Audio file header parsers (WAV, MP3, FLAC)
**Commit:** `e95466d` (same commit - interdependent with Task 1)
**Files:** Already listed above

Created format-specific parsers that read directly from IStream COM objects using StreamHelper utility. WAV parser scans for fmt and data chunks (handles non-standard chunk ordering), extracts PCM parameters, calculates duration from data size / byte rate. MP3 parser skips ID3v2 tags (syncsafe integer parsing), finds first valid frame sync (0xFF + 3 bits), decodes MPEG1 Layer III bitrate/sample rate from lookup tables, estimates duration assuming CBR. FLAC parser validates fLaC marker, reads STREAMINFO metadata block (34 bytes), unpacks bit-packed fields: sample rate (20 bits), channels (3 bits), bit depth (5 bits), total samples (36 bits).

**Key implementation details:**
- StreamHelper.ReadBytes() handles COM marshaling (allocates CoTaskMem for out int, frees after read)
- AudioHeaderParserFactory routes by extension, returns partial AudioFileInfo for unsupported formats (.ogg, .aac, .wma, .opus, .m4a, .aiff)
- All parsers return AudioFileInfo with ParseSucceeded=false + ParseError on failure
- MP3 duration is CBR estimate (VBR would require scanning Xing/VBRI headers - deferred)
- WAV parser handles compressed WAV by detecting audioFormat != 1 (logs warning, sets duration to 0)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Created StreamHelper utility class**
- **Found during:** Task 2 implementation - parsers needed IStream reading
- **Issue:** Plan described inline IStream reading code, but 3 parsers would duplicate marshaling logic
- **Fix:** Extracted StreamHelper.ReadBytes() and TryReadBytes() for DRY principle
- **Files created:** StreamHelper.cs
- **Commit:** e95466d

**2. [Rule 3 - Blocking] Combined Task 1 and Task 2 into single commit**
- **Found during:** Task 1 build verification - AudioPreviewHandler referenced AudioFileInfo/Factory before they existed
- **Issue:** Task 1 cannot compile without Task 2 classes (circular dependency in plan structure)
- **Fix:** Implemented both tasks before committing, single atomic commit
- **Files affected:** All 8 files in single commit
- **Commit:** e95466d

## Verification Results

All success criteria met:

✅ **Build:** Project compiles with 0 errors, 0 warnings
✅ **COM Interfaces:** AudioPreviewHandler implements all 4 interfaces (IPreviewHandler, IInitializeWithStream, IObjectWithSite, IOleWindow)
✅ **Lazy Loading:** Initialize() stores IStream without reading data
✅ **Debouncing:** DoPreview() immediate first load, 150ms timer for subsequent loads (from ConfigManager.DebounceMs)
✅ **Resource Cleanup:** Unload() releases IStream, destroys window, cancels timer, resets state
✅ **COM Safety:** All interface methods wrapped in try/catch, log errors instead of throwing
✅ **WAV Parser:** Scans for fmt/data chunks, extracts sample rate/bit depth/channels/duration
✅ **MP3 Parser:** Skips ID3v2, finds frame sync, decodes bitrate/sample rate, estimates duration
✅ **FLAC Parser:** Validates fLaC marker, reads STREAMINFO, unpacks bit-packed fields
✅ **Unsupported Formats:** Factory returns partial AudioFileInfo with zero metadata for .ogg, .aac, etc.

**Code review verification:**
- Initialize() calls: `_stream = pstream;` (stores without reading) ✅
- DoPreview() checks: `if (_isFirstLoad) { _isFirstLoad = false; DoPreviewInternal(); }` ✅
- DoPreview() else: Creates Timer with ConfigManager.Load().DebounceMs delay ✅
- Unload() calls: `Marshal.ReleaseComObject(_stream); _stream = null;` ✅
- Unload() calls: `_debounceTimer?.Dispose(); _debounceTimer = null;` ✅
- Unload() resets: `_isFirstLoad = true;` ✅

## Technical Notes

**COM Lifecycle Pattern:**
- Initialize is the "constructor" - receives IStream, stores it, does NOT read
- SetWindow is called after Initialize - stores parent HWND and bounding rect
- DoPreview is the "run" method - reads from IStream, parses headers, signals UI
- Unload is the "destructor" - releases IStream, destroys window, fully resets state
- SetRect/SetFocus/QueryFocus/TranslateAccelerator are maintenance methods during preview lifetime

**Debouncing Implementation:**
- First load: `_isFirstLoad` flag starts true, DoPreview executes immediately, sets flag false
- Subsequent loads: DoPreview cancels existing timer (if any), creates new Timer with DebounceMs delay
- Timer callback executes DoPreviewInternal after delay expires
- Rapid file switching: each new DoPreview cancels previous timer, only final selection "wins"
- Unload resets `_isFirstLoad = true` so next Initialize/DoPreview cycle starts fresh

**Header Parsing Approach:**
- No external audio libraries (NAudio, TagLib) - all parsing is hand-written binary reading
- Works with IStream COM objects, not file paths - compatible with shell namespace extensions
- WAV: Standard RIFF chunk iteration (handles non-standard chunk ordering unlike fixed-offset parsers)
- MP3: Frame-level parsing only, no full decode - sufficient for metadata display
- FLAC: Reads only STREAMINFO block (first metadata block), ignores others
- Unsupported formats: No error state, just return format name + file size

**COM Registration:**
- [ComRegisterFunction] attribute makes Register() callable via regasm.exe
- HKLM write requires admin, fallback to HKCU for user-only installation
- File extension associations NOT handled here - Plan 03 will register .wav/.mp3/.flac mappings
- AppID {6d2b5079-2f0b-48dd-ab7f-97cec514d30b} associates handler with prevhost.exe

**P/Invoke Usage:**
- SetParent: Reparent preview window when SetWindow called with new parent
- SetWindowPos: Resize preview window when SetRect called or window created
- SetFocus/GetFocus: Focus management for keyboard navigation
- DestroyWindow: Clean up preview window in Unload
- All P/Invoke in nested NativeMethods class (standard pattern)

## Dependencies Satisfied

**Requirements:**
- 01-01: Uses COM interfaces (IPreviewHandler, IInitializeWithStream, IObjectWithSite, IOleWindow)
- 01-01: Uses Logger for debug/info/error logging throughout
- 01-01: Uses ConfigManager for DebounceMs setting
- PREV-01: Implements full COM lifecycle per Microsoft preview handler spec
- PREV-02: Implements 150ms debounce (first load immediate, subsequent loads debounced)
- PREV-03: Unload releases IStream and destroys window (no file locks after navigation away)

**Provides for future plans:**
- 01-03: AudioPreviewHandler base class ready for UI implementation (OnPreviewDataReady hook)
- 01-03: AudioFileInfo data structure for UI display
- 01-03: Header parsers provide metadata for file info panel
- 02-*: Preview handler infrastructure ready for audio playback integration

## Next Steps

Plan 01-03 will implement the actual preview window UI (WinForms or WPF), wire it to the OnPreviewDataReady() hook, and display the parsed AudioFileInfo in a file info panel. The UI will show filename, file size, format, sample rate, bit depth, channels, and duration. It will also register file extension associations for .wav/.mp3/.flac/.aiff/.ogg/.aac/.wma/.opus/.m4a using the config-driven extension list.

## Self-Check: PASSED

**Created files verified:**
```
FOUND: src/Audex/PreviewHandler/AudioPreviewHandler.cs
FOUND: src/Audex/PreviewHandler/PreviewHandlerRegistration.cs
FOUND: src/Audex/FileReader/AudioFileInfo.cs
FOUND: src/Audex/FileReader/AudioHeaderParserFactory.cs
FOUND: src/Audex/FileReader/WavHeaderParser.cs
FOUND: src/Audex/FileReader/Mp3HeaderParser.cs
FOUND: src/Audex/FileReader/FlacHeaderParser.cs
FOUND: src/Audex/FileReader/StreamHelper.cs
```

**Commits verified:**
```
FOUND: e95466d (Task 1+2 - AudioPreviewHandler + header parsers)
```

**Build verification:**
```
Build succeeded - 0 Warning(s), 0 Error(s)
Output: C:\dev\projects\Music\Audex\src\Audex\bin\x64\Debug\net48\Audex.dll
```
