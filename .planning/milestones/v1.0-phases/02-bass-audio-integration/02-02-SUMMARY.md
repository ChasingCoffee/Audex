---
phase: 02-bass-audio-integration
plan: 02
subsystem: ui
tags: [winforms, gdi-plus, owner-drawn, control-bar, seek-bar, volume-slider, audio-player, metadata-grid]
dependency_graph:
  requires:
    - phase: 02-01
      provides: AudioPlayer (BASS/WASAPI engine), TagReader, AudioPlayerState, AppConfig volume fields
  provides:
    - ControlBarRenderer (owner-drawn seek bar, transport buttons, volume slider)
    - HitZone enum for control bar hit-testing
    - Updated LayoutRenderer (metadata grid with tags, no placeholders)
    - Updated PreviewWindow (AudioPlayer wiring, 250ms position timer, mouse events)
    - Updated AudioPreviewHandler (IStream byte copy, AudioPlayer lifecycle, TagReader integration)
  affects: [03-waveform, future-phases-using-ui]
tech-stack:
  added: []
  patterns:
    - Static renderer receives state, never owns it (ControlBarRenderer, LayoutRenderer)
    - Layout rectangles cached in static fields for zero-recompute hit testing
    - GDI+ geometric shapes for icons (no Unicode font dependency)
    - Position timer (STA WinForms Timer, 250ms) polls AudioPlayer.CurrentPositionSeconds
    - InvokeRequired guard pattern for AudioPlayer event handlers crossing thread boundaries
    - IStream bulk read via Marshal.AllocCoTaskMem pattern (matches existing StreamHelper)
key-files:
  created:
    - src/Audex/UI/ControlBarRenderer.cs
  modified:
    - src/Audex/UI/ThemeHelper.cs (9 new control bar color methods)
    - src/Audex/UI/LayoutRenderer.cs (metadata grid replacing placeholders)
    - src/Audex/UI/PreviewWindow.cs (AudioPlayer wiring, timer, mouse events)
    - src/Audex/PreviewHandler/AudioPreviewHandler.cs (byte copy, AudioPlayer lifecycle)
    - src/Audex/Audio/AudioPlayer.cs (added public StopAndFreeCurrentStream)
key-decisions:
  - "ControlBarRenderer is a static renderer — receives state, does not own it (matches LayoutRenderer pattern)"
  - "GDI+ geometric shapes for button icons — no dependency on Unicode font rendering in prevhost.exe"
  - "Layout rectangles cached in static fields so HitTest() works without re-running Draw()"
  - "Position timer uses STA WinForms Timer (not System.Threading.Timer) — fires on UI thread safely"
  - "BASS initialized once in AudioPreviewHandler constructor, kept alive across file switches"
  - "Unload calls StopAndFreeCurrentStream() not Shutdown() — BASS device persists for next file"
  - "IStream bulk copy uses Marshal.AllocCoTaskMem pattern (consistent with StreamHelper.cs)"
  - "AudioPreviewHandler uses Phase 1 header parsers for bit depth (BASS reports 32-bit float for decode streams)"
requirements-completed: [PLAY-01, PLAY-03, FMT-01, META-01, META-02]
duration: ~13 min (Tasks 1-2 automated; Task 3 human-verified with bug fixes)
completed: 2026-02-17
---

# Phase 02 Plan 02: Control Bar UI and Audio Wiring Summary

**Owner-drawn seek bar/transport/volume control bar with GDI+ icons, metadata grid with tag support, and full AudioPlayer wiring from Explorer IStream to WASAPI output.**

## Status

All 3 tasks complete. Task 3 (human verification) revealed two bugs — both fixed and verified.

## Performance

- **Duration:** ~13 min (automated tasks + human verification + bug fixes)
- **Started:** 2026-02-17T06:22:05Z
- **Completed:** 2026-02-17
- **Tasks completed:** 3 of 3
- **Files modified:** 6

## Accomplishments

- ControlBarRenderer.cs created: DPI-scaled, theme-aware owner-drawn control bar with seek bar (elapsed/total time labels, fill, animated thumb), Play/Pause/Stop buttons (GDI+ geometric icons, hover/press states), volume slider with speaker icon (muted/unmuted with X or arcs)
- LayoutRenderer.cs overhauled: metadata grid with two-column label-value layout (Format, Sample Rate, Bit Depth, Channels, Duration, Bitrate), optional tags section (Title, Artist, Album) hidden when all null, no more Waveform/Controls placeholders
- PreviewWindow.cs wired to AudioPlayer: SetPlayer() method, 250ms position timer, OnMouseDown/Move/Up/Leave handlers driving play/pause/stop/seek/volume/mute, volume/mute persistence to config
- AudioPreviewHandler.cs wired end-to-end: CopyStreamToBytes (64KB chunked IStream read), AudioPlayer.Initialize in constructor, LoadFile + TagReader.ReadTags per file, StopAndFreeCurrentStream in Unload (BASS stays alive)

## Task Commits

1. **Task 1: Create ControlBarRenderer and extend ThemeHelper** - `a60f309` (feat)
2. **Task 2: Wire AudioPlayer into PreviewWindow/AudioPreviewHandler, update LayoutRenderer** - `95b42b0` (feat)
3. **Task 3: Verify audio playback and controls in Explorer** - `8fc4a75` (fix — BassMix + silence zeroing)

## Files Created/Modified

- `src/Audex/UI/ControlBarRenderer.cs` - Owner-drawn control bar: seek bar, transport buttons, volume slider, HitZone hit-testing
- `src/Audex/UI/ThemeHelper.cs` - Added 9 control bar color methods (SeekBarTrack/Fill/Thumb, Button/Hover/Press, VolumeTrack/Fill, ControlBarBackground)
- `src/Audex/UI/LayoutRenderer.cs` - Replaced waveform/controls placeholders with metadata grid + optional tag section
- `src/Audex/UI/PreviewWindow.cs` - AudioPlayer wiring, position timer, mouse event handlers, volume persistence
- `src/Audex/PreviewHandler/AudioPreviewHandler.cs` - IStream byte copy, AudioPlayer lifecycle (init once, free per file), TagReader integration
- `src/Audex/Audio/AudioPlayer.cs` - Added public StopAndFreeCurrentStream() method

## Decisions Made

- **ControlBarRenderer is a static renderer**: matches LayoutRenderer pattern — receives state, does not own it. State is owned by PreviewWindow.
- **GDI+ geometric shapes for icons**: no dependency on Unicode font rendering in prevhost.exe (Braille spinner chars work; geometric shapes are more reliable for custom icons).
- **Layout rectangles cached in static fields**: `_seekBarTrackRect`, `_playPauseButtonRect`, etc. are set during Draw() and used by HitTest(). Safe because there's only one preview panel at a time.
- **STA WinForms Timer for position polling**: fires on the STA message loop — no InvokeRequired guard needed for UI access. 250ms per research findings.
- **BASS initialized once, kept alive across file switches**: expensive init; Unload frees stream but not BASS device.
- **Bit depth from Phase 1 parsers**: BASS reports 32-bit float for all decode streams; WAV/FLAC parsers provide actual bit depth.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added public StopAndFreeCurrentStream() to AudioPlayer**
- **Found during:** Task 2 (AudioPreviewHandler.Unload implementation)
- **Issue:** Plan specified calling `_player.StopAndFreeCurrentStream()` in Unload, but AudioPlayer only had `private StopAndFreeStream()`. Would cause CS0122 compile error.
- **Fix:** Added thin public wrapper method `StopAndFreeCurrentStream()` that delegates to `StopAndFreeStream()`, preserving the private impl while exposing the capability needed by the handler.
- **Files modified:** src/Audex/Audio/AudioPlayer.cs
- **Verification:** Build succeeded, 0 errors.
- **Committed in:** 95b42b0 (Task 2 commit)

---

**2. [Human-verify] Added BassMix mixer for sample rate conversion**
- **Found during:** Task 3 (human verification in Explorer)
- **Issue:** Playback speed was wrong — no sample rate conversion between decode stream (e.g. 44100Hz) and WASAPI output (device default 48000Hz). WASAPI callback read raw decode data directly.
- **Fix:** Added BassMix mixer stream at WASAPI device's native sample rate/channels. Decode streams are added to mixer which handles automatic SRC and channel mapping. Added ManagedBass.Mix NuGet package and bassmix.dll native DLL.
- **Files modified:** AudioPlayer.cs, Audex.csproj, native/x64/bassmix.dll
- **Committed in:** 8fc4a75

**3. [Human-verify] Fixed buzzing on stop/file switch**
- **Found during:** Task 3 (human verification in Explorer)
- **Issue:** WASAPI callback returned `length` without zeroing buffer when not playing — stale buffer data played as buzzing noise.
- **Fix:** Added `RtlZeroMemory(buffer, length)` P/Invoke call before returning silence. Also zero remainder when mixer returns less than requested.
- **Files modified:** AudioPlayer.cs
- **Committed in:** 8fc4a75

**Total deviations:** 3 (1 auto-fixed, 2 from human verification)
**Impact on plan:** Bug fixes required for correct operation. No scope creep. BassMix dependency was a necessary addition not anticipated in the plan.

## Issues Encountered

None beyond the auto-fixed deviation above.

## Next Phase Readiness

- Audio playback fully functional from Explorer preview pane
- WAV, MP3, FLAC support confirmed
- All UI controls verified: seek bar, transport buttons, volume slider, mute toggle
- Metadata grid and tag display verified
- Volume persistence verified
- Phase 3 (waveform visualization) can begin

## Self-Check: PASSED

Files created/modified:

- [x] src/Audex/UI/ControlBarRenderer.cs — created
- [x] src/Audex/UI/ThemeHelper.cs — modified
- [x] src/Audex/UI/LayoutRenderer.cs — modified
- [x] src/Audex/UI/PreviewWindow.cs — modified
- [x] src/Audex/PreviewHandler/AudioPreviewHandler.cs — modified
- [x] src/Audex/Audio/AudioPlayer.cs — modified
- [x] src/Audex/Audex.csproj — modified (ManagedBass.Mix + bassmix.dll)
- [x] src/Audex/native/x64/bassmix.dll — created

Commits:
- [x] a60f309 — Task 1 commit
- [x] 95b42b0 — Task 2 commit
- [x] 8fc4a75 — Task 3 bug fixes (BassMix + silence zeroing)

*Phase: 02-bass-audio-integration*
*Completed: 2026-02-17*
