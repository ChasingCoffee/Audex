---
status: resolved
trigger: "Investigate two issues: toggling detection off then back on doesn't re-run analysis; cached results not shown when detection is off"
created: 2026-02-17T00:00:00Z
updated: 2026-02-17T00:01:00Z
---

## Current Focus

hypothesis: CONFIRMED — two separate bugs found via code trace
test: N/A — code path analysis sufficient
expecting: N/A
next_action: Return diagnosis

## Symptoms

expected: (1) Toggling detection back on should re-run analysis for current file. (2) Cached BPM/key should display even when detection is off.
actual: (1) UI section shows but detection doesn't run again. (2) Cached results not shown when detection is off.
errors: none reported
reproduction: Toggle analysis off then on in settings overlay
started: unknown

## Eliminated

## Evidence

- timestamp: 2026-02-17T00:00:30Z
  checked: SettingsHitZone.AnalysisToggle handler in PreviewWindow.cs lines 374-385
  found: Handler ONLY toggles cfg.EnableBpmKeyDetection and calls Invalidate(). Does NOT call StartBpmKeyAnalysis().
  implication: Toggling the setting on does NOT trigger analysis for the currently loaded file. Only affects the NEXT file loaded via DoPreviewInternal.

- timestamp: 2026-02-17T00:00:40Z
  checked: StartBpmKeyAnalysis lines 779-781
  found: Early return when !EnableBpmKeyDetection — exits BEFORE cache lookup (line 788) and before _currentCacheKey is set (line 785)
  implication: When detection is off, cache is never consulted, _currentCacheKey is never set, _analysisResult stays null. LayoutRenderer receives null analysisResult and shows "-" for both BPM and Key.

- timestamp: 2026-02-17T00:00:45Z
  checked: LayoutRenderer.DrawMusicInfoSection lines 256-457
  found: Renderer does NOT check EnableBpmKeyDetection. It simply renders whatever analysisResult is passed. If null, shows "-".
  implication: Rendering is correct — the problem is upstream. analysisResult is null because cache was never consulted.

- timestamp: 2026-02-17T00:00:50Z
  checked: DoPreviewInternal in AudioPreviewHandler.cs lines 404-414
  found: StartBpmKeyAnalysis is called once during file load on the STA thread. No re-invocation path from settings toggle.
  implication: Confirms Issue 1 — the only call site for analysis is during file load.

## Resolution

root_cause: |
  Issue 1: AnalysisToggle handler (PreviewWindow.cs:374-385) only persists the config flag and repaints.
  It does NOT re-invoke StartBpmKeyAnalysis() for the current file when toggled back on.

  Issue 2: StartBpmKeyAnalysis (PreviewWindow.cs:779-781) early-returns when detection is disabled
  BEFORE the cache lookup (line 788). This means _analysisResult stays null and cached
  BPM/key values are never displayed, even though they exist on disk.

fix:
verification:
files_changed: []
