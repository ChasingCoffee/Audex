---
phase: 04-frequency-colored-waveform
plan: 01
subsystem: audio-analysis
tags: [fft, frequency-colors, waveform, cache, config]
dependency_graph:
  requires: [03-02-SUMMARY.md]
  provides: [FrequencyColorMapper, WaveformData, WaveformCache.wfc, AppConfig.WaveformColorMode]
  affects: [PreviewWindow.cs (caller updated to WaveformData)]
tech_stack:
  added: []
  patterns: [FFT-interleaved-decode, power-weighted-band-rms, versioned-binary-cache]
key_files:
  created:
    - src/Audex/Audio/FrequencyColorMapper.cs
  modified:
    - src/Audex/Audio/WaveformGenerator.cs
    - src/Audex/UI/WaveformCache.cs
    - src/Audex/UI/PreviewWindow.cs
    - src/Audex/Config/AppConfig.cs
    - src/Audex/Config/ConfigManager.cs
decisions:
  - FrequencyColorMapper uses power-weighted RMS (sum of squared magnitudes) for band energy to emphasize strong frequency peaks over noise
  - FFT bin boundaries computed at runtime (not hardcoded) to handle 44100/48000/22050 Hz sample rates
  - Colors array pre-allocated at targetBars size and trimmed after loop to keep peaks and colors in sync
  - ThemeHelper.IsSystemInDarkMode() called once before decode loop and cached (not per-bar) to avoid repeated registry reads
  - WaveformData return type replaces bare float[] to carry both peaks and colors from a single Generate call
  - Color cache stores RGB only (no alpha); alpha applied at render time based on played state
metrics:
  duration: 3m
  completed: 2026-02-17
  tasks_completed: 2
  files_modified: 6
---

# Phase 04 Plan 01: Frequency Analysis Backend Summary

**One-liner:** FFT-to-color backend with DJ-standard crossovers (200 Hz/2500 Hz), heat spectrum palette, versioned .wfc cache, and WaveformColorMode config toggle.

## What Was Built

### FrequencyColorMapper.cs (new)

Static class that converts FFT magnitude data into per-bar colors using:
- `FreqToBin()` — standard DSP formula, runtime-computed for any sample rate
- `BandRms()` — power-weighted RMS (sum-of-squares / count, sqrt) per frequency band
- `Compute()` — blends bass/mids/highs into a single Color using heat spectrum palette:
  - Bass (20–200 Hz): red/warm
  - Mids (200–2500 Hz): yellow-green
  - Highs (2500–16000 Hz): blue/cool
  - Dark/light theme variants (dark full-brightness, light ~75% for contrast)
  - Below EnergyThreshold (0.008): neutral gray (60,60,65 dark / 180,180,185 light)
- `NeutralColor()` — threshold-based neutral gray
- `SmoothColors()` — 3-tap moving average (temp array, avoids cascading)

### WaveformGenerator.cs (extended)

- Return type changed from `float[]?` to `WaveformData?`
- `WaveformData` class: `Peaks float[]` + `FrequencyColors Color[]?`
- After each bar's PCM accumulation, `Bass.ChannelGetData(..., DataFlags.FFT2048)` called interleaved
- FFT buffer pre-allocated as `float[1024]` (FftWindowSize/2) once before loop
- `ThemeHelper.IsSystemInDarkMode()` called once before loop and cached
- `FrequencyColorMapper.SmoothColors()` applied after loop completes
- Colors array trimmed to match peaks length for array-parallel safety

### WaveformCache.cs (extended)

- `ReadColorCache(string key) -> Color[]?`: reads versioned .wfc binary file
  - Version byte check (delete + return null on mismatch)
  - Sanity checks on count (> 0, < 1,000,000)
  - Touches LastWriteTime on success for LRU
- `WriteColorCache(string key, Color[] colors)`: writes versioned .wfc binary file
  - Format: [version:1 byte] [count:int32] [R,G,B per color: 3 bytes each]
  - No alpha stored (applied at render time)
  - Calls EvictIfNeeded after write
- `EvictIfNeeded()` updated to collect both `.wf` and `.wfc` files under combined 50 MB limit

### AppConfig.cs (extended)

- `WaveformColorMode bool` added, defaults to `true` (colored on first use)

### ConfigManager.cs (extended)

- `Load()`: reads `[Waveform]` section → `ColorMode` key → `bool.TryParse` → `config.WaveformColorMode`
- `Save()`: creates `[Waveform]` section if absent, writes `ColorMode = true/false`

### PreviewWindow.cs (updated — Rule 3: blocking fix)

Updated caller to use `WaveformData`:
- `WaveformData? result = WaveformGenerator.Generate(...)`
- Extracts `result.Peaks` for the existing cache write and UI update logic
- `FrequencyColors` not yet consumed here (Plan 02 wires up the rendering)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] PreviewWindow.cs updated to use WaveformData return type**
- **Found during:** Task 1
- **Issue:** Changing WaveformGenerator.Generate to return WaveformData would break the existing caller in PreviewWindow.cs (line 299: `float[]? result = WaveformGenerator.Generate(...)`)
- **Fix:** Updated PreviewWindow.cs to use `WaveformData? result` and extract `result.Peaks` for existing cache write and UI update. FrequencyColors not yet consumed (Plan 02).
- **Files modified:** src/Audex/UI/PreviewWindow.cs
- **Commit:** f6fe54e

## Self-Check: PASSED

All files confirmed on disk. Both task commits verified in git log.

| Item | Status |
|------|--------|
| FrequencyColorMapper.cs | FOUND |
| WaveformGenerator.cs | FOUND |
| WaveformCache.cs | FOUND |
| AppConfig.cs | FOUND |
| ConfigManager.cs | FOUND |
| 04-01-SUMMARY.md | FOUND |
| commit f6fe54e (Task 1) | FOUND |
| commit ea53b40 (Task 2) | FOUND |
