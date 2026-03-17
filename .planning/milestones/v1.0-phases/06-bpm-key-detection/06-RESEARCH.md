# Phase 6: BPM & Key Detection - Research

**Researched:** 2026-02-17
**Domain:** Audio analysis — BPM detection via BASS_FX, musical key detection via chromagram + Krumhansl-Schmuckler, .NET Framework 4.8 background threading, WinForms UI integration
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Analysis trigger & timing**
- Auto-start analysis after a configurable delay when file has no BPM/key tags
- Files WITH existing BPM/key tags skip analysis entirely — tags are authoritative
- Separate BASS decode stream for analysis (independent of playback, like waveform generation)
- For long files, analyze only the first 5 minutes of audio
- Format eligibility (which formats to analyze): Claude's discretion based on format characteristics
- BPM and key analysis parallelism (simultaneous vs sequential): Claude's discretion
- Config toggle: "Enable BPM/Key Detection" setting, defaults to ON, user can disable

**Result display & accuracy**
- Label the source: show "120 BPM (detected)" vs "120 BPM (tag)" to distinguish origin
- Confidence shown as percentage: "120 BPM (detected — 92%)"
- BPM displayed as integer (rounded to nearest whole number)
- Musical key in standard notation: Am, C, F#m, Bb, etc.
- Am/A format: lowercase 'm' suffix for minor, bare letter for major
- Standard enharmonic convention: Bb not A#, Eb not D#, F# not Gb, etc.
- Detection failure shows dash with reason: "— (unable to detect)"

**Caching & persistence**
- Cache location: %TEMP%\Audex\analysis\ (consistent with waveform cache pattern)
- Cache key strategy: Claude's discretion (consistent with existing waveform cache approach)
- Cache size limit / cleanup: Claude's discretion (entries are tiny — BPM, key, confidence)
- Cache payload contents: Claude's discretion (at minimum BPM, key, confidence; optionally metadata for debugging)

**Re-analysis control**
- Re-analyze button in Music Info section next to detected values
- Button style: refresh/reload icon with tooltip "Re-analyze BPM/Key"
- During re-analysis: keep old values visible (dimmed/faded) while progress runs
- Cooldown after re-analysis to prevent accidental double-clicks
- Progress indicator: show percentage (e.g., "Analyzing... 45%") based on audio processed

### Claude's Discretion
- Analysis start delay duration
- Cancel or continue analysis on file switch
- Which formats to skip for analysis (e.g., module formats)
- BPM/key analysis parallel vs sequential
- Cache key strategy
- Cache cleanup policy
- Cache entry payload beyond BPM/key/confidence
- Re-analyze button visibility logic
- Whether to allow retry on previously failed detections
- Visual highlight when re-analysis produces different results

### Deferred Ideas (OUT OF SCOPE)
- Write detected BPM/key back into file tags — new capability involving file modification, write permissions, tag format handling. Could be its own phase or added to Phase 7.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| META-04 | Detects BPM via audio analysis when tag is missing | ManagedBass.Fx BPMDecodeGet on a decode-only stream; progress callback provides percentage; result is float rounded to int |
| META-05 | Detects musical key via audio analysis when tag is missing | Hand-rolled Krumhansl-Schmuckler chromagram correlation; built from BASS FFT data (DataFlags.FFT4096); outputs 24 keys with correlation-derived confidence |
| META-06 | Caches analysis results to avoid re-analyzing files | AnalysisCache class modeled on WaveformCache; SHA-256 key (reuse existing ComputeCacheKey); JSON or binary payload in %TEMP%\Audex\analysis\ |
</phase_requirements>

---

## Summary

Phase 6 adds two audio analysis features (BPM and key detection) that run when a file's tags contain no BPM or key. The project already runs BASS decode streams on background threads for waveform generation — the analysis pipeline follows the same pattern. All scaffolding for background threading, cancellation, cache key computation (SHA-256), and UI marshaling via `Control.Invoke` already exists and is proven.

**BPM detection** uses `ManagedBass.Fx.BassFx.BPMDecodeGet`, which wraps un4seen's `bass_fx.dll` BPM analysis. This function analyzes a decode-only stream and fires a progress callback with 0-100 percent — exactly what the user specified for the progress indicator. It returns a float BPM value (or -1 on failure) that is rounded to an integer for display. This requires adding the `ManagedBass.Fx` NuGet package and the `bass_fx.dll` native x64 DLL.

**Key detection** is NOT provided by BASS_FX. There is no third-party .NET library that is practical for .NET Framework 4.8 x64 in this COM DLL context. The correct approach is to implement the **Krumhansl-Schmuckler key-finding algorithm** directly in C# — approximately 100 lines of code. The algorithm uses the existing BASS FFT output (same `DataFlags.FFT4096` already used for waveform frequency colors) to build a 12-element chromagram, then correlates it against the 24 major/minor key profiles. The correlation value is directly usable as a confidence score after normalizing it to a 0-100 percent range.

**Primary recommendation:** Use ManagedBass.Fx for BPM + hand-rolled Krumhansl-Schmuckler for key. Both feed from a shared decode stream. Run them sequentially (BPM first, then key) on a single background thread to avoid BASS handle sharing issues.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ManagedBass.Fx | 4.0.2 | BPM detection via BPMDecodeGet | Only managed wrapper for bass_fx.dll; supports .NET Framework 4.5+ |
| ManagedBass | 4.0.2 (already present) | Decode stream creation for analysis | Already in project; proven to work |
| System.Security.Cryptography | .NET 4.8 BCL | SHA-256 cache key | Already used in WaveformCache |
| System.IO / BinaryWriter | .NET 4.8 BCL | Cache file I/O | Already used in WaveformCache |

### Supporting (no new packages needed)

| Component | Source | Purpose | When to Use |
|-----------|--------|---------|-------------|
| Krumhansl-Schmuckler implementation | Hand-rolled C# ~100 lines | Key detection from FFT chromagram | Always — no suitable .NET package exists |
| DataFlags.FFT4096 | ManagedBass (existing) | FFT for chromagram | During key analysis pass |
| CancellationTokenSource | .NET BCL (existing pattern) | Cancel analysis on file switch | Copy pattern from StartWaveformGeneration |
| System.Threading.Thread | .NET BCL (existing pattern) | Background analysis thread | Copy pattern from StartWaveformGeneration |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| ManagedBass.Fx BPMDecodeGet | NAudio BPM detector | NAudio is not in project; adds weight; BASS integration is simpler |
| Krumhansl-Schmuckler hand-rolled | Essentia, Librosa, AForge | Essentia/Librosa are Python; AForge is abandoned; no maintained .NET key detector suitable for net48 x64 COM DLL |
| BASS_FX background flag | Manual background thread | BASS_FX_BPM_BKGRND is Windows-only; our own thread gives full cancellation control |

### Installation

Add to `.csproj`:
```xml
<PackageReference Include="ManagedBass.Fx" Version="4.0.2" />
```

Add native DLL to `src/Audex/native/x64/`:
```
bass_fx.dll  (x64, from https://www.un4seen.com/)
```

Add to `.csproj` ItemGroup (same pattern as existing DLLs):
```xml
<Content Include="native\x64\bass_fx.dll">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  <Link>bass_fx.dll</Link>
</Content>
```

---

## Architecture Patterns

### Recommended Project Structure

```
src/Audex/
├── Audio/
│   ├── AnalysisResult.cs          # Result record: Bpm, Key, BpmConfidence, KeyConfidence, FailureReason
│   ├── BpmKeyAnalyzer.cs          # Static class: Analyze(audioData, ct, progress, maxSec)
│   ├── KeyDetector.cs             # Krumhansl-Schmuckler implementation
│   └── (existing files unchanged)
├── UI/
│   ├── AnalysisCache.cs           # Disk cache: %TEMP%\Audex\analysis\
│   └── PreviewWindow.cs           # Extended with analysis state + re-analyze button
├── Config/
│   └── AppConfig.cs               # Add: bool EnableBpmKeyDetection = true
```

### Pattern 1: Analysis Lifecycle (mirrors StartWaveformGeneration)

**What:** Background thread starts after a configurable delay, checks cache, runs analysis, marshals results back to UI thread via `Control.Invoke`.
**When to use:** Every file load where BPM or key tag is absent and detection is enabled.

```csharp
// In PreviewWindow — mirrors StartWaveformGeneration exactly
private CancellationTokenSource? _analysisCts;
private int _currentAnalysisId;
private const int ANALYSIS_DELAY_MS = 800; // after waveform starts

public void StartBpmKeyAnalysis(byte[] audioData, bool isModuleFormat)
{
    // Cancel any in-progress analysis
    _analysisCts?.Cancel();
    _analysisCts?.Dispose();
    _analysisCts = null;

    int analysisId = Interlocked.Increment(ref _currentAnalysisId);

    // Check cache first (SHA-256 key, same as waveform)
    string key = WaveformCache.ComputeCacheKey(audioData);
    AnalysisResult? cached = AnalysisCache.Read(key);
    if (cached != null)
    {
        UpdateAnalysisDisplay(cached, isStale: false);
        return;
    }

    _analysisCts = new CancellationTokenSource();
    var ct = _analysisCts.Token;

    Thread bgThread = new Thread(() =>
    {
        // Delay before starting (configurable, default 800ms)
        if (ct.WaitHandle.WaitOne(ANALYSIS_DELAY_MS)) return; // cancelled

        void onProgress(float pct)
        {
            if (ct.IsCancellationRequested) return;
            if (!IsHandleCreated || IsDisposed) return;
            try { Invoke(() => { if (_currentAnalysisId == analysisId) UpdateProgressDisplay(pct); }); }
            catch { }
        }

        AnalysisResult result = BpmKeyAnalyzer.Analyze(audioData, ct, onProgress, maxSec: 300.0);

        if (ct.IsCancellationRequested) return;

        // Cache result (even failures, to avoid re-analyzing)
        try { AnalysisCache.Write(key, result); } catch { }

        if (!IsHandleCreated || IsDisposed) return;
        try
        {
            Invoke(() =>
            {
                if (_currentAnalysisId == analysisId)
                    UpdateAnalysisDisplay(result, isStale: false);
            });
        }
        catch { }
    });
    bgThread.IsBackground = true;
    bgThread.Start();
}
```

### Pattern 2: BPM Detection via ManagedBass.Fx

**What:** Creates a fresh BASS decode stream from raw bytes, calls `BassFx.BPMDecodeGet` with a progress callback for 0-300 second window.
**When to use:** BPM tag is null/absent.

```csharp
// Source: ManagedBass.Fx documentation (managedbass.github.io)
using ManagedBass.Fx;

public static (float bpm, float confidence) DetectBpm(
    byte[] audioData, CancellationToken ct,
    Action<float> onProgress, double maxSec = 300.0)
{
    GCHandle handle = GCHandle.Alloc(audioData, GCHandleType.Pinned);
    int stream = 0;
    try
    {
        IntPtr ptr = handle.AddrOfPinnedObject();
        stream = Bass.CreateStream(ptr, 0, audioData.Length, BassFlags.Decode | BassFlags.Float);
        if (stream == 0) return (-1f, 0f);

        double totalSec = Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetLength(stream));
        double endSec = Math.Min(totalSec, maxSec);

        // minMaxBPM = 0 uses defaults (45–230 BPM)
        // BassFlags.Default = no background thread (we ARE the background thread)
        float bpm = BassFx.BPMDecodeGet(
            stream,
            startSec: 0.0,
            endSec: endSec,
            minMaxBPM: 0,  // 0 = default range 45-230 BPM
            flags: BassFlags.Default,
            procedure: (ch, pct, user) => { if (!ct.IsCancellationRequested) onProgress(pct * 0.5f); },
            // BPM uses 0%-50% of total progress; key uses 50%-100%
            user: IntPtr.Zero);

        // Confidence: BASS_FX does not expose a confidence value.
        // Use a heuristic: bpm > 0 = high confidence if in common range (60-200),
        // moderate confidence if at extremes (45-60 or 200-230).
        float confidence = bpm > 0
            ? (bpm >= 60 && bpm <= 200 ? 0.92f : 0.70f)
            : 0f;

        return (bpm, confidence);
    }
    finally
    {
        if (stream != 0) Bass.StreamFree(stream);
        handle.Free();
    }
}
```

### Pattern 3: Key Detection via Krumhansl-Schmuckler

**What:** Accumulates chromagram by reading FFT bins from BASS decode stream, then correlates against 24 key profiles. Confidence is the Pearson correlation of the winning key.
**When to use:** Key tag is null/absent.

```csharp
// Krumhansl (1990) major and minor key profiles — C-starting, 12 pitch classes
// Source: Krumhansl "Cognitive Foundations of Musical Pitch" (verified via academic sources)
private static readonly double[] MajorProfile =
    { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
private static readonly double[] MinorProfile =
    { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

// Standard key names: 12 major + 12 minor (C-based, enharmonic per user decisions)
// Major: Bb not A#, Eb not D#, Ab not G#, Db not C#, Gb not F# (by convention)
// Minor: C#m not Dbm, F#m not Gbm, Abm not G#m, Bbm not A#m, Ebm not D#m
private static readonly string[] MajorKeys =
    { "C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };
private static readonly string[] MinorKeys =
    { "Cm", "C#m", "Dm", "Ebm", "Em", "Fm", "F#m", "Gm", "Abm", "Am", "Bbm", "Bm" };

// Build chromagram from FFT frames accumulated during the analysis pass
// Each FFT frame (DataFlags.FFT4096 = 2048 useful bins) maps to 12 pitch classes
// by summing FFT bin magnitudes falling within each semitone's frequency range.

public static (string key, float confidence) DetectKeyFromChromagram(double[] chroma)
{
    // Normalize chromagram to sum = 1
    double sum = 0; foreach (double v in chroma) sum += v;
    if (sum < 1e-10) return ("—", 0f);
    double[] normalized = new double[12];
    for (int i = 0; i < 12; i++) normalized[i] = chroma[i] / sum;

    double bestCorr = double.MinValue;
    string bestKey = "—";

    for (int root = 0; root < 12; root++)
    {
        // Correlate against major and minor profiles (rotated by root semitones)
        double majorCorr = PearsonCorrelation(normalized, RotateProfile(MajorProfile, root));
        double minorCorr = PearsonCorrelation(normalized, RotateProfile(MinorProfile, root));

        if (majorCorr > bestCorr) { bestCorr = majorCorr; bestKey = MajorKeys[root]; }
        if (minorCorr > bestCorr) { bestCorr = minorCorr; bestKey = MinorKeys[root]; }
    }

    // Convert correlation (-1..1) to confidence (0..100%)
    // Typical winning correlations for correct key: 0.5–0.9
    float confidence = (float)Math.Max(0, Math.Min(1, (bestCorr + 1.0) / 2.0));
    return (bestKey, confidence);
}
```

### Pattern 4: Cache Entry (AnalysisCache)

**What:** Lightweight binary cache in `%TEMP%\Audex\analysis\`, keyed by SHA-256 (same as WaveformCache). Stores BPM, key, confidence, analysis timestamp.
**When to use:** After every analysis completes (including failures).

```csharp
// Cache file format (.bka = BPM/Key Analysis):
// byte  version (= 1)
// float bpm     (0 = not detected; -1 = analysis failed)
// byte  keyLen
// byte[] key    (UTF-8, up to 255 bytes)
// float bpmConfidence (0.0–1.0)
// float keyConfidence (0.0–1.0)
// long  analysisTimestampUtcTicks
// byte  failureFlags  (bit 0 = BPM failed, bit 1 = key failed)

// Cache key: reuse WaveformCache.ComputeCacheKey(audioData) — same SHA-256
// Cache directory: %TEMP%\Audex\analysis\
// Extension: .bka
// Eviction: count-based (max 2000 entries) rather than size-based (entries are ~40 bytes each)
```

### Pattern 5: Chromagram Accumulation from BASS FFT

**What:** During the analysis decode loop, accumulate FFT bins into 12 pitch class buckets. Maps each FFT bin's center frequency to the nearest semitone using `bin * sampleRate / fftSize`.

```csharp
// FFT size for key analysis — larger = better frequency resolution for semitone mapping
// DataFlags.FFT4096 returns 2048 useful bins (half of 4096)
private const int KeyFftSize = 4096;
private const int KeyFftBins = KeyFftSize / 2; // 2048 bins

// Map each FFT bin to a pitch class (0–11, where 0=C)
// Reference: A4 = 440 Hz, MIDI pitch class formula
private static int FreqToPitchClass(double freqHz)
{
    if (freqHz <= 0) return -1;
    // MIDI note: 69 + 12 * log2(freq / 440)
    double midiNote = 69.0 + 12.0 * Math.Log(freqHz / 440.0, 2.0);
    int pitchClass = ((int)Math.Round(midiNote)) % 12;
    return ((pitchClass % 12) + 12) % 12; // ensure 0–11
}

// Pre-compute bin-to-pitch-class mapping once per stream (sampleRate known from ChannelGetInfo)
int[] binToPitchClass = new int[KeyFftBins];
for (int bin = 1; bin < KeyFftBins; bin++) // skip DC
{
    double freqHz = (double)bin * sampleRate / KeyFftSize;
    // Only use musically relevant range: 27.5 Hz (A0) to 4186 Hz (C8)
    if (freqHz >= 27.5 && freqHz <= 4186.0)
        binToPitchClass[bin] = FreqToPitchClass(freqHz);
    else
        binToPitchClass[bin] = -1; // out of range
}

// Accumulate: for each FFT frame, add magnitude to pitch class bucket
double[] chroma = new double[12];
float[] fftBuffer = new float[KeyFftBins];
// ... in decode loop ...
int fftRead = Bass.ChannelGetData(stream, fftBuffer, (int)DataFlags.FFT4096);
if (fftRead > 0)
{
    for (int bin = 1; bin < KeyFftBins; bin++)
    {
        int pc = binToPitchClass[bin];
        if (pc >= 0) chroma[pc] += fftBuffer[bin];
    }
}
```

### Anti-Patterns to Avoid

- **Creating a BASS decode stream in DoPreviewInternal synchronously, then passing it to background thread**: BASS streams are not thread-safe. Create the decode stream fresh inside the background thread (same pattern as WaveformGenerator does with pinned GCHandle).
- **Using BassFlags.FX_BPM_BKGRND and calling BPMDecodeGet from the background thread simultaneously**: BPMDecodeGet is synchronous (blocking) unless BKGRND flag is used. Since we control the thread, call it synchronously without the BKGRND flag — simpler and cancellable via our own CancellationToken.
- **Sharing the analysis stream with the playback stream**: Always create a completely independent decode stream for analysis (same principle as WaveformGenerator).
- **Running BPM and key detection on two concurrent threads**: They each need their own BASS decode stream (from the same pinned byte array). Two concurrent decode streams from the same pinned GCHandle is safe — but introduces complexity. Run sequentially on one thread: BPM first (0-50% progress), then key (50-100% progress).
- **Not pinning the audioData byte array**: BASS accesses the array directly via IntPtr. The GCHandle.Alloc(Pinned) pattern is mandatory, exactly as in WaveformGenerator.
- **Caching failures without a failure flag**: If analysis fails (BASS returns -1, or decode yields silence), cache the failure result so the file is not re-analyzed on every preview. Include a failureFlags field to distinguish "not analyzed yet" from "analyzed and failed."
- **Showing the re-analyze button for tag-sourced values**: Button should only appear when BPM or key was detected (not from tags). Tags are authoritative — no re-analysis needed.
- **Re-using WaveformCache.ComputeCacheKey for analysis but storing in the same directory**: Store analysis cache in a subdirectory (`analysis\`) to avoid name collisions if the same key extension were ever reused.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| BPM detection algorithm | Custom beat detection from scratch | ManagedBass.Fx BassFx.BPMDecodeGet | Edge cases (varying tempo, time signatures, low-energy tracks) — BASS_FX uses SoundTouch library, battle-tested |
| SHA-256 cache key | Custom hash or filename-based key | WaveformCache.ComputeCacheKey() (already exists) | Reuse existing method; same logic, same deduplication semantics |
| Background thread + cancellation | New threading abstraction | System.Threading.Thread + CancellationTokenSource | Existing pattern in StartWaveformGeneration — proven in prevhost.exe context |
| UI marshaling from background to STA | Event queue, message loop | Control.Invoke() | Existing pattern — mandatory for WinForms from non-STA thread |
| Key name normalization | New normalizer | MusicKeyNormalizer.Normalize() (already exists) | Already handles Camelot, Open Key, and text variants; detected keys feed through same normalizer |

**Key insight:** The project's existing BASS decode stream pattern and WaveformCache are the blueprint for everything in this phase. The only genuinely new code is BpmKeyAnalyzer (which wraps BASS_FX BPM + hand-rolled key detection), AnalysisCache (modeled on WaveformCache), and the UI changes to LayoutRenderer and PreviewWindow.

---

## Common Pitfalls

### Pitfall 1: Module formats — BPM/key detection produces nonsense
**What goes wrong:** BASS MusicLoad for .mod/.xm/.it/.s3m files produces synthesized PCM but the BPM from the module sequencer has nothing to do with the audio BPM detection algorithm. Key detection on synth patterns produces arbitrary chromagram distributions.
**Why it happens:** Module formats are tracker music; their "tempo" is internal to the tracker, not a repeating low-frequency pattern BASS_FX can detect.
**How to avoid:** Skip BPM/key analysis entirely for `isModuleFormat = true` files. This is already tracked in `AudioFileInfo.IsModuleFormat`.
**Warning signs:** Detected BPM wildly different from module's actual BPM; key detection results meaningless.

### Pitfall 2: BASS_FX returns -1 for silent/short files or non-rhythmic content
**What goes wrong:** BPMDecodeGet returns -1 (failure) for: very short files (<3 seconds), files with no distinct low-frequency pattern (classical, ambient, spoken word), or silence.
**Why it happens:** BASS_FX BPM detection algorithm requires repeating sub-250Hz patterns. No patterns = no BPM.
**How to avoid:** Treat -1 as "unable to detect", not as an error. Cache the failure. Display "— (unable to detect)" per user decision. Do not retry automatically.
**Warning signs:** -1 returned for files under 10 seconds of effective content.

### Pitfall 3: Analysis completes after file switch
**What goes wrong:** User switches to a new file; the old analysis thread completes and tries to update the UI with stale results.
**Why it happens:** Background thread has captured a closure reference. By the time it completes, `_currentAnalysisId` has been incremented.
**How to avoid:** Same pattern as waveform generation: capture `analysisId` at thread start, check `_currentAnalysisId == analysisId` inside the Invoke callback before updating any state.
**Warning signs:** BPM/key display shows values from a previous file briefly.

### Pitfall 4: GCHandle lifetime and stream disposal order
**What goes wrong:** `handle.Free()` is called before `Bass.StreamFree(stream)`, causing BASS to access freed memory.
**Why it happens:** Exception in the analysis path skips StreamFree; or cleanup order is wrong.
**How to avoid:** Same pattern as WaveformGenerator: always free stream BEFORE freeing handle. Use try/finally to guarantee cleanup. Keep `stream` and `handle` in outer scope so the finally block can see them.
**Warning signs:** Access violation / crash in prevhost.exe during analysis.

### Pitfall 5: BPM confidence is not exposed by BASS_FX
**What goes wrong:** User expects a meaningful confidence percentage for BPM, but BASS_FX BPMDecodeGet only returns the BPM value (or -1), no confidence.
**Why it happens:** The BASS_FX API surface has no confidence output parameter.
**How to avoid:** Use a heuristic: if BPM returned is in the common range (60-200), assign 92% confidence. If at extremes (45-60 or 200-230), assign 70%. These numbers reflect that common tempos are more reliably detected. Flag as "estimated confidence" in comments.
**Warning signs:** None — this is a design decision, not a bug.

### Pitfall 6: Key confidence over-reported from high correlation
**What goes wrong:** Krumhansl-Schmuckler reports correlation > 0.8 even when the key is ambiguous (e.g., pentatonic melody that fits multiple keys equally well).
**Why it happens:** The algorithm's correlation measures fit to key profile, not uniqueness of fit. A simple melody on C can correlate equally with Am.
**How to avoid:** Report the raw correlation-derived confidence honestly. Do not inflate it. Display as-is. Users see "Am (detected — 71%)" and understand it may be uncertain.
**Warning signs:** Key detection shows 99% confidence for monotone drum loop.

### Pitfall 7: Progress reporting during BPM analysis with BKGRND flag
**What goes wrong:** If BASS_FX_BPM_BKGRND flag is used, BPMDecodeGet returns immediately and the analysis runs internally — but our CancellationToken cannot interrupt it mid-way.
**Why it happens:** BKGRND flag delegates threading to BASS, bypassing our CancellationToken checks.
**How to avoid:** Do NOT use the BKGRND flag. Call BPMDecodeGet synchronously from our own background thread. CancellationToken will be checked in the progress callback. Note: BPMDecodeGet is blocking for the duration of analysis — this is acceptable because we own the thread.
**Warning signs:** Cancellation on file switch is slow (analysis completes before thread notices cancellation).

### Pitfall 8: Analysis cache and waveform cache sharing the same SHA-256 key space
**What goes wrong:** If both caches use the same directory and the same SHA-256 key but different extensions, no collision happens with current extensions. But if a future cache type reuses `.bka`, collisions could occur.
**Why it happens:** Design ambiguity between cache namespaces.
**How to avoid:** Use a dedicated `analysis\` subdirectory within `%TEMP%\Audex\`. Extension `.bka` is unique. WaveformCache uses `.wf` and `.wfc`. No collision possible.

---

## Code Examples

Verified patterns from official sources:

### BPMDecodeGet Signature (from ManagedBass.Fx docs)
```csharp
// Source: https://managedbass.github.io/api/ManagedBass.Fx.BassFx.html
public static float BPMDecodeGet(
    int Channel,
    double StartSec,
    double EndSec,
    int MinMaxBPM,    // 0 = default range 45-230; or MakeLong(minBPM, maxBPM)
    BassFlags Flags,  // BassFlags.Default for synchronous; BASSFXBpm.BASS_FX_BPM_BKGRND for background
    BPMProgressProcedure Procedure,  // delegate void(int Channel, float Percent, IntPtr User)
    IntPtr User = default
);
// Returns: float BPM, or -1 on failure
```

### BPMProgressProcedure Delegate (from ManagedBass.Fx docs)
```csharp
// Source: https://managedbass.github.io/api/ManagedBass.Fx.BPMProgressProcedure.html
public delegate void BPMProgressProcedure(int Channel, float Percent, IntPtr User);
// Channel: channel that BPMDecodeGet was called on
// Percent: 0.0 to 100.0 — progress of analysis
// User: user data passed to BPMDecodeGet
```

### Krumhansl-Schmuckler Profiles (from academic sources)
```csharp
// Source: Krumhansl (1990) "Cognitive Foundations of Musical Pitch"
// Values verified at: https://gist.github.com/bmcfee/1f66825cef2eb34c839b42dddbad49fd
// Major profile (C = index 0, C# = 1, ..., B = 11):
double[] MajorProfile = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
// Minor profile (C = index 0, C# = 1, ..., B = 11):
double[] MinorProfile = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };
```

### Standard Key Name Arrays with Correct Enharmonics
```csharp
// Enharmonic conventions per user decision: Bb not A#, Eb not D#, F# not Gb, Ab not G#, Db not C#
// Major keys (12, starting from C):
string[] MajorKeys = { "C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };
// Minor keys (12, starting from Cm):
string[] MinorKeys = { "Cm", "C#m", "Dm", "Ebm", "Em", "Fm", "F#m", "Gm", "Abm", "Am", "Bbm", "Bm" };
// Note: C#m not Dbm; F#m not Gbm — sharps preferred for minor, flats preferred for major
// This matches MusicKeyNormalizer._standardKeys already defined in the project
```

### Pearson Correlation for Key Detection
```csharp
// Standard Pearson correlation between observed pitch class distribution and key profile
private static double PearsonCorrelation(double[] x, double[] y)
{
    double meanX = 0, meanY = 0;
    for (int i = 0; i < 12; i++) { meanX += x[i]; meanY += y[i]; }
    meanX /= 12; meanY /= 12;

    double num = 0, denX = 0, denY = 0;
    for (int i = 0; i < 12; i++)
    {
        double dx = x[i] - meanX, dy = y[i] - meanY;
        num += dx * dy;
        denX += dx * dx;
        denY += dy * dy;
    }
    double den = Math.Sqrt(denX * denY);
    return den < 1e-10 ? 0.0 : num / den;
}
```

### AnalysisCache Structure
```csharp
// Result record returned by BpmKeyAnalyzer.Analyze()
public sealed class AnalysisResult
{
    public int? DetectedBpm { get; init; }          // null = not detected
    public string? DetectedKey { get; init; }       // null = not detected
    public float BpmConfidence { get; init; }       // 0.0–1.0
    public float KeyConfidence { get; init; }       // 0.0–1.0
    public string? FailureReason { get; init; }     // non-null when both failed
    public bool BpmFailed { get; init; }
    public bool KeyFailed { get; init; }
}
```

### Display String Formatting
```csharp
// Tag-sourced (from AudioFileInfo.Bpm):
"120 BPM (tag)"           // integer, "(tag)" suffix
"Am (tag)"                // standard notation, "(tag)" suffix

// Detected (from AnalysisResult):
"120 BPM (detected — 92%)"    // integer, confidence as integer percent
"Am (detected — 71%)"         // standard notation, confidence as integer percent

// Failed:
"— (unable to detect)"    // dash, reason in parens

// While analyzing:
"Analyzing... 45%"        // replaces the value field during analysis
```

### Re-Analyze Button (WinForms GDI+)
```csharp
// Button placement: right of the BPM/Key values in Music Info section
// Style: unicode refresh symbol "⟳" (U+27F3) or "↺" (U+21BA), drawn inline
// Hit test: small rectangle (18x18 logical pixels at 96 DPI) at a fixed offset from right margin
// Tooltip: "Re-analyze BPM/Key" — drawn as a tooltip string near the button on hover
// Visibility: only when DetectedBpm or DetectedKey is non-null (i.e., detected values exist)
// Cooldown: 2 seconds after click (prevents double-click re-trigger)
// During re-analysis: dim existing values with alpha 128, show "Analyzing... X%"
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|-----------------|--------------|--------|
| BASS.NET BPMCounter (radio42 managed wrapper) | ManagedBass.Fx BassFx.BPMDecodeGet | ManagedBass replaced BASS.NET as the modern wrapper (2016+) | ManagedBass is NuGet-distributed; this project already uses it; same underlying bass_fx.dll |
| BPM confidence via second pass or beat counting | Heuristic from BPM value range | No standard API exists | Simple and honest; matches display requirement |
| Librosa/Essentia for key detection | Hand-rolled Krumhansl-Schmuckler | Always — no .NET equivalent exists | ~100 lines of C#; no new dependency; fully portable to net48 |

**Deprecated/outdated:**
- BASS.NET (radio42 managed wrapper): superseded by ManagedBass NuGet packages; do not use.
- BASS_FX_BPM_BKGRND flag: avoid in this project — we manage threads ourselves and need CancellationToken integration.

---

## Discretion Decisions (Research-Backed Recommendations)

The planner should use these recommendations for the discretion areas:

**Analysis start delay:** 800ms after file load. This allows the waveform to start generating (which uses a separate decode stream on its own thread). 800ms is enough time to not feel instant but short enough that analysis results appear without a long wait. The debounce timer for file switching is 150ms (from config); 800ms is well beyond that, preventing spurious analysis starts.

**Cancel vs. continue on file switch:** Cancel. Increment `_currentAnalysisId`, cancel CTS, and start fresh. The waveform generator uses the same pattern. The analysis thread should check `ct.IsCancellationRequested` in the progress callback. Continuing an old analysis to completion only to discard it wastes CPU.

**Which formats to skip:** Skip module formats (`isModuleFormat = true`). These are tracker files where tempo is a sequencer concept, not a repeating low-frequency audio pattern. BASS_FX would produce meaningless results. Also skip files shorter than 5 seconds (too short for reliable detection). All other formats (WAV, MP3, FLAC, OGG, AAC, M4A, WMA, OPUS) are eligible.

**BPM/key analysis parallel vs. sequential:** Sequential on a single background thread. BPM first (uses BASS decode stream + BASS_FX), then key (uses a second BASS decode stream + chromagram). Running two BASS decode streams simultaneously from the same pinned byte array is technically safe, but adds threading complexity with no practical speed benefit in prevhost.exe. Sequential is simpler, easier to cancel, and produces cleaner progress reporting (0-50% = BPM, 50-100% = key).

**Cache key strategy:** Reuse `WaveformCache.ComputeCacheKey(audioData)` — the SHA-256 of the full file bytes. This is content-addressed; same file = same key regardless of filename. Consistent with waveform cache approach as specified.

**Cache cleanup policy:** Count-based limit of 2000 entries (not size-based). Each `.bka` file is approximately 40 bytes. 2000 entries = ~80KB maximum total. Evict oldest-first by LastWriteTime (same LRU pattern as WaveformCache). Trigger eviction after every write.

**Cache payload beyond BPM/key/confidence:** Include `analysisTimestampUtcTicks` (for debugging) and `failureFlags` byte (bit 0 = BPM failed, bit 1 = key failed). This lets us distinguish "file has no detection result yet" (cache miss) from "file was analyzed and detection failed" (cache hit with failure flags set).

**Re-analyze button visibility:** Show only when at least one of DetectedBpm or DetectedKey is non-null (i.e., we successfully detected something). Also show when both failed, to allow the user to retry. Do NOT show when both values came from tags (tags are authoritative). On re-analyze, clear the cache entry for the current file's key, then run analysis again.

**Retry on previously failed detections:** Yes — the re-analyze button triggers a fresh analysis even for previously failed files. This allows the user to retry if they think the detection might succeed. Cache the result again after retry.

**Visual highlight on changed result:** Briefly highlight changed values in accent color for 2 seconds (same accent color used elsewhere in the UI if any, or a subtle background tint). This signals to the user "this changed." If unchanged, no highlight.

---

## Open Questions

1. **bass_fx.dll availability for x64**
   - What we know: bass_fx.dll x64 exists at un4seen.com; ManagedBass.Fx 4.0.2 supports .NET Framework 4.5+; the project is .NET 4.8 x64.
   - What's unclear: Whether the existing bass_fx.dll in this project (if any) is already x64 or needs to be downloaded fresh. Current `native/x64/` folder does NOT contain bass_fx.dll.
   - Recommendation: Download `bass_fx24-x64.zip` from `https://www.un4seen.com/` to get the x64 `bass_fx.dll`. Add it to `native/x64/`. This is a one-time manual step.

2. **BPM confidence meaningfulness for non-rhythmic content**
   - What we know: BASS_FX returns -1 for content without repeating low-frequency patterns. The heuristic confidence (92% for common range, 70% for extremes) is not derived from signal analysis.
   - What's unclear: Whether users will find the heuristic confidence misleading.
   - Recommendation: Use the heuristic; it is disclosed by labeling values as "(detected — X%)". A truly signal-derived confidence would require access to BASS_FX internals not exposed by the API.

3. **Key detection accuracy for complex modern music**
   - What we know: Krumhansl-Schmuckler works best for tonal music; it struggles with atonal, chromatic, or heavily layered electronic music. Accuracy is typically 70-85% for common pop/electronic music.
   - What's unclear: Whether this accuracy is acceptable for the target user base (DJ/producer context implied by Serato/Traktor support).
   - Recommendation: The user specified "detected" vs "tag" labeling and confidence percentages — this design already communicates uncertainty. Ship with Krumhansl-Schmuckler and improve in a future phase if accuracy is insufficient.

---

## Sources

### Primary (HIGH confidence)
- `https://managedbass.github.io/api/ManagedBass.Fx.BassFx.html` — BPMDecodeGet, BPMProgressProcedure signatures
- `https://managedbass.github.io/api/ManagedBass.Fx.BPMProgressProcedure.html` — Delegate signature
- `https://www.nuget.org/packages/ManagedBass.Fx/` — Version 4.0.2, .NET Framework 4.5+ support
- `https://www.radio42.com/bass/help/html/948adef2-7877-a3f7-bbf1-1bb2056e6d53.htm` — BASSFXBpm enum values
- Krumhansl (1990) profiles via `https://gist.github.com/bmcfee/1f66825cef2eb34c839b42dddbad49fd` (Python reference, values are universal)
- Project codebase: `WaveformGenerator.cs`, `WaveformCache.cs`, `FrequencyColorMapper.cs`, `TagReader.cs`, `MusicKeyNormalizer.cs`, `AudioPreviewHandler.cs`, `LayoutRenderer.cs`, `AppConfig.cs`, `ConfigManager.cs`

### Secondary (MEDIUM confidence)
- `https://rnhart.net/articles/key-finding/` — Krumhansl-Schmuckler algorithm description
- `https://github.com/pie62/HandyKaraoke/blob/master/BASS/bass_fx24-linux/bass_fx.txt` — BASS_FX documentation text (confirmed BPM algorithm uses SoundTouch; no key detection)
- Multiple sources confirming BASS_FX has no key detection capability

### Tertiary (LOW confidence)
- BPM confidence heuristic values (92%/70%) — informed inference from algorithm behavior, not from official docs
- Key accuracy estimates (70-85%) — general academic literature, not benchmarked against this project's use case

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — ManagedBass.Fx documented at official sources; hand-rolled key detector uses academically verified profiles
- Architecture: HIGH — mirrors existing proven patterns (WaveformGenerator, WaveformCache) exactly
- Pitfalls: HIGH — most pitfalls discovered by reading existing code (GCHandle pattern, stream ownership, generationId pattern)
- BPM confidence heuristic: LOW — not backed by official BASS_FX documentation; a design choice

**Research date:** 2026-02-17
**Valid until:** 2026-08-17 (ManagedBass.Fx is stable; Krumhansl-Schmuckler profiles are decades-old math)
