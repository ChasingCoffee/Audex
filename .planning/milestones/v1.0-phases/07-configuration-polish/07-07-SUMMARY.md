---
phase: 07-configuration-polish
plan: 07
subsystem: UI / Tooltip
tags: [tooltip, gdi+, owner-drawn, prevhost, winforms]
gap_closure: true
gap_issue: UAT-13

dependency_graph:
  requires: [07-02-SUMMARY.md, 07-03-SUMMARY.md]
  provides: [working-tooltips-on-control-bar-buttons]
  affects: [src/Audex/UI/PreviewWindow.cs]

tech_stack:
  added: []
  patterns:
    - "Owner-drawn tooltip rendered via GDI+ in OnPaint — no native popup window"
    - "400ms delay timer (WinForms Timer on STA thread) before showing tooltip"
    - "High-contrast tooltip colors: dark theme = light tooltip, light theme = dark tooltip"

key_files:
  modified:
    - src/Audex/UI/PreviewWindow.cs

decisions:
  - "Owner-drawn tooltip in OnPaint is the only viable tooltip approach in prevhost.exe — WinForms ToolTip creates a TOOLTIPS_CLASS popup that requires a WinForms Form in the window ancestry, which prevhost.exe does not provide"
  - "UpdateTooltipForPosition() helper called at end of OnMouseMove unifies gear icon and control bar zone tooltip logic"
  - "Tooltip suppressed when settings overlay open — avoids visual conflict between two overlay layers"

metrics:
  duration_seconds: 339
  duration_human: "~6 minutes"
  completed_date: "2026-02-18"
  tasks_completed: 1
  tasks_total: 1
  files_modified: 1
---

# Phase 07 Plan 07: Owner-Drawn Tooltip Implementation Summary

**One-liner:** Replaced broken WinForms ToolTip (TOOLTIPS_CLASS popup) with custom GDI+ tooltip rendered directly in OnPaint, making hover hints visible in prevhost.exe for the first time.

## What Was Built

Owner-drawn tooltip system that renders tooltip text directly on the UserControl's GDI+ surface rather than creating a native popup window.

**Root cause of UAT gap #13:** `WinForms ToolTip` internally creates a `TOOLTIPS_CLASS` native window. In prevhost.exe, the UserControl is reparented via `SetParent()` with no `WinForms.Form` in the window ancestry. The tooltip's owner window chain is broken — the popup opens off-screen or is immediately destroyed. No native tooltip approach works in this hosting environment.

**Fix:** Render the tooltip text as a GDI+ `DrawString` call directly on the control's `OnPaint` surface. Timer-based 400ms delay before showing, clamped-position rendering above the mouse cursor, high-contrast colors for both dark and light themes.

## Implementation Details

### Fields Added (replacing `ToolTip? _tooltip`)
```csharp
private string? _tooltipText;
private Point _tooltipPosition;
private System.Windows.Forms.Timer? _tooltipTimer;
private bool _tooltipVisible;
```

### Methods Added
- `UpdateTooltipForPosition(Point mousePos, bool overGear)` — Called from end of `OnMouseMove`. Unified handler for both gear icon and control bar zones. Starts 400ms timer on tooltip text change, clears immediately on zone-to-null transition.
- `DrawOwnerTooltip(Graphics g, float dpiScale)` — Called from `OnPaint` when `_tooltipVisible && !_settingsOpen`. Measures text, positions above cursor (clamped to control bounds), fills background, draws border and text in high-contrast colors.

### Integration Points
- `OpenSettings()` — Stops timer, clears `_tooltipVisible` and `_tooltipText` immediately when overlay opens
- `OnMouseLeave()` — Stops timer and clears tooltip state on mouse leave
- `Dispose()` — Stops and disposes `_tooltipTimer`

### Tooltip Colors
```
Dark theme:  background = RGB(240,240,240), text = RGB(30,30,30), border = RGB(180,180,180)
Light theme: background = RGB(50,50,50),    text = RGB(245,245,245), border = RGB(100,100,100)
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Pre-existing: SettingsOverlayRenderer.Draw missing waveformHeightPreset argument**
- **Found during:** Initial build attempt
- **Issue:** `SettingsOverlayRenderer.Draw()` signature had been updated (in commit f8ae830) to require a `waveformHeightPreset` string parameter, but the call site in `PreviewWindow.OnPaint` had not been updated.
- **Fix:** Added `_waveformHeightPreset` as the last argument to the `SettingsOverlayRenderer.Draw()` call.
- **Files modified:** `src/Audex/UI/PreviewWindow.cs`
- **Commit:** f8ae830 (included in prior gap-closure session)

### Note on Commit Attribution
The tooltip implementation changes were committed as part of commit `f8ae830` (`fix(07-05): waveform radio state, analysis toggle re-enable, cached results display`) during a prior gap-closure execution session. The commit included both the 07-05 plan changes and the 07-07 tooltip implementation. The build verifies both are correct and fully functional.

## Verification

- `dotnet build src/Audex -c Release` — 0 errors, 0 warnings
- No `new ToolTip` or `_tooltip.` references in PreviewWindow.cs
- `DrawOwnerTooltip` and `UpdateTooltipForPosition` methods present and wired
- Timer disposed in `Dispose()` override
- Tooltip suppressed when settings overlay is open

## Self-Check

### Files Verified
- `src/Audex/UI/PreviewWindow.cs` — FOUND (contains all tooltip implementation)

### Commits Verified
- `f8ae830` — FOUND (contains tooltip changes confirmed via `git show f8ae830`)

## Self-Check: PASSED
