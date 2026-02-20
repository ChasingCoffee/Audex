---
phase: 06-bpm-key-detection
verified: 2026-02-17T00:00:00Z
status: passed
score: 16/16 must-haves verified
re_verification: false
---

# Phase 6: BPM & Key Detection Verification Report

**Phase Goal:** BPM and musical key are detected via audio analysis when tags are missing
**Verified:** 2026-02-17
**Status:** PASSED
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

#### Plan 01 Truths (Analysis Engine)

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 1 | BPM detection runs via ManagedBass.Fx BPMDecodeGet on a decode-only stream and returns integer BPM with confidence | VERIFIED | `BpmKeyAnalyzer.cs:65` — `BassFx.BPMDecodeGet(stream, 0.0, endSec, 0, BassFlags.Default, bpmCallback, IntPtr.Zero)` with confidence heuristic (0.92 / 0.70) |
| 2 | Key detection runs via Krumhansl-Schmuckler chromagram correlation and returns standard notation key with confidence | VERIFIED | `KeyDetector.cs` — `PearsonCorrelation`, `MajorProfile`, `MinorProfile`, `DetectKeyFromChromagram` all present and substantive |
| 3 | Analysis results are cached to disk in binary .bka files so repeated file previews skip re-analysis | VERIFIED | `AnalysisCache.cs` — `Read`, `Write`, `Delete`, `EvictIfNeeded`, `CacheExtension = ".bka"`, `MaxEntries = 2000` |
| 4 | Analysis can be cancelled via CancellationToken without resource leaks | VERIFIED | `BpmKeyAnalyzer.cs` — `try/finally` at lines 191-201 ensures stream freed before GCHandle.Free(); CancellationToken checked in BPM callback and every 100 FFT frames in key phase |
| 5 | Module formats and short files (<5s) are skipped — analysis is not attempted | VERIFIED | `PreviewWindow.cs:455` — `if ((hasBpmTag && hasKeyTag) \|\| isModuleFormat \|\| duration < 5.0) return;` |
| 6 | Config toggle EnableBpmKeyDetection exists and defaults to ON | VERIFIED | `AppConfig.cs:60` — `public bool EnableBpmKeyDetection { get; set; } = true;`; ConfigManager Load/Save reads/writes `[Analysis]` INI section |

#### Plan 02 Truths (UI Wiring)

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 7 | User sees BPM value when file has no BPM tag — analysis runs automatically after 800ms delay | VERIFIED | `PreviewWindow.cs` — `ANALYSIS_DELAY_MS = 800`, delay via `ct.WaitHandle.WaitOne(ANALYSIS_DELAY_MS)`; `StartBpmKeyAnalysis` called from `AudioPreviewHandler.DoPreviewInternal()` |
| 8 | User sees musical key when file has no key tag — analysis runs automatically | VERIFIED | Same `StartBpmKeyAnalysis` pipeline; key phase (50-100% progress) produces `DetectedKey` displayed in LayoutRenderer |
| 9 | Values are labeled: "120 BPM (tag)" vs "120 BPM (detected -- 92%)" to distinguish origin | VERIFIED | `LayoutRenderer.cs:398` — `$"{info.Bpm.Value} BPM (tag)"` and `LayoutRenderer.cs:402` — `$"{analysisResult.DetectedBpm} BPM (detected \u2014 {(int)(analysisResult.BpmConfidence * 100)}%)"` |
| 10 | Progress shows actual percentage: "Analyzing... 45%" while analysis runs | VERIFIED | `LayoutRenderer.cs:323` — `keyDisplay = $"Analyzing... {progressPct}%"` and BPM row same pattern; progress batched at 2% threshold in PreviewWindow |
| 11 | Analysis results are cached — re-opening the same file shows results instantly from cache | VERIFIED | `PreviewWindow.cs:467` — `AnalysisCache.Read(cacheKey)` checked before starting background thread; instant display on hit |
| 12 | User can click re-analyze button to force fresh analysis with cooldown | VERIFIED | `PreviewWindow.cs:844-864` — `HitTestReanalyze`, 2s cooldown check, `AnalysisCache.Delete`, `_isReanalyzing = true`, `StartBpmKeyAnalysis` re-called |
| 13 | Module formats and files < 5 seconds are not analyzed | VERIFIED | Same as Truth 5 — enforced in `StartBpmKeyAnalysis` |
| 14 | File switch cancels in-progress analysis without resource leaks | VERIFIED | `AudioPreviewHandler.Unload()` — `CancelBpmKeyAnalysis()` called first; `UpdateContent()` resets all analysis state; `CancellationTokenSource` disposed on cancel |
| 15 | Detection failure shows dash with reason: "-- (unable to detect)" | VERIFIED | `LayoutRenderer.cs:347-348` — `keyDisplay = "\u2014 (unable to detect)"` when `analysisResult?.KeyFailed == true`; same for BPM at lines 404-407 |
| 16 | Re-analysis dims old values and shows progress; restores on completion | VERIFIED | `LayoutRenderer.cs:326-335` — `isReanalyzing` path draws old detected values at `Color.FromArgb(128, textColor)` and shows separate "Analyzing... X%" row below Key |

**Score:** 16/16 truths verified

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/Audex/Audio/AnalysisResult.cs` | Result data class with DetectedBpm, DetectedKey, confidences, failure flags | VERIFIED | 29-line file; all 7 required properties present (DetectedBpm, DetectedKey, BpmConfidence, KeyConfidence, FailureReason, BpmFailed, KeyFailed) |
| `src/Audex/Audio/KeyDetector.cs` | Krumhansl-Schmuckler key detection from chromagram | VERIFIED | 132-line substantive implementation; MajorProfile, MinorProfile, PearsonCorrelation, RotateProfile, DetectKeyFromChromagram, FreqToPitchClass all present |
| `src/Audex/Audio/BpmKeyAnalyzer.cs` | Static Analyze method running BPM then key sequentially with progress callback | VERIFIED | 204-line implementation; BassFx.BPMDecodeGet call, two separate decode streams, GCHandle.Pinned, try/finally resource safety, CancellationToken throughout |
| `src/Audex/Audio/AnalysisCache.cs` | Binary disk cache in %TEMP%\Audex\analysis\ | VERIFIED | 194-line implementation; Read, Write, Delete, EvictIfNeeded; .bka extension; MaxEntries=2000; version byte; binary format with failFlags |
| `src/Audex/Config/AppConfig.cs` | EnableBpmKeyDetection config toggle | VERIFIED | `public bool EnableBpmKeyDetection { get; set; } = true;` at line 60 |
| `src/Audex/native/x64/bass_fx.dll` | Native x64 bass_fx DLL | VERIFIED | Exists; 88,064 bytes; confirmed x64 |
| `src/Audex/Audex.csproj` | ManagedBass.Fx 4.0.2 NuGet + bass_fx.dll Content item | VERIFIED | `<PackageReference Include="ManagedBass.Fx" Version="4.0.2" />` at line 24; bass_fx.dll Content item at line 63-66 |
| `src/Audex/UI/LayoutRenderer.cs` | Music Info section with detected/tag labels, confidence, progress, re-analyze button | VERIFIED | DrawMusicInfoSection fully extended; HitTestReanalyze static method; _reanalyzeButtonBounds field; DrawReanalyzeButton with GDI+ arc; DrawTooltip |
| `src/Audex/UI/PreviewWindow.cs` | Analysis lifecycle: start, cancel, progress callback, cache check, re-analyze | VERIFIED | StartBpmKeyAnalysis, CancelBpmKeyAnalysis methods present; all analysis state fields; re-analyze flow in OnMouseDown; hover in OnMouseMove; Dispose calls CancelBpmKeyAnalysis |
| `src/Audex/PreviewHandler/AudioPreviewHandler.cs` | Analysis trigger after waveform generation start | VERIFIED | Lines 396-404: StartBpmKeyAnalysis called in DoPreviewInternal(); lines 478-485: CancelBpmKeyAnalysis called in Unload() |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| BpmKeyAnalyzer.cs | ManagedBass.Fx.BassFx | BPMDecodeGet call | WIRED | `BassFx.BPMDecodeGet(stream, 0.0, endSec, 0, BassFlags.Default, bpmCallback, IntPtr.Zero)` at line 65 |
| BpmKeyAnalyzer.cs | KeyDetector.cs | DetectKeyFromChromagram call | WIRED | `var (key, keyConf) = KeyDetector.DetectKeyFromChromagram(chroma)` at line 173 |
| AnalysisCache.cs | WaveformCache.cs | Reuses ComputeCacheKey for SHA-256 content hash | WIRED (indirect) | Pattern not in AnalysisCache.cs directly — by design, the caller (PreviewWindow.cs:463) computes `WaveformCache.ComputeCacheKey(audioData)` and passes the key string in. The same SHA-256 hash is reused; the implementation choice avoids a dependency from the Audio layer to the UI layer. Not a gap. |
| AudioPreviewHandler.cs | PreviewWindow.cs | InvokeOnUI(() => _previewWindow.StartBpmKeyAnalysis(...)) | WIRED | Lines 396-404 in DoPreviewInternal(); pattern `StartBpmKeyAnalysis` confirmed |
| PreviewWindow.cs | BpmKeyAnalyzer.cs | BpmKeyAnalyzer.Analyze() called from background thread | WIRED | `AnalysisResult? result = BpmKeyAnalyzer.Analyze(audioDataRef, ct, onProgress, 300.0)` at line 522 |
| PreviewWindow.cs | AnalysisCache.cs | Cache read before analysis, write after completion | WIRED | `AnalysisCache.Read(cacheKey)` at line 467; `AnalysisCache.Write(capturedCacheKey, result)` at line 527; `AnalysisCache.Delete(_currentCacheKey)` at line 853 |
| LayoutRenderer.cs | AnalysisResult.cs | Receives AnalysisResult to format display strings | WIRED | `AnalysisResult?` parameter in Render() and DrawMusicInfoSection(); analysisResult?.DetectedBpm, analysisResult?.DetectedKey, analysisResult?.BpmConfidence, analysisResult?.KeyConfidence accessed throughout |

---

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|---------|
| META-04 | 06-01-PLAN, 06-02-PLAN | Detects BPM via audio analysis when tag is missing | SATISFIED | BpmKeyAnalyzer.Analyze() calls BassFx.BPMDecodeGet; result surfaced in LayoutRenderer as "X BPM (detected — Y%)"; triggered from AudioPreviewHandler when hasBpmTag=false |
| META-05 | 06-01-PLAN, 06-02-PLAN | Detects musical key via audio analysis when tag is missing | SATISFIED | KeyDetector.DetectKeyFromChromagram(chroma) via Krumhansl-Schmuckler; result surfaced in LayoutRenderer as "X (detected — Y%)"; triggered when hasKeyTag=false |
| META-06 | 06-01-PLAN, 06-02-PLAN | Caches analysis results to avoid re-analyzing files | SATISFIED | AnalysisCache.Read() checked before analysis; AnalysisCache.Write() called on completion (including failure results); AnalysisCache.Delete() for re-analyze flow; .bka binary files in %TEMP%\Audex\analysis\ with 2000-entry LRU eviction |

**Orphaned requirements check:** No additional requirements mapped to Phase 6 in REQUIREMENTS.md beyond META-04, META-05, META-06.

---

### Anti-Patterns Found

| File | Pattern | Severity | Disposition |
|------|---------|----------|-------------|
| BpmKeyAnalyzer.cs | `return null;` (lines 74, 137) | Info | Legitimate: returns null when CancellationToken triggered (caller handles null as cancellation signal) |
| AnalysisCache.cs | `return null;` (lines 50, 62, 96) | Info | Legitimate: null returned for cache miss (file not found), version mismatch, or read error |

No blockers found. No placeholder implementations. No empty handlers. No static returns masking missing queries.

---

### Human Verification Required

#### 1. BPM/Key Display in Explorer Preview Pane

**Test:** Open Windows Explorer preview pane. Select an MP3 or FLAC file without BPM/key tags. Wait 1-2 seconds.
**Expected:** Music Info section shows "Analyzing... X%" for both BPM and Key rows, then transitions to "X BPM (detected — 92%)" and "Am (detected — 71%)" (or similar values with confidence percentages).
**Why human:** Live COM shell extension behavior in prevhost.exe — cannot verify programmatically.

#### 2. Progress Animation

**Test:** Select a large audio file (10+ minutes, no BPM/key tags). Watch the progress percentage increment in real-time.
**Expected:** "Analyzing... X%" increments from 0% to 50% (BPM phase), then 50% to 100% (key phase).
**Why human:** UI animation behavior during live analysis requires observation.

#### 3. Cache Hit on Re-Open

**Test:** Select a file without tags, wait for analysis to complete. Close and re-open the same file.
**Expected:** BPM and key values appear instantly (no "Analyzing..." phase) with the same values and confidence percentages.
**Why human:** Requires observing timing behavior in live shell extension.

#### 4. Re-Analyze Button Interaction

**Test:** After values are detected, locate the re-analyze button (circular arrow icon) to the right of the "Music Info" header. Click it.
**Expected:** Old values dim to 50% opacity, "Analyzing... X%" appears below the Key row, then fresh detected values appear.
**Why human:** Visual appearance and interaction timing requires live testing.

#### 5. File Switch Cancels Analysis

**Test:** Select a file without tags. Immediately select a different file while "Analyzing..." is shown.
**Expected:** Analysis stops cleanly (no zombie prevhost.exe threads consuming CPU), new file's analysis state initializes fresh.
**Why human:** Background thread cancellation requires process-level observation.

---

### Gaps Summary

No gaps. All 16 must-have truths are verified in the actual codebase. All artifacts exist with substantive implementations. All key links are wired. Requirements META-04, META-05, and META-06 are satisfied by the implementation. No placeholder code or empty stubs detected.

The only notable deviation from plan: the `AnalysisCache → WaveformCache.ComputeCacheKey` key link is indirect — the key computation is done by `PreviewWindow` (the caller) rather than inside `AnalysisCache`. This is a sound architectural decision (avoids an Audio layer dependency on the UI layer) and the functional intent is fully preserved. Not treated as a gap.

---

_Verified: 2026-02-17_
_Verifier: Claude (gsd-verifier)_
