---
phase: 03-basic-waveform-visualization
plan: 02
subsystem: ui
tags: [waveform, bass, gdi+, winforms, mouse-events, caching, progressive-reveal, seek, scrub]

# Dependency graph
requires:
  - phase: 03-01
    provides: WaveformGenerator, WaveformCache, WaveformRenderer, ThemeHelper waveform colors
  - phase: 02-bass-audio-integration
    provides: AudioPlayer.Seek(), AudioPlayer.Play(), AudioPlayerState, position timer pattern
provides:
  - PreviewWindow: waveform state fields, layout region, mouse seek/scrub/hover, progressive reveal, generation lifecycle
  - AudioPreviewHandler: StartWaveformGeneration trigger on file load, CancelWaveformGeneration on unload
  - ControlBarRenderer: HitZone.Waveform enum value
  - Complete end-to-end waveform seekbar working in Explorer preview pane
affects:
  - Phase 4+ (any future changes to PreviewWindow layout or mouse handling)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Waveform generation batched invoke — progress callbacks fire every 50 bars, not every bar, to avoid 2000 Invoke calls
    - CancellationToken + generation ID guard — stale callbacks from previous files are silently discarded
    - Cache-first waveform load — ReadCache checked before spawning background thread; instant display on revisit
    - Visual-only drag — mouse drag updates _waveformDragPosition without seeking audio; seek fires on MouseUp
    - Targeted Invalidate — only _waveformBounds or controlBarBounds invalidated per update; full Invalidate avoided

key-files:
  created: []
  modified:
    - src/Audex/UI/PreviewWindow.cs
    - src/Audex/PreviewHandler/AudioPreviewHandler.cs
    - src/Audex/UI/ControlBarRenderer.cs

key-decisions:
  - "Waveform height fixed at 120px DPI-scaled (not 43% of pane) — user requested it not scale with resize"
  - "Start/end time labels removed from waveform — redundant with control bar time display"
  - "Click on waveform while stopped starts playback from clicked position"
  - "Drag is visual-only; audio seeks on MouseUp release (not during drag)"
  - "Waveform generation progress batched every 50 bars to avoid excessive UI thread marshaling"

patterns-established:
  - "Generation ID guard: capture int generationId before background thread, discard callback if _currentGenerationId != generationId"
  - "Cache-first pattern: ComputeCacheKey → ReadCache → if hit return immediately, else generate + WriteCache"
  - "Fixed DPI-scaled height: (int)(120 * dpiScale) — preferred over percentage-of-pane for stable layout"

requirements-completed: [WAVE-01, WAVE-03, WAVE-04, PLAY-02]

# Metrics
duration: ~20min (includes human verification)
completed: 2026-02-17
---

# Phase 3 Plan 2: Waveform Integration Summary

**Progressive waveform seekbar wired into Explorer preview pane — click-to-seek, drag-to-scrub, smooth playhead animation, hover guide with tooltip, and cache-first instant display on revisit**

## Performance

- **Duration:** ~20 min (includes human verification)
- **Started:** 2026-02-16T23:57:39Z
- **Completed:** 2026-02-17T00:17:10Z
- **Tasks:** 2 (1 auto + 1 human-verify checkpoint)
- **Files modified:** 3

## Accomplishments
- PreviewWindow wired to WaveformGenerator, WaveformCache, and WaveformRenderer — waveform appears between metadata and control bar with progressive left-to-right reveal
- Full seek interaction: click for instant seek, click-and-drag for visual scrub (audio seeks on release), clicking while stopped starts playback
- Playhead animates smoothly during playback; bars behind playhead render at 55% opacity; hover shows crosshair cursor, guide line, and time tooltip
- Cache-first load with generation ID guard ensures instant waveform on revisit and clean cancellation when switching files

## Task Commits

Each task was committed atomically:

1. **Task 1: Integrate waveform generation, rendering, and mouse interaction** - `ab1fb36` (feat)
2. **Post-verification fix: Fixed-height waveform and remove redundant time labels** - `df65f92` (fix)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `src/Audex/UI/PreviewWindow.cs` - Waveform state fields, layout region, OnPaint call to WaveformRenderer.Draw, mouse events (hover/seek/scrub), StartWaveformGeneration, CancelWaveformGeneration, UpdateContent reset
- `src/Audex/PreviewHandler/AudioPreviewHandler.cs` - StartWaveformGeneration trigger after LoadFile, CancelWaveformGeneration in Unload
- `src/Audex/UI/ControlBarRenderer.cs` - HitZone.Waveform added to enum

## Decisions Made
- **Fixed waveform height:** User requested the waveform not scale with pane resize, so height changed from 43% of pane to fixed 120px (DPI-scaled). This gives a stable, predictable layout regardless of Explorer pane height.
- **Time labels removed:** Start/end time labels (0:00 and total duration) were removed from the waveform area. The control bar already shows elapsed/total time, making the waveform labels redundant.
- **Click-while-stopped starts playback:** Clicking the waveform while audio is stopped or idle triggers both Seek() and Play() so the user can audition any position without pressing play first.
- **Drag is visual-only:** During mouse drag, only _waveformDragPosition is updated (for the visual playhead), and audio seeks on MouseUp. This prevents choppy seeks during scrubbing.
- **Batched progressive reveal:** onBarReady callback invokes on the UI thread every 50 bars (not every bar) to avoid ~2000 Invoke calls during generation.

## Deviations from Plan

### Auto-fixed Issues (post-verification)

**1. [Rule 1 - User Feedback] Changed waveform height from percentage to fixed 120px**
- **Found during:** Human verification (Task 2)
- **Issue:** User found waveform scaling with pane height undesirable — waveform changed size on resize
- **Fix:** Changed `(int)(ClientRectangle.Height * 0.43f)` to `(int)(120 * dpiScale)` for stable fixed height
- **Files modified:** src/Audex/UI/PreviewWindow.cs
- **Verification:** Waveform stays at consistent height when Explorer pane is resized
- **Committed in:** df65f92

**2. [Rule 1 - User Feedback] Removed start/end time labels from waveform**
- **Found during:** Human verification (Task 2)
- **Issue:** Time labels at waveform edges were redundant with control bar elapsed/total display
- **Fix:** Removed label rendering from WaveformRenderer call parameters; labels no longer drawn
- **Files modified:** src/Audex/UI/PreviewWindow.cs
- **Verification:** Clean waveform area without text labels; control bar continues to show time
- **Committed in:** df65f92

---

**Total deviations:** 2 user-feedback fixes (post human-verify checkpoint)
**Impact on plan:** Both fixes improve UX based on direct user observation. No scope creep.

## Issues Encountered

None during automated implementation. All behavior matched plan spec. Post-verification tweaks were user preference refinements, not bugs.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 3 (Basic Waveform Visualization) is fully complete — both plans executed and verified
- All WAVE-01, WAVE-03, WAVE-04, PLAY-02 requirements satisfied
- Phase 4 can proceed: waveform seekbar is stable, interactive, and cached
- PreviewWindow layout now has three regions: metadata (top), waveform (fixed 120px DPI-scaled), control bar (60px DPI-scaled)

---
*Phase: 03-basic-waveform-visualization*
*Completed: 2026-02-17*
