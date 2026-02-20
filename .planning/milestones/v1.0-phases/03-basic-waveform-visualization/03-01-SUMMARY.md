---
phase: 03-basic-waveform-visualization
plan: 01
subsystem: ui
tags: [bass, waveform, gdi+, sha256, caching, renderer]

# Dependency graph
requires:
  - phase: 02-bass-audio-integration
    provides: Bass.CreateStream decode pattern, GCHandle pinned buffer lifecycle, BassFlags.Decode|Float usage
provides:
  - WaveformGenerator: background BASS decode-only stream producing float[~2000] peak array
  - WaveformCache: SHA-256 keyed binary .wf files in %TEMP%\Audex\ with 50MB LRU eviction
  - WaveformRenderer: complete GDI+ waveform visualization (bars, playhead, hover tooltip, time labels)
  - ThemeHelper waveform color methods (7 new methods for bar, played bar, background, center line, playhead, guide, label)
affects:
  - 03-02 (wires these components into PreviewWindow)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - WaveformGenerator uses BASS decode-only stream (not added to mixer/WASAPI) — pure PCM extraction
    - Bass.StreamFree before GCHandle.Free — established ordering to avoid freed-memory access
    - Static renderer pattern: receives all state, caches layout bounds, exposes HitTest — same as ControlBarRenderer

key-files:
  created:
    - src/Audex/Audio/WaveformGenerator.cs
    - src/Audex/UI/WaveformCache.cs
    - src/Audex/UI/WaveformRenderer.cs
  modified:
    - src/Audex/UI/ThemeHelper.cs

key-decisions:
  - "WaveformGenerator uses separate BASS decode-only stream (not BassMix) — pure waveform extraction without audio output side effects"
  - "Canonical peak count is 2000 bars (renderer downsamples at paint time) — display-resolution independent"
  - "WaveformCache uses %TEMP%\\Audex\\ (not LOCALAPPDATA) — transient derived data, not user data"
  - "Played bars behind playhead drawn at alpha=140 (55% opacity) for subtle dimming effect"
  - "White playhead in both themes — visible against dark background, contrasts with blue bars on light background"

patterns-established:
  - "Static renderer pattern: WaveformRenderer follows ControlBarRenderer exactly (no owned state, cached bounds, HitTest)"
  - "Bass.StreamFree always before GCHandle.Free — prevents BASS reading freed memory"

requirements-completed: [WAVE-01, WAVE-04]

# Metrics
duration: 3min
completed: 2026-02-17
---

# Phase 3 Plan 1: Waveform Components Summary

**BASS decode-only stream to float[2000] peak array, SHA-256 disk cache with 50MB LRU eviction, and GDI+ waveform renderer with mirrored bars, amplitude gradient, playhead, and hover tooltip**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-02-17T07:49:57Z
- **Completed:** 2026-02-17T07:52:27Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- WaveformGenerator decodes audio to 2000-bar peak array using a BASS decode-only stream, with CancellationToken support and progressive onBarReady callback
- WaveformCache persists peak arrays to %TEMP%\Audex\ as binary .wf files keyed by SHA-256 content hash, with 50MB oldest-first eviction
- WaveformRenderer draws the complete waveform visualization: mirrored vertical bars with rounded tops, blue-to-cyan amplitude gradient, played-portion dimming at 55% opacity, white playhead line with downward triangle marker, hover guide line with time tooltip, and start/end time labels
- ThemeHelper extended with 7 new waveform-specific color methods covering both light and dark themes

## Task Commits

Each task was committed atomically:

1. **Task 1: Create WaveformGenerator and WaveformCache** - `22e7f47` (feat)
2. **Task 2: Create WaveformRenderer and add ThemeHelper waveform colors** - `679019d` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `src/Audex/Audio/WaveformGenerator.cs` - BASS decode-only stream, 2000-bar peak extraction, CancellationToken support
- `src/Audex/UI/WaveformCache.cs` - SHA-256 keyed binary cache, 50MB LRU eviction, %TEMP%\Audex\
- `src/Audex/UI/WaveformRenderer.cs` - Static GDI+ renderer: bars, playhead, hover guide, time labels
- `src/Audex/UI/ThemeHelper.cs` - Added 7 waveform color methods

## Decisions Made
- WaveformGenerator uses a pure decode-only BASS stream (not added to BassMix/WASAPI mixer) — waveform extraction is fully independent of playback
- Canonical peak count is 2000 bars regardless of display width — WaveformRenderer downsamples at paint time, so the cached peak array can be reused at any display size
- WaveformCache stores to %TEMP% not %LOCALAPPDATA% — peaks are derived/transient data, appropriate for temp storage
- Played bars drawn at alpha=140 for subtle dimming effect per user decisions
- Playhead is white in both themes per user decisions

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All three components (WaveformGenerator, WaveformCache, WaveformRenderer) compile and are ready for integration
- Plan 02 can now wire these into PreviewWindow: trigger Generate on file load, check cache first, call Renderer.Draw in OnPaint, handle mouse events for hover/seek

---
*Phase: 03-basic-waveform-visualization*
*Completed: 2026-02-17*
