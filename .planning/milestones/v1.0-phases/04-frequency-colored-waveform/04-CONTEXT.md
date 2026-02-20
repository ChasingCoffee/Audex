# Phase 4: Frequency-Colored Waveform - Context

**Gathered:** 2026-02-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Enhance the existing waveform to display frequency content (bass/mids/highs) using color coding. Bar height remains amplitude-based (Phase 3). Color represents the frequency mix at each point. Users can visually identify bass-heavy sections, vocal sections, and high-frequency content. A toggle allows switching between monochrome and frequency-colored modes.

</domain>

<decisions>
## Implementation Decisions

### Color mapping
- Heat spectrum palette: bass = red/warm, mids = yellow/green, highs = blue/cool
- Muted/desaturated tones — visible but not distracting, blends with Explorer's UI
- Each bar is a single blended color representing the weighted frequency mix (not stacked segments)
- Played/past region uses alpha dimming (same approach as Phase 3, frequency colors still visible underneath)
- Palette adjusted per theme — slightly different hues/saturation for contrast on both light and dark backgrounds
- Below a certain energy threshold, bars render neutral/gray instead of attempting to color silence
- Playhead remains white (same as Phase 3)

### Frequency bands
- DJ-standard crossover frequencies (~200Hz bass/mid, ~2.5kHz mid/high)
- Musical frequency range only (~20Hz-16kHz)
- FFT window size as named internal constant (tweakable but not user-facing)

### Visual blending
- Smooth transitions between neighboring bars (averaging/smoothing so adjacent bars don't jump colors abruptly)
- Bar height = amplitude only (same as Phase 3); color = frequency content — two independent dimensions
- Color dimming state updates on mouse-up release (consistent with Phase 3 seek behavior), not during drag

### Waveform toggle
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

</decisions>

<specifics>
## Specific Ideas

- "I was thinking like Serato or FL Studio colored waveforms" — both do frequency coloring on waveforms well, target that aesthetic
- Progressive loading approach should reuse the same mechanism from Phase 3

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 04-frequency-colored-waveform*
*Context gathered: 2026-02-17*
