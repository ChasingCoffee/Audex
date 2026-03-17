---
phase: 07-configuration-polish
plan: 03
subsystem: ui
tags: [keyboard-shortcuts, TranslateAccelerator, tooltips, WinForms, COM, shell-extension]

# Dependency graph
requires:
  - phase: 07-01
    provides: Settings overlay infrastructure and config system
  - phase: 07-02
    provides: SettingsOverlayRenderer, ToggleSettings/CloseSettings/IsSettingsOpen API

provides:
  - TranslateAccelerator keyboard shortcut routing (Ctrl+Space/Left/Right/Up/Down/L/M/comma, Escape)
  - TogglePlayPause, SeekRelative, AdjustVolume, ToggleMute public methods on PreviewWindow
  - GetTooltipText(HitZone) on ControlBarRenderer with shortcut hints
  - WinForms ToolTip integration in PreviewWindow showing per-zone hints on hover
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - TranslateAccelerator as Shell-sanctioned keyboard routing mechanism (not global hooks)
    - Tooltip hints include focus guidance ("click preview pane first") for keyboard shortcuts

key-files:
  created: []
  modified:
    - src/Audex/PreviewHandler/AudioPreviewHandler.cs
    - src/Audex/UI/PreviewWindow.cs
    - src/Audex/UI/ControlBarRenderer.cs

key-decisions:
  - "GetKeyState used instead of e.Control — TranslateAccelerator receives MSG struct, not KeyEventArgs"
  - "OemComma (0xBC) used as raw VK code for Ctrl+, (Keys.OemComma value is 0xBC)"
  - "Tooltip text includes 'click preview pane first' guidance — TranslateAccelerator only fires when pane has focus"
  - "AdjustVolume unmutes before adjusting when currently muted — adjust from muted = implicit unmute"
  - "ToggleMute calls SetVolume(0) when muting (redundant with SetMute but belt-and-suspenders)"
  - "ToolTip component initialized in constructor with 400ms delay for responsive but not intrusive hints"

patterns-established:
  - "Keyboard routing pattern: check WM_KEYDOWN, GetKeyState for modifiers, switch on vk, dispatch via InvokeOnUI"
  - "Tooltip update pattern: update on zone change in OnMouseMove, clear with empty string on None zone"

requirements-completed:
  - PLAY-05

# Metrics
duration: 8min
completed: 2026-02-17
---

# Phase 7 Plan 03: Keyboard Shortcuts and Tooltip Hints Summary

**Shell TranslateAccelerator intercepts Ctrl+modifier shortcuts routing to 8 playback actions, with per-button tooltip hints showing shortcut names and focus guidance**

## Performance

- **Duration:** 8 min
- **Started:** 2026-02-17T23:52:49Z
- **Completed:** 2026-02-17T23:59:00Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- TranslateAccelerator fully implemented: intercepts 8 shortcut combos + Escape, forwards everything else to Explorer frame
- All required PreviewWindow action methods added: TogglePlayPause, SeekRelative, AdjustVolume, ToggleMute
- GetKeyState P/Invoke added for Ctrl-state detection from MSG struct (no KeyEventArgs available in COM callback)
- ControlBarRenderer.GetTooltipText returns per-zone tooltip strings with shortcut hints and focus guidance
- WinForms ToolTip component wired to OnMouseMove for hover-triggered hints on all interactive controls

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement TranslateAccelerator keyboard routing and PreviewWindow action methods** - `972ee17` (feat)
2. **Task 2: Add keyboard shortcut hints to control bar tooltips** - `816293c` (feat)

**Plan metadata:** (committed with SUMMARY.md)

## Files Created/Modified

- `src/Audex/PreviewHandler/AudioPreviewHandler.cs` - Added GetKeyState P/Invoke, WM_KEYDOWN constants, rewrote TranslateAccelerator with Ctrl+key switch table
- `src/Audex/UI/PreviewWindow.cs` - Added TogglePlayPause, SeekRelative, AdjustVolume, ToggleMute; ToolTip field initialized in constructor; OnMouseMove updates tooltip on zone change
- `src/Audex/UI/ControlBarRenderer.cs` - Added GetTooltipText(HitZone) static method

## Decisions Made

- **GetKeyState for modifier detection:** TranslateAccelerator receives a raw MSG struct, not a WinForms KeyEventArgs. GetKeyState(VK_CONTROL) is the correct API to check Ctrl state at message-time.
- **OemComma as 0xBC raw VK code:** The Ctrl+, shortcut uses the raw virtual key constant (0xBC) matching Keys.OemComma, since we switch on the integer wParam value.
- **AdjustVolume unmutes when muted:** Pressing Ctrl+Up/Down while muted implicitly unmutes — matches standard media player behavior.
- **ToolTip 400ms delay:** Long enough to avoid flicker on fast mouse movements, short enough to feel responsive.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None. Build succeeded with 0 warnings and 0 errors on first attempt for both tasks.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- PLAY-05 keyboard shortcut requirement is now complete
- All Phase 7 plan 03 deliverables are shipped
- Ready to continue with remaining Phase 7 plans (installer, etc.)

## Self-Check: PASSED

- FOUND: src/Audex/PreviewHandler/AudioPreviewHandler.cs
- FOUND: src/Audex/UI/PreviewWindow.cs
- FOUND: src/Audex/UI/ControlBarRenderer.cs
- FOUND: .planning/phases/07-configuration-polish/07-03-SUMMARY.md
- FOUND commit: 972ee17 (Task 1)
- FOUND commit: 816293c (Task 2)
- FOUND commit: 9c619d7 (docs/metadata)
- Build: succeeded 0 errors 0 warnings

---
*Phase: 07-configuration-polish*
*Completed: 2026-02-17*
