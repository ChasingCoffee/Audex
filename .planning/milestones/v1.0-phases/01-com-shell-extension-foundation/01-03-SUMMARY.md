---
phase: 01-com-shell-extension-foundation
plan: 03
subsystem: preview-ui
tags: [winforms, theme-detection, gdi-plus, registry-registration, preview-window]
dependency_graph:
  requires: [audio-preview-handler, header-parsers, config-manager, logger]
  provides: [preview-window-ui, theme-helper, layout-renderer, error-banner, registration-scripts]
  affects: [02-bass-audio-integration]
tech_stack:
  added: []
  patterns: [winforms-usercontrol, setparent-reparenting, double-buffered-paint, dpi-scaling, registry-theme-detection]
key_files:
  created:
    - src/Audex/UI/PreviewWindow.cs
    - src/Audex/UI/ThemeHelper.cs
    - src/Audex/UI/LayoutRenderer.cs
    - src/Audex/UI/ErrorBanner.cs
    - scripts/register.ps1
    - scripts/unregister.ps1
  modified:
    - src/Audex/PreviewHandler/AudioPreviewHandler.cs
    - src/Audex/PreviewHandler/PreviewHandlerRegistration.cs
    - src/Audex/FileReader/AudioHeaderParserFactory.cs
decisions:
  - WinForms UserControl with SetParent() reparenting (NOT raw Win32 CreateWindowEx — prevhost.exe crashes with raw HWND approach)
  - Double-buffered OnPaint rendering with GDI+ (no flicker during resize)
  - Loading spinner appears only after 200ms delay to avoid flash on fast loads
  - DisableLowILProcessIsolation=1 DWORD on CLSID required for .NET CLR in prevhost.exe
  - ThreadingModel=Apartment on InprocServer32 (required for WinForms STA)
  - Register under SystemFileAssociations AND ProgID shellex for reliable extension coverage
  - Display filename without extension in preview header (instead of raw filename or "Unknown")
  - Try IStream.Stat with flag=0 first to get filename, fall back to flag=1 (STATFLAG_NONAME)
requirements-completed: [PREV-01, PREV-02, PREV-04, PREV-05]
metrics:
  duration_minutes: ~15
  tasks_completed: 3
  files_created: 6
  files_modified: 3
  commits: 3
  completed_date: 2026-02-16
---

# Phase 01 Plan 03: Preview Window UI & Registration Summary

**WinForms UserControl preview window with theme-aware GDI+ rendering (file info, waveform/controls placeholders, error banner), DPI scaling, and PowerShell register/unregister scripts for 9 audio extensions — verified end-to-end in Explorer**

## Objective Achieved

Delivered the visible user experience for the preview handler. Selecting an audio file in Explorer now shows a theme-aware preview with filename (sans extension), file size, format, and parsed metadata (sample rate, bit depth, channels, duration) for WAV/MP3/FLAC. Unsupported formats show "Playback coming soon." Grayed-out waveform and controls placeholders hint at future features. Registration scripts enable one-command setup for development.

## Tasks Completed

### Task 1: Implement preview window with theme-aware UI rendering
**Commit:** `fab1a1b`
**Files:** PreviewWindow.cs, ThemeHelper.cs, LayoutRenderer.cs, ErrorBanner.cs, AudioPreviewHandler.cs

Created WinForms UserControl with double-buffered OnPaint rendering. ThemeHelper detects system dark/light mode via HKCU registry (AppsUseLightTheme). LayoutRenderer draws file info panel (30%), waveform placeholder (45%), and controls placeholder (25%) with DPI-scaled fonts and padding. ErrorBanner renders semi-transparent overlay with user-friendly message and log path. Loading spinner shows only after 200ms delay.

### Task 2: Create file type registration and dev scripts
**Commit:** `07993a4`
**Files:** register.ps1, unregister.ps1, PreviewHandlerRegistration.cs

PowerShell registration script builds Release x64, kills prevhost.exe, runs regasm, sets DisableLowILProcessIsolation=1, configures ThreadingModel=Apartment, and registers 9 extensions (.wav, .mp3, .flac, .aiff, .ogg, .aac, .wma, .opus, .m4a) under both SystemFileAssociations and ProgID shellex keys.

### Critical Fix: Rewrite preview window to WinForms UserControl pattern
**Commit:** `ed42c77`
**Files:** PreviewWindow.cs, AudioPreviewHandler.cs, AudioHeaderParserFactory.cs, register.ps1, unregister.ps1

Raw Win32 CreateWindowEx with custom WndProc caused prevhost.exe to crash. Rewrote to WinForms UserControl pattern: create control in constructor (STA thread), force Handle creation, use SetParent() for reparenting, Control.Invoke() for MTA-to-STA marshaling. This is the required pattern for .NET preview handlers.

### Task 3: Human verification — end-to-end Explorer testing
**Status:** Approved by user

All 8 test scenarios verified:
- WAV/MP3/FLAC display parsed metadata correctly
- OGG/M4A show "Playback coming soon"
- Rapid file switching: no crashes, debounce working
- Dark/light theme respected
- Preview pane resizes correctly
- Log file exists at %LOCALAPPDATA%

## Deviations from Plan

### Auto-fixed Issues

**1. [Critical] Rewrote PreviewWindow from Win32 to WinForms UserControl**
- **Found during:** Task 1 integration testing in Explorer
- **Issue:** Raw Win32 CreateWindowEx + RegisterClassEx + custom WndProc caused prevhost.exe to crash — .NET CLR cannot reliably host raw Win32 windows in the COM surrogate process
- **Fix:** Complete rewrite to WinForms UserControl with SetParent() reparenting, Control.Invoke() for thread marshaling
- **Files modified:** PreviewWindow.cs, AudioPreviewHandler.cs
- **Commit:** ed42c77

**2. [Enhancement] Display filename without extension instead of "Unknown"**
- **Found during:** Task 3 human verification
- **Issue:** IStream.Stat with STATFLAG_NONAME (flag=1) doesn't return filename, showing "Unknown" in preview header
- **Fix:** Try Stat with flag=0 first to get filename; display Path.GetFileNameWithoutExtension() in LayoutRenderer
- **Files modified:** AudioPreviewHandler.cs, LayoutRenderer.cs
- **Not yet committed** (will be included in summary commit)

---

**Total deviations:** 2 (1 critical architecture fix, 1 user-requested enhancement)
**Impact on plan:** Architecture fix was essential — raw Win32 approach is fundamentally incompatible with .NET preview handlers. Enhancement improves UX.

## Issues Encountered

- prevhost.exe crashes with raw Win32 HWND approach — resolved by switching to WinForms UserControl pattern (documented in MEMORY.md for future reference)
- DLL lock from prevhost.exe requires killing the process before rebuilding

## User Setup Required

None - registration scripts handle all setup.

## Next Phase Readiness

Phase 1 complete. All COM infrastructure, UI rendering, header parsing, and registration working end-to-end in Explorer. Ready for Phase 2 (BASS Audio Integration) which will replace the "Playback coming soon" placeholder with actual audio playback.

## Self-Check: PASSED

**Created files verified:**
- FOUND: src/Audex/UI/PreviewWindow.cs
- FOUND: src/Audex/UI/ThemeHelper.cs
- FOUND: src/Audex/UI/LayoutRenderer.cs
- FOUND: src/Audex/UI/ErrorBanner.cs
- FOUND: scripts/register.ps1
- FOUND: scripts/unregister.ps1

**Commits verified:**
- FOUND: fab1a1b (Task 1 - UI rendering)
- FOUND: 07993a4 (Task 2 - registration scripts)
- FOUND: ed42c77 (Critical fix - WinForms rewrite)

**Build verification:** Build succeeded - 0 Warnings, 0 Errors
