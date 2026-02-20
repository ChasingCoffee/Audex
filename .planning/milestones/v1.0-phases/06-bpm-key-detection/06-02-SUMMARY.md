---
phase: 06-bpm-key-detection
plan: 02
subsystem: UI Analysis Wiring
tags: [bpm-display, key-display, analysis-lifecycle, re-analyze, cache, progress, layout-renderer]
dependency_graph:
  requires: [06-01]
  provides: [StartBpmKeyAnalysis, CancelBpmKeyAnalysis, HitTestReanalyze, AnalysisDisplay]
  affects: [src/Audex/UI/LayoutRenderer.cs, src/Audex/UI/PreviewWindow.cs, src/Audex/PreviewHandler/AudioPreviewHandler.cs]
tech_stack:
  added: []
  patterns: [analysis-id stale-callback guard, 800ms delay before analysis start, 2% progress batching threshold, 2s re-analyze cooldown]
key_files:
  created: []
  modified:
    - src/Audex/UI/LayoutRenderer.cs
    - src/Audex/UI/PreviewWindow.cs
    - src/Audex/PreviewHandler/AudioPreviewHandler.cs
decisions:
  - "Re-analyze button visible only when analysisResult != null AND at least one value came from detection (not tags)"
  - "Reanalysis dims old values alpha=128 and shows progress below key row during active re-analysis"
  - "Progress batching threshold 2% matches BpmKeyAnalyzer key phase reporting threshold"
  - "_metadataBounds cached in OnPaint field (mirrors _waveformBounds pattern) for targeted Invalidate on analysis state changes"
  - "Re-analyze button uses GDI+ DrawArc for circular arrow icon — no Unicode font dependency in prevhost.exe"
metrics:
  duration: ~5 minutes
  completed: 2026-02-17
  tasks_completed: 2
  files_created: 0
  files_modified: 3
---

# Phase 6 Plan 2: BPM/Key Analysis UI Wiring Summary

**One-liner:** LayoutRenderer extended with detected/tag/progress display + re-analyze button; PreviewWindow adds full analysis lifecycle (start/cancel/progress/cache/re-analyze); AudioPreviewHandler triggers and cancels analysis.

## Objective

Connected the BPM/key analysis engine (Plan 01) to the user-facing preview UI. Selecting an audio file without BPM/key tags now automatically detects and displays values with confidence percentages after an 800ms delay, with results cached for instant recall on re-open.

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Extend LayoutRenderer for detected/tag display and re-analyze button | 32cbe8e | LayoutRenderer.cs |
| 2 | Add analysis lifecycle to PreviewWindow and trigger from AudioPreviewHandler | d1ecd55 | PreviewWindow.cs, AudioPreviewHandler.cs |

## What Was Built

### LayoutRenderer.cs

Extended `Render()` and `DrawMusicInfoSection()` with analysis state parameters:
- `AnalysisResult? analysisResult`, `bool isAnalyzing`, `float analysisProgress`, `bool isReanalyzing`, `bool isReanalyzeHovered`
- BPM display: `"120 BPM (tag)"` / `"120 BPM (detected — 92%)"` / `"Analyzing... 45%"` / `"— (unable to detect)"`
- Key display: same pattern — `"Am (tag)"` / `"Am (detected — 71%)"` / progress / failure
- Dimming: existing detected values drawn at alpha=128 during re-analysis; progress text shown below key row
- Re-analyze button: GDI+ arc-based circular refresh icon (18x18 logical pixels, DPI-scaled) to the right of the Music Info header
- Hover highlight: semi-transparent overlay when cursor is over button
- Tooltip: `"Re-analyze BPM/Key"` drawn above button when hovered
- `HitTestReanalyze(Point)`: static hit test backed by `_reanalyzeButtonBounds` static field

### PreviewWindow.cs

Added complete analysis lifecycle mirroring `StartWaveformGeneration` pattern:

**State fields:**
- `_analysisResult`, `_isAnalyzing`, `_analysisProgress`, `_isReanalyzing`
- `_currentAnalysisId` (Interlocked increment for stale-callback guard)
- `_analysisCts` (CancellationTokenSource)
- `_lastReanalyzeTime` (cooldown tracking)
- `_currentAudioData`, `_currentCacheKey`, `_isModuleFormat`, `_currentDuration` (re-analyze support)
- `_isReanalyzeHovered`, `_metadataBounds` (hover/invalidate)

**StartBpmKeyAnalysis():**
1. Cancels prior analysis, stores tag presence and audio reference
2. Skips if both tags present, module format, or duration < 5s; checks config toggle
3. Cache hit → instant display via `AnalysisCache.Read()`
4. 800ms cancellable delay via `ct.WaitHandle.WaitOne()`
5. Progress batching: only invokes UI when change >= 2%
6. Caches result (even failures) via `AnalysisCache.Write()`
7. Stale-callback guard: `_currentAnalysisId != analysisId` check before UI update

**Re-analyze flow (OnMouseDown):**
- 2-second cooldown check
- `AnalysisCache.Delete(_currentCacheKey)` to force fresh analysis
- Sets `_isReanalyzing = true` (dims old values during re-run)
- Calls `StartBpmKeyAnalysis()` with stored references

**Hover handling (OnMouseMove/OnMouseLeave):** Updates `_isReanalyzeHovered`, sets Hand cursor, invalidates `_metadataBounds`.

**UpdateContent():** Resets all analysis state on file switch.

**Dispose():** Calls `CancelBpmKeyAnalysis()` before `CancelWaveformGeneration()`.

### AudioPreviewHandler.cs

In `DoPreviewInternal()`: after waveform generation trigger, calls:
```csharp
InvokeOnUI(() => _previewWindow.StartBpmKeyAnalysis(
    analysisDataRef, isModule, analysisDuration, hasBpmTag, hasKeyTag));
```

In `Unload()`: cancels analysis before waveform generation:
```csharp
InvokeOnUI(() => _previewWindow.CancelBpmKeyAnalysis());
```

## Verification

- `dotnet build`: **0 errors, 0 warnings**
- `HitTestReanalyze` method present in LayoutRenderer.cs: confirmed
- `_reanalyzeButtonBounds` static field present: confirmed
- `AnalysisResult` referenced in LayoutRenderer.cs: confirmed
- `StartBpmKeyAnalysis` method in PreviewWindow.cs: confirmed
- `CancelBpmKeyAnalysis` method in PreviewWindow.cs: confirmed
- `BpmKeyAnalyzer.Analyze` called in PreviewWindow.cs: confirmed
- `AnalysisCache.Read` and `AnalysisCache.Write` called in PreviewWindow.cs: confirmed
- `StartBpmKeyAnalysis` called in AudioPreviewHandler.cs: confirmed
- `CancelBpmKeyAnalysis` called in AudioPreviewHandler.cs Unload(): confirmed
- `HitTestReanalyze` called in PreviewWindow.cs OnMouseDown: confirmed

## Deviations from Plan

None — plan executed exactly as written.

One minor implementation note: the plan mentioned showing `"Analyzing... X%"` for both BPM and Key rows during first-time analysis. The implementation shows the progress text in both the Key row and BPM row independently during first-time analysis. During re-analysis, the old detected values are shown dimmed and a separate progress line appears below the Key row (to avoid overwriting the existing BPM/Key display while re-analyzing).

## Self-Check

| Item | Status |
|------|--------|
| LayoutRenderer.cs modified | FOUND |
| PreviewWindow.cs modified | FOUND |
| AudioPreviewHandler.cs modified | FOUND |
| commit 32cbe8e (Task 1) | FOUND |
| commit d1ecd55 (Task 2) | FOUND |
| Build: 0 errors | PASSED |

## Self-Check: PASSED
