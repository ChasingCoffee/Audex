---
phase: 07-configuration-polish
plan: 01
subsystem: config,ui,playback
tags: [json-config, autoplay, loop, control-bar, migration]
dependency_graph:
  requires: []
  provides:
    - JSON config at %LOCALAPPDATA%\Audex\config.json
    - INI-to-JSON migration on first load
    - Autoplay/Loop config fields
    - Autoplay/Loop checkboxes in control bar
    - AutoplayDelayMs debounced playback trigger
    - Loop-on-end playback behavior
  affects:
    - 07-02 (settings overlay reads/writes same config)
    - 07-03 (keyboard shortcuts call ToggleAutoplay/ToggleLoop)
tech_stack:
  added:
    - System.Text.Json 8.0.5 (JSON serialization)
  patterns:
    - INI migration runs once: config.ini exists AND config.json does not
    - Timer-per-file autoplay (System.Threading.Timer, canceled at start of each DoPreviewInternal)
    - Checkbox state stored in PreviewWindow fields, persisted via ConfigManager.Save on toggle
    - Static renderer pattern extended: Draw() gets isAutoplay/isLoop params with defaults
key_files:
  created: []
  modified:
    - src/Audex/Config/AppConfig.cs
    - src/Audex/Config/ConfigManager.cs
    - src/Audex/Utils/PathHelper.cs
    - src/Audex/Audex.csproj
    - src/Audex/UI/ControlBarRenderer.cs
    - src/Audex/UI/PreviewWindow.cs
    - src/Audex/PreviewHandler/AudioPreviewHandler.cs
decisions:
  - System.Text.Json 8.0.5 chosen over Newtonsoft.Json (modern Microsoft serializer, net48 compatible)
  - INI file preserved in place as backup after migration (not deleted)
  - Checkbox hit targets expanded by 4px DPI-scaled for easier clicking
  - Autoplay timer canceled at top of DoPreviewInternal for safety alongside debounce in DoPreview
  - Play() public method accepts Stopped, Paused, or Idle states to handle all post-load states
metrics:
  duration: "5m 15s"
  completed: 2026-02-17
  tasks_completed: 2
  files_modified: 7
---

# Phase 7 Plan 01: Config JSON Migration and Autoplay/Loop Controls Summary

JSON config migration replacing INI with System.Text.Json, INI migration on first load, plus autoplay/loop checkboxes on far left of control bar with 500ms debounced autoplay and loop-on-end behavior.

## Tasks Completed

| # | Task | Commit | Key Files |
|---|------|--------|-----------|
| 1 | Migrate config from INI to JSON and extend AppConfig | 24add2f | AppConfig.cs, ConfigManager.cs, PathHelper.cs, csproj |
| 2 | Add autoplay/loop checkboxes to control bar and wire autoplay behavior | 3c1de39 | ControlBarRenderer.cs, PreviewWindow.cs, AudioPreviewHandler.cs |

## What Was Built

**Task 1 - Config JSON Migration:**
- `AppConfig.cs`: Added 5 new fields: `Autoplay` (bool, false), `AutoplayDelayMs` (int, 500), `Loop` (bool, false), `WasapiDeviceIndex` (int, -1), `WaveformHeightPreset` (string, "Medium")
- `PathHelper.cs`: Added `GetJsonConfigPath()` returning `%LOCALAPPDATA%\Audex\config.json`
- `ConfigManager.cs`: Complete rewrite — `Load()` calls `MigrateIfNeeded()` then reads JSON; `Save()` writes JSON with `WriteIndented=true`. Private `LoadFromIni()` handles one-time INI migration. INI file preserved as backup.
- `Audex.csproj`: Added `System.Text.Json 8.0.5` package reference.

**Task 2 - Autoplay/Loop UI and Behavior:**
- `ControlBarRenderer.cs`: Added `AutoplayCheckbox` and `LoopCheckbox` to `HitZone` enum. Added checkbox rendering (`DrawCheckbox()` helper with GDI+ checkmark) on far left of transport row. Extended `Draw()` to accept `isAutoplay`/`isLoop` params with defaults. Extended `HitTest()` with expanded hit targets (+4px DPI-scaled per side).
- `PreviewWindow.cs`: Added `_isAutoplay`/`_isLoop` fields loaded from config in constructor. Passes state to `ControlBarRenderer.Draw()`. Handles `HitZone.AutoplayCheckbox`/`HitZone.LoopCheckbox` clicks via `ToggleAutoplay()`/`ToggleLoop()`. Added public `ToggleAutoplay()`, `ToggleLoop()`, `Play()` methods and `IsAutoplay`/`IsLoop` properties. Added `SavePlaybackConfig()` helper. Loop wired in `OnPlaybackEnded`: when `_isLoop` is true, seeks to 0 and calls `Play()`.
- `AudioPreviewHandler.cs`: Added `_autoplayTimer` field (System.Threading.Timer). Canceled at start of each `DoPreviewInternal()` and in `Unload()`. When `config.Autoplay` is true and format is supported, schedules `_previewWindow.Play()` after `config.AutoplayDelayMs` (default 500ms).

## Deviations from Plan

None — plan executed exactly as written.

## Verification

- `dotnet build src/Audex -c Release` succeeds with 0 warnings, 0 errors (after killing prevhost.exe DLL lock)
- Config JSON migration: MigrateIfNeeded() triggers on first Load() when config.ini exists but config.json does not
- New AppConfig defaults: Autoplay=false, AutoplayDelayMs=500, Loop=false, WasapiDeviceIndex=-1, WaveformHeightPreset="Medium"
- Control bar layout: [Auto] [Loop] checkboxes on far left | centered Play/Pause + Stop | Volume on right

## Self-Check: PASSED
