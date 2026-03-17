---
phase: 05-extended-format-support
plan: 02
subsystem: audio-metadata-ui-registration
tags: [bpm, key, dj-software, tag-reading, music-info-ui, format-error, registration-scripts]
dependency_graph:
  requires: ["05-01"]
  provides: ["bpm-key-display", "format-error-display", "dynamic-registration"]
  affects: ["UI/LayoutRenderer", "UI/WaveformRenderer", "UI/PreviewWindow", "Audio/TagReader", "scripts/register"]
tech_stack:
  added: ["MusicKeyNormalizer (new class)", "MusicInfo (new class)", "Serato Autotags GEOB parsing"]
  patterns: ["most-precise-wins BPM selection", "Camelot/OpenKey/text key normalization", "dynamic plugin DLL presence check"]
key_files:
  created:
    - src/Audex/Audio/MusicKeyNormalizer.cs
  modified:
    - src/Audex/Audio/TagReader.cs
    - src/Audex/FileReader/AudioFileInfo.cs
    - src/Audex/PreviewHandler/AudioPreviewHandler.cs
    - src/Audex/UI/LayoutRenderer.cs
    - src/Audex/UI/WaveformRenderer.cs
    - src/Audex/UI/PreviewWindow.cs
    - src/Audex/Config/AppConfig.cs
    - src/Audex/Config/ConfigManager.cs
    - scripts/register.ps1
    - scripts/unregister.ps1
decisions:
  - "GeneralEncapsulatedObjectFrame.Object returns ByteVector not byte[] — use ByteVector.Data with CS0618 pragma suppression"
  - "SelectMostPreciseBpm reads all sources before selecting winner — not first-wins"
  - "DrawTagGrid now returns yOffset (was void) to allow Music Info section to follow correctly"
  - "formatError field stored in PreviewWindow not passed as parameter — cleaner than threading through OnPaint"
metrics:
  duration_seconds: 511
  duration_display: "~8.5 minutes"
  completed_date: "2026-02-17"
  tasks_completed: 3
  tasks_total: 3
  files_modified: 10
  files_created: 1
---

# Phase 5 Plan 02: BPM/Key Metadata, Music Info UI, Format Error Display, Registration Scripts Summary

BPM/key reading from all tag types (ID3v2/Vorbis/APE/Serato GEOB) with Camelot+OpenKey+text normalization, Music Info section in UI always visible, format error display in waveform area, and dynamic registration scripts with backup/restore.

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Create MusicKeyNormalizer and extend TagReader | 8d9abad | MusicKeyNormalizer.cs, TagReader.cs, AudioFileInfo.cs, AudioPreviewHandler.cs |
| 2 | Add Music Info UI section, format error display, config extension | aedd90a | LayoutRenderer.cs, WaveformRenderer.cs, PreviewWindow.cs, AppConfig.cs, ConfigManager.cs |
| 3 | Update registration scripts with dynamic plugin-based registration | eeeba7c | scripts/register.ps1, scripts/unregister.ps1 |

## What Was Built

### Task 1: BPM/Key Tag Reading with DJ Software Coverage

**MusicKeyNormalizer.cs** (new) — Static normalizer mapping to standard notation:
- Camelot Wheel: "8A" → "Am", "8B" → "C" (full 24-key mapping)
- Open Key: "1d" → "C", "1m" → "Am" (full 24-key mapping)
- Text forms: "A minor" → "Am", "C# major" → "C#", bare root "F" → "F"
- Falls back to raw input if format is unrecognized (shows something vs nothing)

**TagReader.cs** extended with `ReadMusicInfo(byte[] data, string fileName)`:
- BPM from: ID3v2 TBPM → Vorbis BPM → APE BPM → Serato Autotags GEOB (fallback)
- Key from: ID3v2 TKEY → Vorbis INITIALKEY → APE INITIALKEY
- "Most precise wins" BPM selection: "119.97" beats "120" (most decimal places)
- Serato Autotags GEOB parsing: skips 2-byte header, reads null-terminated ASCII BPM
- All wrapped in try/catch returning MusicInfo(null, null) on any exception

**DJ Software Coverage** (documented in code comments):
- Traktor: Standard TBPM + TKEY → covered by ID3v2 sources 1
- rekordbox: Writes TKEY (covered); does NOT write TBPM (known limitation, Phase 6 fills gap)
- Serato: Standard TBPM/TKEY + GEOB fallback

**AudioFileInfo.cs** — Added: `Bpm`, `Key`, `IsModuleFormat`, `FormatError` fields

**AudioPreviewHandler.cs** — Wired:
- `PluginManager.IsFormatSupported()` check before `LoadFile`; sets `formatError` if unsupported
- `TagReader.ReadMusicInfo()` call after tag reading
- `Bpm`, `Key`, `IsModuleFormat`, `FormatError` populated in AudioFileInfo construction
- Waveform generation skipped when `formatError != null`

### Task 2: Music Info UI Section and Format Error Display

**LayoutRenderer.cs**:
- New `DrawMusicInfoSection()` called after tag grid, always visible
- Section header "Music Info" (9.5pt bold in secondary text color)
- Key row first, BPM row second (per user decision)
- Dashes "-" for missing values (placeholder for Phase 6 BPM detection)
- Module formats: Bit Depth and Bitrate rows hidden (not meaningful for .mod/.xm/.it/.s3m)
- `DrawTagGrid()` now returns `int` yOffset (was void) for correct layout chaining

**WaveformRenderer.cs**:
- New `DrawFormatError(Graphics g, Rectangle bounds, string errorMessage, float dpiScale)` static method
- Draws waveform background + top border + centered "Format Unavailable: {reason}" text
- Text rendered in secondary text color with ellipsis trimming

**PreviewWindow.cs**:
- Added `_formatError` field; populated from `info.FormatError` in `UpdateContent()`
- `OnPaint` branches: `DrawFormatError` when `_formatError != null`, else normal `WaveformRenderer.Draw`

**AppConfig.cs**: Default `SupportedExtensions` extended to include `.aif`, `.mod`, `.xm`, `.it`, `.s3m`

**ConfigManager.cs**:
- Load: `[Formats]` section takes precedence over legacy `[FileTypes]`
- Save: writes `[Formats]` Extensions entry alongside `[Audio]` and `[Waveform]`

### Task 3: Registration Scripts

**scripts/register.ps1**:
- Core + module extensions always registered: `.wav`, `.mp3`, `.flac`, `.aiff`, `.aif`, `.ogg`, `.mod`, `.xm`, `.it`, `.s3m`
- Plugin-dependent only registered when DLL present in output dir:
  - `bass_aac.dll` → `.aac`, `.m4a`
  - `basswma.dll` → `.wma`
  - `bassopus.dll` → `.opus`
- Backup step (before registration loop): saves existing non-Audex CLSIDs to `%LOCALAPPDATA%\Audex\prev-handlers.json`

**scripts/unregister.ps1**:
- Uses full 14-extension superset (regardless of which plugins are currently present)
- Safety check: only removes registry entries where our CLSID is registered
- Restore step: reads `prev-handlers.json`, re-registers previous handlers, removes backup file

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] TagLib# GeneralEncapsulatedObjectFrame.Object returns ByteVector not byte[]**
- **Found during:** Task 1, first build attempt
- **Issue:** `geob.Object` return type is `TagLib.ByteVector`, not `byte[]` as the plan assumed
- **Fix:** Changed `byte[] frameData = geob.Object` to `TagLib.ByteVector bv = geob.Object; byte[] frameData = bv.Data`. Added `#pragma warning disable CS0618` since the frame type is also marked obsolete
- **Files modified:** `src/Audex/Audio/TagReader.cs`
- **Commit:** 8d9abad

**2. [Rule 2 - Missing return value] DrawTagGrid needed to return yOffset**
- **Found during:** Task 2 implementation
- **Issue:** `DrawTagGrid()` was `void` — Music Info section following it had no way to know the updated y position
- **Fix:** Changed return type from `void` to `int` and updated call site to capture returned yOffset before adding padding
- **Files modified:** `src/Audex/UI/LayoutRenderer.cs`
- **Commit:** aedd90a

**3. [Rule 2 - Null safety] Nullable reference warnings for string? List.Add calls**
- **Found during:** Task 1, first build attempt
- **Issue:** Compiler (nullable reference types enabled) warned on `bpmCandidates.Add(tbpm)` even though `!string.IsNullOrWhiteSpace(tbpm)` guarantees non-null
- **Fix:** Added null-forgiving operator `!` on all 4 BPM candidate additions and on `raw.Trim()` in `NormalizeBpm`
- **Files modified:** `src/Audex/Audio/TagReader.cs`
- **Commit:** 8d9abad

## Verification Results

- `dotnet build` succeeds with 0 errors, 0 warnings
- `ReadMusicInfo` present in TagReader.cs
- `MusicKeyNormalizer.Normalize` called in TagReader.cs
- `FormatError` field in AudioFileInfo.cs
- Traktor/rekordbox coverage documented in TagReader.cs comments
- "Music Info" section in LayoutRenderer.cs
- "Format Unavailable" in WaveformRenderer.cs
- `_formatError` field and `formatError` wiring in PreviewWindow.cs
- `[Formats]` section read/write in ConfigManager.cs
- `Test-Path.*bass_aac.dll` in register.ps1
- `prev-handlers.json` in both scripts
- `.mod` in both scripts
- Both scripts parse without syntax errors

## Self-Check: PASSED

All 12 expected files found on disk. All 3 task commits (8d9abad, aedd90a, eeeba7c) present in git log. Build succeeds with 0 errors, 0 warnings.
