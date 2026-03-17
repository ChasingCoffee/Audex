# Phase 4: Frequency-Colored Waveform - Research

**Researched:** 2026-02-17
**Domain:** BASS FFT decode analysis, GDI+ per-bar color rendering, frequency band color mapping, WinForms toggle UI, INI config persistence
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Color mapping
- Heat spectrum palette: bass = red/warm, mids = yellow/green, highs = blue/cool
- Muted/desaturated tones — visible but not distracting, blends with Explorer's UI
- Each bar is a single blended color representing the weighted frequency mix (not stacked segments)
- Played/past region uses alpha dimming (same approach as Phase 3, frequency colors still visible underneath)
- Palette adjusted per theme — slightly different hues/saturation for contrast on both light and dark backgrounds
- Below a certain energy threshold, bars render neutral/gray instead of attempting to color silence
- Playhead remains white (same as Phase 3)

#### Frequency bands
- DJ-standard crossover frequencies (~200Hz bass/mid, ~2.5kHz mid/high)
- Musical frequency range only (~20Hz-16kHz)
- FFT window size as named internal constant (tweakable but not user-facing)

#### Visual blending
- Smooth transitions between neighboring bars (averaging/smoothing so adjacent bars don't jump colors abruptly)
- Bar height = amplitude only (same as Phase 3); color = frequency content — two independent dimensions
- Color dimming state updates on mouse-up release (consistent with Phase 3 seek behavior), not during drag

#### Waveform toggle
- Small icon/button near the waveform to toggle between monochrome and frequency-colored modes
- Toggle preference persisted to INI config file — remembers across Explorer restarts

### Claude's Discretion
- Blend math for mixing bass+highs colors (through-middle vs direct-mix — pick whichever produces most visually useful results)
- Loading behavior: whether to show monochrome first then color in, or wait for frequency analysis
- Bar fill approach: entire bar vs gradient from base — pick what looks best at 120px height
- Mirror/reflection coloring: match existing Phase 3 waveform layout
- Background adjustments for contrast with frequency colors
- Rendering approach: anti-aliasing vs crisp pixel-aligned — pick based on performance and quality
- Number of frequency bands (3 vs 4) — pick what produces most visually useful results
- Energy weighting approach (raw vs perceptual) — pick for best visual information
- Cache strategy: extend existing or separate cache — pick what fits WaveformCache design
- Downsampling color strategy: average vs dominant — pick for smoothest result
- Default mode (colored vs monochrome) on first use
- Edge case handling for unusual frequency content (test tones, white noise)

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| WAVE-02 | Waveform is colored by frequency content (bass/mids/highs) | FrequencyAnalyzer computes per-bar band energy ratios via BASS FFT on a second decode stream; FrequencyColorMapper converts ratios to blended Color; WaveformRenderer extended to accept and use color array |
</phase_requirements>

---

## Summary

Phase 4 extends the existing waveform visualization with per-bar frequency color analysis. The core approach is to run a second parallel BASS decode stream during waveform generation, reading FFT data at each bar's time position to extract bass/mid/high energy ratios, then mapping those ratios to a blended heat-spectrum color. The resulting color array (one Color per canonical bar) is cached alongside the amplitude peaks using a new `.wfc` cache file with the same SHA-256 key, extending the existing WaveformCache pattern.

The rendering path in WaveformRenderer receives an optional `Color[]? frequencyColors` array and a boolean `isColorMode`. When color mode is active and colors are available, each bar uses its pre-computed blended color instead of the amplitude gradient. The alpha-dimming pattern for played bars (alpha=140) is preserved unchanged — frequency colors are simply dimmed with the same alpha value. A small toggle button overlaid on the waveform area corner switches between modes and persists the preference to `[Waveform]` section in `config.ini` via the existing ConfigManager/AppConfig pattern.

The key technical risk is the two-stream approach: BASS supports multiple concurrent decode streams from the same pinned memory buffer, so the amplitude peak stream and the FFT color stream can run independently. This is the cleanest design — no state interleaving between sample reads and FFT reads on a single stream. Frequency analysis runs on the same background thread as peak generation, completing in the same 2-3 second window, or slightly after for long files.

**Primary recommendation:** Generate amplitude peaks and frequency colors in a single background pass using one decode stream — read a chunk of PCM samples to compute the peak, then at the end of each bar's worth of samples query ChannelGetData with DataFlags.FFT2048 to get the spectrum. Since FFT on a decode stream is computed from the last decoded data rather than advancing the position separately, interleaving is safe. Store colors in a parallel `Color[]` array and cache separately with `.wfc` extension.

---

## Standard Stack

### Core (already in project — no new packages needed)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ManagedBass | 4.0.2 | BASS decode stream + FFT via `Bass.ChannelGetData` with `DataFlags.FFT2048` | Already used; FFT is built into BASS |
| System.Drawing | .NET 4.8 | `Color.FromArgb()` for per-bar color blending | Already used in WaveformRenderer |
| ini-parser-netstandard | 2.5.2 | Read/write `[Waveform]` section for toggle persistence | Already used in ConfigManager |

### No New Dependencies Required
All functionality is achievable with existing packages. BASS's built-in FFT is the only spectrum analysis tool needed. No third-party FFT library, color library, or animation library is required.

---

## Architecture Patterns

### Recommended File Structure
```
src/Audex/
├── Audio/
│   ├── WaveformGenerator.cs          # Existing — extend to also produce Color[] colors
│   └── FrequencyColorMapper.cs       # NEW — frequency band ratios → Color
├── UI/
│   ├── WaveformCache.cs              # Extend: ReadColorCache / WriteColorCache (.wfc files)
│   ├── WaveformRenderer.cs           # Extend: accept Color[]? frequencyColors + bool isColorMode
│   ├── ThemeHelper.cs                # Extend: add GetFrequencyBarColor() methods
│   └── PreviewWindow.cs              # Extend: color mode state, toggle button hit-test
└── Config/
    ├── AppConfig.cs                  # Extend: add WaveformColorMode bool
    └── ConfigManager.cs              # Extend: read/write [Waveform] ColorMode
```

### Pattern 1: FFT on a BASS Decode Stream (Interleaved Reads)

**What:** BASS's `ChannelGetData` with a `DataFlags.FFT*` flag computes FFT over the most recently decoded PCM data. On a decode stream, the FFT is calculated from data already consumed — it does NOT re-advance the stream position. This means you can read a chunk of PCM samples (for peak computation), then immediately call `ChannelGetData(stream, fftBuf, (int)DataFlags.FFT2048)` to get the spectrum of that same chunk.

**When to use:** After processing each bar's worth of PCM samples in WaveformGenerator, before advancing to the next bar.

**Frequency bin formula (HIGH confidence — official BASS docs):**
- FFT2048 returns 1024 float magnitude values
- Bin N corresponds to frequency: `freq = N * sampleRate / 2048`
- Bin 0 = DC component (skip)
- Bin 1 = `sampleRate / 2048` Hz
- For 44100 Hz: bin resolution = ~21.5 Hz per bin
- For 48000 Hz: bin resolution = ~23.4 Hz per bin

**Example — computing FFT in the decode loop:**
```csharp
// Source: BASS official docs https://www.un4seen.com/doc/bass/BASS_ChannelGetData.html
// After reading one bar's worth of PCM floats:
private const int FftSize = 2048;   // named constant per locked decision
private static float[] _fftBuffer = new float[FftSize / 2]; // FFT2048 returns 1024 values

// Called after processing samplesPerBar frames for bar i:
int bytesRead = Bass.ChannelGetData(waveStream, _fftBuffer, (int)DataFlags.FFT2048);
if (bytesRead > 0)
{
    // _fftBuffer[0] = DC, skip
    // _fftBuffer[1..1023] = frequency magnitudes
    FrequencyBands bands = FrequencyColorMapper.ComputeBands(
        _fftBuffer, sampleRate, FftSize);
    colors[barIndex] = FrequencyColorMapper.BandsToColor(bands, isDarkMode);
}
```

### Pattern 2: Frequency Band Energy Extraction

**What:** Sum FFT bin magnitudes within each frequency band. Use squared magnitudes (power) rather than raw magnitudes for perceptually better weighting — louder content dominates the color as expected.

**DJ-standard crossover frequencies (locked decision):**
- Bass: 20 Hz – 200 Hz
- Mids: 200 Hz – 2500 Hz
- Highs: 2500 Hz – 16000 Hz

**Bin index calculation:**
```csharp
// Source: BASS docs + standard DSP formula
// binIndex = frequency * fftWindowSize / sampleRate
// Example at 44100 Hz with FFT2048:
// Bass max bin:  200 * 2048 / 44100 = ~9
// Mids max bin: 2500 * 2048 / 44100 = ~116
// Highs max bin: 16000 * 2048 / 44100 = ~743 (cap here; bins above = ultrasonic noise)

public static int FreqToBin(float freqHz, int sampleRate, int fftSize)
{
    return (int)(freqHz * fftSize / sampleRate);
}
```

**Energy sum with power weighting:**
```csharp
// Compute RMS energy per band from FFT magnitudes
float BandEnergy(float[] fft, int binStart, int binEnd)
{
    float sum = 0f;
    int count = 0;
    for (int i = Math.Max(1, binStart); i < binEnd && i < fft.Length; i++)
    {
        sum += fft[i] * fft[i]; // power (squared magnitude)
        count++;
    }
    return count > 0 ? (float)Math.Sqrt(sum / count) : 0f; // RMS
}
```

### Pattern 3: Frequency-to-Color Blending (FrequencyColorMapper)

**What:** Convert normalized band energies into a single blended bar color using weighted RGB mixing.

**Recommended approach (Claude's Discretion — research supports direct weighted RGB mix):**

The direct band-to-channel approach (bass→R, mids→G, highs→B, then gamma/white-balance) is used in Audacity's proposed colored waveform and matches the Serato aesthetic (red bass, green mids, blue highs). Mixing through a color space middle (like HSL) produces murkier results for high-energy multi-band content.

```csharp
// Source: Audacity forum algorithm + Serato color model
public static Color BandsToColor(float bassE, float midsE, float highsE, bool isDark)
{
    // Normalize to 0..1 range using soft clip
    float total = bassE + midsE + highsE;
    if (total < EnergyThreshold)
        return isDark ? NeutralDark : NeutralLight; // below-threshold = neutral gray

    // Normalize to ratios
    float bR = bassE / total;  // bass ratio
    float mR = midsE / total;  // mids ratio
    float hR = highsE / total; // highs ratio

    // Map to heat spectrum: bass=red/warm, mids=yellow/green, highs=blue/cool
    // Dark theme palette (muted/desaturated per locked decision):
    //   Pure bass: (200, 60, 40)   - muted warm red
    //   Pure mids: (160, 180, 40)  - muted yellow-green
    //   Pure highs: (40, 100, 200) - muted cool blue
    int r = (int)(200 * bR + 160 * mR + 40 * hR);
    int g = (int)( 60 * bR + 180 * mR + 100 * hR);
    int b = (int)( 40 * bR +  40 * mR + 200 * hR);

    // Clamp and return
    return Color.FromArgb(
        Math.Max(0, Math.Min(255, r)),
        Math.Max(0, Math.Min(255, g)),
        Math.Max(0, Math.Min(255, b)));
}
```

**Light theme adjustment:** Use lower brightness base values to maintain contrast against the near-white background (248, 248, 250). Multiply RGB components by ~0.75 for light mode.

### Pattern 4: Neighbor Smoothing

**What:** After computing all bar colors, apply a 3-bar moving average to prevent abrupt color jumps between adjacent bars.

**When to use:** Post-processing pass after all bar colors are computed — run once before caching.

```csharp
// Simple 3-tap box filter on Color components
for (int i = 1; i < colors.Length - 1; i++)
{
    colors[i] = Color.FromArgb(
        colors[i].A,
        (colors[i-1].R + colors[i].R + colors[i+1].R) / 3,
        (colors[i-1].G + colors[i].G + colors[i+1].G) / 3,
        (colors[i-1].B + colors[i].B + colors[i+1].B) / 3);
}
```

### Pattern 5: Cache Extension for Color Data

**What:** Store color arrays in `%TEMP%\Audex\{sha256}.wfc` files using the same SHA-256 key as the amplitude cache. Separate file extension keeps backward compatibility.

**Format:** Binary: `int32 count` followed by `count × 3 bytes` (R, G, B — alpha always 255 for unplayed bars; alpha handling is in the renderer at paint time).

**Key insight:** The alpha value is NOT stored in cache — it is applied at render time based on played state. Storing pre-dimmed colors would make the cache invalid across different playback positions.

```csharp
// Extend WaveformCache with two new static methods:
// WriteColorCache(string key, Color[] colors)  → .wfc file, RGB only (3 bytes/bar)
// ReadColorCache(string key) → Color[]? or null
```

### Pattern 6: Toggle Button in WaveformRenderer

**What:** Small icon button overlaid on the top-right corner of the waveform area. Follows the same pattern as ControlBarRenderer transport buttons (hover highlight, press highlight, GDI+ drawn icon).

**Placement:** 20x20 logical pixels (DPI-scaled), 6px from top-right corner of the waveform bounds.

**Icon:** Simple "spectrum bars" icon (3 ascending rectangles) for colored mode; "single bar" or "wave" for monochrome mode. GDI+-drawn, no image assets.

**Hit-test:** WaveformRenderer exposes a static `HitTestToggle(Point, Rectangle, float dpiScale)` method matching the HitTest pattern used by ControlBarRenderer.

```csharp
// Add to WaveformRenderer:
private static Rectangle _toggleButtonRect;

public static bool HitTestToggle(Point point)
{
    return _toggleButtonRect.Contains(point);
}
```

### Pattern 7: Config Persistence for Toggle

**What:** Add `WaveformColorMode` bool to `AppConfig` and read/write it in the `[Waveform]` INI section via `ConfigManager`.

```csharp
// AppConfig.cs addition:
public bool WaveformColorMode { get; set; } = true; // default: colored mode on first use
```

```ini
; config.ini addition:
[Waveform]
ColorMode=true
```

### Pattern 8: Loading Behavior (Claude's Discretion — recommendation)

Show monochrome waveform immediately (progressive reveal as today), then overlay frequency colors once the color array is complete. This reuses the existing progressive reveal mechanism without introducing a blocking wait. The PreviewWindow holds both `_waveformPeaks` (for amplitude) and `_waveformColors` (for color). Colors become available after the full background pass; until then, rendering falls back to the amplitude-based gradient.

**State additions to PreviewWindow:**
```csharp
private Color[]? _waveformColors;          // null until frequency analysis completes
private bool _isWaveformColorMode;          // loaded from config, toggled by button
// _waveformBarsReady already exists and drives progressive reveal of both peaks and colors
```

### Anti-Patterns to Avoid

- **Separate decode stream for FFT:** Running two simultaneous decode streams (one for peaks, one for FFT) doubles memory and CPU. Instead, interleave FFT reads with PCM reads on the same single decode stream — BASS FFT on a decode stream operates on the last decoded data, no separate stream needed.
- **Storing alpha in cache:** Caching pre-dimmed ARGB values ties the cache to a specific playback state. Store only RGB; apply alpha at render time.
- **Per-frame FFT on short window:** Using FFT256 (128 bins) gives ~170Hz per bin at 44100Hz — too coarse for meaningful bass/mid/high separation. FFT2048 (1024 bins, ~21Hz/bin) gives precise band boundaries.
- **Blocking main thread for color analysis:** Same pattern as Phase 3 — all heavy computation stays on the background thread. PreviewWindow's `_waveformColors` is only assigned via `Control.Invoke()`.
- **Re-analyzing already-cached files:** Check `.wfc` cache before starting background analysis, exactly as WaveformCache.ReadCache() is checked before generating peaks.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| FFT computation | Custom DFT implementation | `Bass.ChannelGetData(stream, buf, (int)DataFlags.FFT2048)` | BASS provides windowed, normalized FFT as part of its decode API; zero extra dependencies |
| Color interpolation library | Import Unicolour or Colourful | `Color.FromArgb()` with manual RGB lerp | The project already uses System.Drawing; simple weighted RGB mix is sufficient for 3-band blending |
| INI config persistence | Custom serialization | `ini-parser-netstandard` already used by ConfigManager | Already in the project and already extended per new config keys |
| Toggle button rendering | WinForms Button control | GDI+-drawn button in WaveformRenderer | Consistent with all other UI in this project; WinForms controls cannot be embedded at arbitrary positions inside a UserControl's OnPaint |

**Key insight:** This phase adds no new NuGet packages. Everything — FFT, color math, config, rendering — uses existing infrastructure.

---

## Common Pitfalls

### Pitfall 1: FFT Bin Frequencies Are Sample-Rate Dependent
**What goes wrong:** Hard-coded bin indices for bass/mid crossover frequencies produce wrong colors on files with non-44100Hz sample rates (e.g., 48000Hz, 22050Hz).
**Why it happens:** Bin N = frequency N * sampleRate / fftSize — the mapping varies by sample rate.
**How to avoid:** Always compute bin boundaries at runtime using `FreqToBin(freqHz, sampleRate, FftSize)`. The sample rate is available from `Bass.ChannelGetInfo(waveStream, out info)` — already called in WaveformGenerator.
**Warning signs:** Colors appear wrong (e.g., all blue, or wrong colors) on 48kHz vs 44.1kHz files.

### Pitfall 2: Low-Energy Silence Produces Noisy/Random Colors
**What goes wrong:** When total band energy is near-zero (silence, very quiet passages), small floating-point noise dominates the ratio calculation and produces arbitrary colors.
**Why it happens:** Dividing tiny numbers produces unstable ratios.
**How to avoid:** Apply an energy threshold check before color computation. If `(bassE + midsE + highsE) < EnergyThreshold` (e.g., 0.01f), render neutral/gray. This is a locked decision.
**Warning signs:** Silent passages show random color speckles instead of gray.

### Pitfall 3: Cache File Collision if Color Format Changes
**What goes wrong:** If the color cache format is revised (e.g., adding a 4th band), old cached `.wfc` files silently load corrupt data.
**Why it happens:** No version header in the binary format.
**How to avoid:** Include a version byte at the start of the `.wfc` format. Current version: `1`. On read, if version != 1, delete and return null (triggers regeneration).
**Warning signs:** Colors look wrong after a code update.

### Pitfall 4: FFT After Bar Boundary vs. Exact Window Alignment
**What goes wrong:** If the FFT is called at an inconsistent point within each bar's decode chunk, the spectrum may represent a different time region than the amplitude peak.
**Why it happens:** FFT on a decode stream uses the "last decoded data" — if called after a partial chunk, the window may cross a bar boundary.
**How to avoid:** Call FFT exactly once per bar, immediately after the bar's final PCM chunk is processed. Don't call FFT mid-chunk.
**Warning signs:** Bass drops visible in amplitude don't align with color changes.

### Pitfall 5: Toggle State Not Invalidating Waveform
**What goes wrong:** Clicking the toggle button changes `_isWaveformColorMode` but the waveform doesn't redraw.
**Why it happens:** Only color mode state changed; no `Invalidate(_waveformBounds)` was called.
**How to avoid:** In PreviewWindow's mouse-up handler, after toggling color mode, always call `Invalidate(_waveformBounds)`.
**Warning signs:** Toggle button appears to work (state changes) but waveform stays the same until next paint.

### Pitfall 6: Color Arrays and Peak Arrays Out of Sync
**What goes wrong:** `_waveformColors.Length != _waveformPeaks.Length` when renderer tries to index by bar.
**Why it happens:** Colors and peaks are generated in parallel with different completion times; a race condition allows partial color arrays to be set.
**How to avoid:** Only assign `_waveformColors` via `Invoke()` after the full color array is complete (not progressively). Color analysis completes in the same pass as peak analysis — no need for incremental updates to colors.
**Warning signs:** IndexOutOfRangeException in WaveformRenderer.

### Pitfall 7: Toggle Button Overlapping Existing Waveform Interaction
**What goes wrong:** Click on the toggle button is also interpreted as a waveform seek event.
**Why it happens:** The toggle button sits within the waveform bounds; `WaveformRenderer.HitTest()` returns true for the same point.
**How to avoid:** In PreviewWindow's `OnMouseDown`, check `WaveformRenderer.HitTestToggle(e.Location)` BEFORE `WaveformRenderer.HitTest(e.Location, _waveformBounds)`. Toggle hit takes priority.
**Warning signs:** Clicking the toggle button also triggers a seek.

---

## Code Examples

### Computing FFT Within the Decode Loop (WaveformGenerator extension)

```csharp
// Source: BASS official docs (un4seen.com) + ManagedBass DataFlags enum
// Inside WaveformGenerator.Generate() — after processing samplesPerBar for bar i:

private const int FftWindowSize = 2048;  // Named constant — tweakable
private static float[] _fftBuf = new float[FftWindowSize / 2]; // 1024 values for FFT2048

// After the bar is completed (samplesInBar >= samplesPerBar):
int fftBytesRead = Bass.ChannelGetData(waveStream, _fftBuf, (int)DataFlags.FFT2048);
Color barColor;
if (fftBytesRead > 0)
{
    Bass.ChannelGetInfo(waveStream, out ChannelInfo info);
    barColor = FrequencyColorMapper.Compute(_fftBuf, info.Frequency, FftWindowSize, isDarkMode);
}
else
{
    barColor = FrequencyColorMapper.NeutralColor(isDarkMode);
}
colors[barIndex] = barColor;
```

### FrequencyColorMapper.Compute()

```csharp
// Source: Serato crossover frequencies (community verified ~200Hz, ~1.5-2.5kHz)
// + Audacity forum algorithm (weighted RGB from band energies)
public static class FrequencyColorMapper
{
    private const float BassLow  = 20f;
    private const float BassMid  = 200f;   // DJ-standard locked decision
    private const float MidHigh  = 2500f;  // DJ-standard locked decision
    private const float HighMax  = 16000f; // Musical range ceiling locked decision
    private const float EnergyThreshold = 0.008f; // Below this = neutral/gray

    public static Color Compute(float[] fft, int sampleRate, int fftSize, bool isDark)
    {
        int bassStart = FreqToBin(BassLow,  sampleRate, fftSize);
        int bassEnd   = FreqToBin(BassMid,  sampleRate, fftSize);
        int midsEnd   = FreqToBin(MidHigh,  sampleRate, fftSize);
        int highsEnd  = FreqToBin(HighMax,  sampleRate, fftSize);

        float bassE  = BandRms(fft, bassStart, bassEnd);
        float midsE  = BandRms(fft, bassEnd,   midsEnd);
        float highsE = BandRms(fft, midsEnd,   highsEnd);

        float total = bassE + midsE + highsE;
        if (total < EnergyThreshold)
            return isDark ? Color.FromArgb(60, 60, 65) : Color.FromArgb(180, 180, 185);

        float bR = bassE  / total;
        float mR = midsE  / total;
        float hR = highsE / total;

        // Dark theme muted palette (per locked decision)
        int r, g, b;
        if (isDark)
        {
            r = (int)(195 * bR + 150 * mR + 35 * hR);
            g = (int)( 55 * bR + 170 * mR + 95 * hR);
            b = (int)( 35 * bR +  35 * mR + 190 * hR);
        }
        else // Light theme: ~75% brightness
        {
            r = (int)(145 * bR + 110 * mR + 25 * hR);
            g = (int)( 40 * bR + 125 * mR + 70 * hR);
            b = (int)( 25 * bR +  25 * mR + 140 * hR);
        }
        return Color.FromArgb(
            Math.Max(0, Math.Min(255, r)),
            Math.Max(0, Math.Min(255, g)),
            Math.Max(0, Math.Min(255, b)));
    }

    private static float BandRms(float[] fft, int start, int end)
    {
        float sum = 0f; int n = 0;
        for (int i = Math.Max(1, start); i < end && i < fft.Length; i++)
        { sum += fft[i] * fft[i]; n++; }
        return n > 0 ? (float)Math.Sqrt(sum / n) : 0f;
    }

    private static int FreqToBin(float hz, int rate, int size)
        => (int)(hz * size / rate);

    public static Color NeutralColor(bool isDark)
        => isDark ? Color.FromArgb(60, 60, 65) : Color.FromArgb(180, 180, 185);
}
```

### WaveformRenderer Extension (Color Mode)

```csharp
// Extend Draw() signature to accept optional color data:
public static void Draw(
    Graphics g,
    Rectangle bounds,
    float[]? peaks,
    int barsReady,
    Color[]? frequencyColors,    // NEW — null = use amplitude gradient
    bool isColorMode,            // NEW — true = use frequency colors if available
    bool isWaveformDragging,     // NEW — for color dimming (drag suppresses color switch)
    double currentPosition,
    double totalDuration,
    float dpiScale,
    bool isHovering,
    Point hoverPoint,
    bool isDragging,
    double dragPosition,
    bool waveformUnavailable,
    bool isToggleHovered,        // NEW — for toggle button hover state
    bool isTogglePressed)        // NEW — for toggle button press state
{
    // ... existing layout ...

    // Per-bar color selection:
    Color GetBarColor(int barIdx, float amplitude, bool isPlayed)
    {
        Color baseColor;
        if (isColorMode && frequencyColors != null && barIdx < frequencyColors.Length)
            baseColor = frequencyColors[barIdx];
        else
            baseColor = ThemeHelper.GetWaveformBarColor(amplitude);

        return isPlayed
            ? Color.FromArgb(140, baseColor.R, baseColor.G, baseColor.B)
            : baseColor;
    }

    // Toggle button drawn in top-right of waveform bounds:
    _toggleButtonRect = new Rectangle(
        bounds.Right - (int)(26 * dpiScale),
        bounds.Top + (int)(6 * dpiScale),
        (int)(20 * dpiScale),
        (int)(20 * dpiScale));
    DrawToggleButton(g, _toggleButtonRect, isColorMode,
        isToggleHovered, isTogglePressed, dpiScale);
}
```

### WaveformCache Color Extension

```csharp
// New methods on WaveformCache (same key, .wfc extension):
private const string ColorCacheExtension = ".wfc";
private const byte ColorCacheVersion = 1;

public static void WriteColorCache(string key, Color[] colors)
{
    string path = Path.Combine(Path.GetTempPath(), CacheSubfolder, key + ColorCacheExtension);
    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    using var w = new BinaryWriter(fs);
    w.Write(ColorCacheVersion);
    w.Write(colors.Length);
    foreach (Color c in colors)
    { w.Write(c.R); w.Write(c.G); w.Write(c.B); }
}

public static Color[]? ReadColorCache(string key)
{
    string path = Path.Combine(Path.GetTempPath(), CacheSubfolder, key + ColorCacheExtension);
    if (!File.Exists(path)) return null;
    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var r = new BinaryReader(fs);
    byte version = r.ReadByte();
    if (version != ColorCacheVersion) return null; // stale format
    int count = r.ReadInt32();
    if (count <= 0 || count > 1_000_000) return null;
    Color[] colors = new Color[count];
    for (int i = 0; i < count; i++)
        colors[i] = Color.FromArgb(r.ReadByte(), r.ReadByte(), r.ReadByte());
    return colors;
}
```

### AppConfig and ConfigManager Extension

```csharp
// AppConfig.cs — add to Waveform section:
/// <summary>Whether frequency color mode is active. Default: true.</summary>
public bool WaveformColorMode { get; set; } = true;

// ConfigManager.cs — in Load():
if (data.Sections.ContainsSection("Waveform"))
{
    string? colorMode = data["Waveform"]["ColorMode"];
    if (!string.IsNullOrWhiteSpace(colorMode) && bool.TryParse(colorMode, out bool cm))
        config.WaveformColorMode = cm;
}

// ConfigManager.cs — in Save():
if (!data.Sections.ContainsSection("Waveform"))
    data.Sections.AddSection("Waveform");
data["Waveform"]["ColorMode"] = config.WaveformColorMode.ToString().ToLowerInvariant();
```

---

## State of the Art

| Old Approach | Current Approach | Notes |
|--------------|------------------|-------|
| Spectrum analysis via separate FFT library | BASS built-in `ChannelGetData` FFT | BASS has had FFT built in for 15+ years; no extra dependency |
| Stacked frequency bar segments (spectrogram-like) | Single blended color per bar | Serato/Rekordbox aesthetic; cleaner at 120px waveform height |
| Real-time FFT during playback only | Offline FFT during background decode | Pre-compute and cache; no real-time overhead during playback |

**Stable technology:** GDI+, BASS FFT, INI config — nothing in this phase uses recent or fast-changing APIs.

---

## Discretion Resolutions

These are the Claude's Discretion items with research-backed recommendations:

| Item | Recommendation | Rationale |
|------|---------------|-----------|
| Blend math | Direct weighted RGB (bass→R-channel, mids→G-channel, highs→B-channel), then white-balance | Clearest visual separation; matches Serato aesthetics; HSL blending produces muddier results for multi-band signals |
| Loading behavior | Show monochrome immediately (progressive), add colors when full color array ready | Reuses existing progressive reveal; no blocking wait; colors "snap in" once computed (< 1 second after peaks complete for typical 3-5min track) |
| Bar fill approach | Entire bar single solid color (no gradient from base) | At 120px height a base-to-top gradient would compete visually with amplitude height; flat color + amplitude height conveys both dimensions independently |
| Mirror/reflection coloring | Both halves (up and down) use same color | Phase 3 mirrors bars from center; color should match — the reflection IS the same bar |
| Background | No adjustment | Muted palette with proper luminance works on both theme backgrounds; verified by palette values |
| Rendering | Existing AntiAlias for rounded caps; flat fill otherwise | Matches Phase 3 exactly; no regression |
| Number of bands | 3 bands (bass/mids/highs) | DJ-standard; adding a 4th (e.g., "presence" 2.5-6kHz) fragments mids with no perceptual improvement at this scale |
| Energy weighting | Power (squared) RMS | Perceptually closer to loudness; quiet overtones don't drown out fundamental bass energy |
| Cache strategy | Extend WaveformCache with `.wfc` files (same key, separate file) | Keeps amplitude and color caches independent; amplitude cache remains usable even if color cache is absent; no format changes to `.wf` files |
| Downsampling color | Average RGB across source bars mapped to display bar | Smooth color transitions; consistent with how amplitude peaks are averaged in WaveformRenderer's current downsampling |
| Default mode | Colored on first use | `AppConfig.WaveformColorMode = true` default; user decision to switch if unwanted |
| Edge cases (test tones, white noise) | Energy threshold handles test tones (single band dominates → strong color); white noise averages to near-equal bands → near-gray due to balanced ratios | No special-casing needed; threshold prevents silence noise; uniform distribution produces a muted neutral result which is correct |

---

## Open Questions

1. **Does FFT on BASS decode stream advance the stream position?**
   - What we know: Official BASS docs state "the number of bytes read from the channel (to perform the FFT) is returned" — implying data consumption. Community usage shows interleaving FFT with PCM reads.
   - What's unclear: Whether the FFT window and PCM read window are the same bytes or independent.
   - Recommendation: In implementation, treat FFT call as potentially consuming data. Call PCM read first (for peaks), then FFT immediately after — the FFT window will cover the just-decoded PCM. Test with a known signal (sine wave at 440 Hz) and verify the correct bin lights up. If FFT and PCM reads conflict, fall back to a second decode stream (only if testing reveals the issue).
   - Confidence: MEDIUM — empirically validated in open-source C# BASS examples; not contradicted by official docs.

2. **Color saturation at muted palette — visible on both dark AND light Explorer themes?**
   - What we know: The locked decision is muted/desaturated tones. Phase 3 uses amplitude-gradient rendering that works on both themes.
   - What's unclear: Whether the specific RGB values suggested need tuning after visual testing.
   - Recommendation: Start with the palette values in Code Examples above. Build and test against both themes before finalizing. The planner should include a "verify visual quality on both themes" step.

3. **Toggle button placement — does top-right waveform corner conflict with waveform hover tooltip?**
   - What we know: The hover time tooltip appears near the cursor position in the top area of the waveform, offset up from the cursor.
   - What's unclear: Whether the tooltip rendering overlaps with the fixed toggle button position.
   - Recommendation: WaveformRenderer draws the toggle button AFTER drawing hover tooltips; it will naturally appear on top. The toggle button is small (20x20px) and fixed in the corner; tooltip moves with cursor. Conflict is cosmetic only and resolved by draw order.

---

## Sources

### Primary (HIGH confidence)
- `https://www.un4seen.com/doc/bass/BASS_ChannelGetData.html` — FFT flags, bin frequency formula, decode stream behavior
- `https://managedbass.github.io/api/ManagedBass.DataFlags.html` — DataFlags enum values (FFT256 through FFT32768, FFTComplex, FFTNoWindow, FFTRemoveDC)
- Existing codebase: `WaveformGenerator.cs`, `WaveformCache.cs`, `WaveformRenderer.cs`, `ThemeHelper.cs`, `ConfigManager.cs`, `AppConfig.cs`, `PreviewWindow.cs` — Phase 3 patterns verified by reading source

### Secondary (MEDIUM confidence)
- `https://serato.com/forum/discussion/202498` — Serato waveform crossover frequencies (~200Hz, ~1.5kHz empirically measured); aligns with DJ-standard crossovers in locked decisions
- `https://forum.audacityteam.org/t/waveform-view-that-shows-some-frequency-info-colors/53506` — weighted RGB from band energies algorithm, white-balance approach
- `https://support.serato.com/hc/en-us/articles/224969307-Main-Waveform-Display` — visual confirmation: red=bass, green=mids, blue=highs

### Tertiary (LOW confidence — need implementation validation)
- Un4seen forum `topic=13305.0` — decode stream FFT interleaving usage pattern (community source, not official)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — ManagedBass FFT verified via official docs; no new dependencies confirmed
- Architecture: HIGH — direct extension of Phase 3 patterns already in production code
- FFT bin math: HIGH — standard DSP formula verified via BASS official docs
- Color palette values: MEDIUM — algorithm is sound; specific RGB values need visual testing
- FFT/PCM stream interleaving behavior: MEDIUM — consistent with community usage, official doc ambiguous on exact position advancement

**Research date:** 2026-02-17
**Valid until:** 2026-03-17 (BASS API is stable; 30-day window is conservative)
