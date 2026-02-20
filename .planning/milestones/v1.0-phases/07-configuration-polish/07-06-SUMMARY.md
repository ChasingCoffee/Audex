---
phase: 07-configuration-polish
plan: 06
subsystem: audio
tags: [wasapi, bass, audioplayer, device-switching, gap-closure]

# Dependency graph
requires:
  - phase: 07-02
    provides: SettingsOverlay with WASAPI device selection UI that saves WasapiDeviceIndex to config
  - phase: 07-01
    provides: ConfigManager.Load() with WasapiDeviceIndex field in AppConfig

provides:
  - WASAPI output device switching — selecting a device in settings routes audio to it on next file preview
  - AudioPlayer.SwitchDevice(int) — teardown + reinit WASAPI + mixer with new device
  - AudioPlayer.CurrentDeviceIndex property — tracks active device for change detection
  - Initialize(int deviceIndex=-1) — first init uses saved device preference

affects:
  - Any future work touching AudioPlayer initialization or WASAPI lifecycle

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Compare config vs in-memory state on each preview to detect user preference changes
    - Teardown-then-reinit pattern for WASAPI device switching (Stop->Free->Init->Mixer->Start)
    - Reapply per-session state (volume, mute) after WASAPI reinit (WASAPI session volume is per-device)

key-files:
  created: []
  modified:
    - src/Audex/Audio/AudioPlayer.cs
    - src/Audex/PreviewHandler/AudioPreviewHandler.cs

key-decisions:
  - "Device switch takes effect on next file preview (not hot-swap) — consistent with the UI note already in place from 07-02"
  - "Fallback to default device (-1) on SwitchDevice failure — keeps audio working even if selected device disappears"
  - "Volume and mute reapplied after device switch because WASAPI session volume is per-device and resets on reinit"
  - "Config loaded once in constructor as initConfig, reused for volume/mute to avoid two disk reads"

patterns-established:
  - "WASAPI device lifecycle: Stop -> Free -> Init(deviceIndex) -> GetInfo -> CreateMixer -> Start"
  - "Device change detection: compare ConfigManager.Load().WasapiDeviceIndex to _player.CurrentDeviceIndex before LoadFile"

requirements-completed: [CONF-02]

# Metrics
duration: 7min
completed: 2026-02-18
---

# Phase 07 Plan 06: WASAPI Output Device Switching Summary

**WASAPI output device selection now routes audio to the chosen device — AudioPlayer.SwitchDevice tears down and reinits WASAPI+mixer with the new device index on each file preview when the config changes**

## Performance

- **Duration:** ~7 min
- **Started:** 2026-02-18T01:12:18Z
- **Completed:** 2026-02-18T01:18:57Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Added `_currentDeviceIndex` field, `CurrentDeviceIndex` property, and `Initialize(int deviceIndex=-1)` to AudioPlayer so device preference is respected from first init
- Implemented `SwitchDevice(int)` method with full WASAPI teardown/reinit: Stop, Free, Init new device, get format info, recreate mixer at new sample rate, restart WASAPI
- Wired device change detection into `DoPreviewInternal` — checks config vs current device before every file load, calls `SwitchDevice` if different, reapplies volume/mute

## Task Commits

Each task was committed atomically:

1. **Task 1: Add SwitchDevice method to AudioPlayer** - `8de5dc0` (feat)
2. **Task 2: Wire device switch into DoPreviewInternal** - `838ca9a` (feat)

## Files Created/Modified

- `src/Audex/Audio/AudioPlayer.cs` - Added `_currentDeviceIndex` field, `CurrentDeviceIndex` property, updated `Initialize(int deviceIndex=-1)`, added `SwitchDevice(int)` method with WASAPI teardown/reinit and fallback
- `src/Audex/PreviewHandler/AudioPreviewHandler.cs` - Constructor uses `initConfig.WasapiDeviceIndex` for `Initialize()`, `DoPreviewInternal` detects device change and calls `SwitchDevice` with volume/mute reapplication

## Decisions Made

- Device switch takes effect on next file preview (not hot-swap) — consistent with the informational note already shown in the settings overlay from plan 07-02
- Fallback to default device (-1) on `SwitchDevice` failure so audio remains functional if selected device disappears
- Volume and mute reapplied after device switch because WASAPI session volume is per-device and resets to device default on reinit
- Config loaded once as `initConfig` in constructor; `var config = initConfig` reuses it for volume/mute to avoid redundant disk reads

## Deviations from Plan

None — plan executed exactly as written. The pre-existing build errors (`SettingsOverlayRenderer.Draw` missing `waveformHeightPreset` arg, `_tooltip` field removal) were found to already be resolved in the working tree from prior gap-closure work; prevhost.exe locking the DLL was the only build obstacle and was resolved by killing the process.

## Issues Encountered

- Pre-existing uncommitted changes to PreviewWindow.cs and SettingsOverlayRenderer.cs (from prior gap-closure work) caused apparent compile errors on first build. After killing prevhost.exe (which held the DLL lock), the build succeeded cleanly — the working tree changes were already internally consistent.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- WASAPI device switching is fully wired: UI saves choice, AudioPlayer reads and applies it on next file
- All 5 UAT gap-closure plans (07-02 through 07-06) are now complete
- v1.0 milestone is ready for packaging and release

---
*Phase: 07-configuration-polish*
*Completed: 2026-02-18*
