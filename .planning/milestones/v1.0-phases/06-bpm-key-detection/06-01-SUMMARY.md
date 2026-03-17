---
phase: 06-bpm-key-detection
plan: 01
subsystem: Audio Analysis Engine
tags: [bpm-detection, key-detection, bass-fx, krumhansl-schmuckler, cache, config]
dependency_graph:
  requires: []
  provides: [BpmKeyAnalyzer.Analyze, KeyDetector.DetectKeyFromChromagram, AnalysisCache.Read, AnalysisCache.Write, EnableBpmKeyDetection]
  affects: [src/Audex/Audio/, src/Audex/Config/]
tech_stack:
  added: [ManagedBass.Fx 4.0.2, bass_fx.dll x64 88KB]
  patterns: [Krumhansl-Schmuckler chromagram correlation, GCHandle.Pinned for BASS decode, binary .bka cache with LRU eviction]
key_files:
  created:
    - src/Audex/Audio/AnalysisResult.cs
    - src/Audex/Audio/KeyDetector.cs
    - src/Audex/Audio/BpmKeyAnalyzer.cs
    - src/Audex/Audio/AnalysisCache.cs
    - src/Audex/native/x64/bass_fx.dll
  modified:
    - src/Audex/Audex.csproj
    - src/Audex/Config/AppConfig.cs
    - src/Audex/Config/ConfigManager.cs
decisions:
  - "ManagedBass.Fx 4.0.2 NuGet + bass_fx.dll x64 (downloaded from un4seen.com /files/z/0/bass_fx24.zip)"
  - "BpmKeyAnalyzer uses two separate decode streams — one for BPM phase, one for key phase (shared stream causes position conflicts)"
  - "Confidence heuristics: 0.92 for 60-200 BPM range (common DJ tempo), 0.70 for extremes (45-60 or 200-230)"
  - "Chromagram bins limited to piano range 27.5-4186 Hz to exclude sub-bass and ultra-high noise"
  - "AnalysisCache uses count-based LRU eviction (2000 entries) not size-based, unlike WaveformCache"
  - "CancellationToken checked every 100 FFT frames in key phase to balance responsiveness vs overhead"
metrics:
  duration: ~5 minutes
  completed: 2026-02-17
  tasks_completed: 2
  files_created: 5
  files_modified: 3
---

# Phase 6 Plan 1: BPM/Key Analysis Engine Summary

**One-liner:** BPM detection via ManagedBass.Fx BPMDecodeGet + Krumhansl-Schmuckler chromagram key detection with binary .bka disk cache and INI config toggle.

## Objective

Built the complete backend for audio BPM and musical key detection — covering the NuGet dependency, native DLL, result model, analysis orchestrator, key detection algorithm, and disk cache.

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Add ManagedBass.Fx, bass_fx.dll, AnalysisResult, config toggle | 7875c99 | Audex.csproj, bass_fx.dll, AnalysisResult.cs, AppConfig.cs, ConfigManager.cs |
| 2 | Implement KeyDetector, BpmKeyAnalyzer, AnalysisCache | ad253ac | KeyDetector.cs, BpmKeyAnalyzer.cs, AnalysisCache.cs |

## What Was Built

### AnalysisResult.cs
Data class returned by `BpmKeyAnalyzer.Analyze()` and persisted by `AnalysisCache`. Fields: `DetectedBpm` (int?), `DetectedKey` (string?), `BpmConfidence` (0.0-1.0), `KeyConfidence` (0.0-1.0), `BpmFailed`, `KeyFailed`, `FailureReason`.

### KeyDetector.cs
Static class implementing Krumhansl-Schmuckler (1990) key detection. `DetectKeyFromChromagram(double[] chroma)` normalizes chromagram, computes Pearson correlation against all 24 major/minor profiles, returns (key, confidence). `FreqToPitchClass(double freqHz)` converts Hz to MIDI pitch class 0-11. Standard enharmonic spelling: Db (not C#), Eb (not D#), Ab (not G#), Bb (not A#).

### BpmKeyAnalyzer.cs
Static `Analyze(byte[] audioData, CancellationToken ct, Action<float> onProgress, double maxSeconds = 300.0)`:
- Pins audioData with `GCHandle.Alloc(Pinned)`
- **BPM phase (0-50%):** `BassFx.BPMDecodeGet` on first decode stream with progress callback
- Frees BPM stream, checks cancellation
- **Key phase (50-100%):** Creates fresh decode stream, reads `FFT4096` frames, accumulates chromagram (27.5-4186 Hz range), calls `KeyDetector.DetectKeyFromChromagram`
- `finally` block ensures stream freed before handle freed
- Anti-patterns avoided: no `BassFlags.FX_BPM_BKGRND`, no shared stream, GCHandle freed last

### AnalysisCache.cs
Binary `.bka` cache in `%TEMP%\Audex\analysis\`:
- `Read(string cacheKey)` — version byte validation, binary fields, LRU touch on hit
- `Write(string cacheKey, AnalysisResult result)` — writes version + bpm + key UTF-8 + confidences + ticks + fail flags
- `Delete(string cacheKey)` — removes entry for re-analyze flow
- `EvictIfNeeded()` — count-based LRU: keeps 2000 newest `.bka` files

### Config Toggle
`AppConfig.EnableBpmKeyDetection = true` (default on). Persisted in `[Analysis]` INI section via ConfigManager Load/Save.

## Verification

- `dotnet build` succeeds: **0 errors, 0 warnings**
- `bass_fx.dll` exists in `native/x64/`: **88,064 bytes** (x64, v2.4.12.6)
- All 5 new source files exist and contain required symbols
- `BassFx.BPMDecodeGet` in BpmKeyAnalyzer.cs: confirmed
- `PearsonCorrelation` in KeyDetector.cs: confirmed
- `.bka` in AnalysisCache.cs: confirmed
- `EnableBpmKeyDetection` in AppConfig.cs: confirmed

## Deviations from Plan

None - plan executed exactly as written.

The only minor note: bass_fx.dll download URL `bass_fx24-x64.zip` returned 404 (un4seen packages combined Win32/x64 in one zip). Used `bass_fx24.zip` (all platforms combined), extracted `x64/bass_fx.dll`. This is the correct DLL, just at a different URL than the plan specified.

## Self-Check

Checking created files exist:

| Item | Status |
|------|--------|
| AnalysisResult.cs | FOUND |
| KeyDetector.cs | FOUND |
| BpmKeyAnalyzer.cs | FOUND |
| AnalysisCache.cs | FOUND |
| bass_fx.dll | FOUND |
| commit 7875c99 | FOUND |
| commit ad253ac | FOUND |

## Self-Check: PASSED
