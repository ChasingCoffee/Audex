# Phase 3: Basic Waveform Visualization - Context

**Gathered:** 2026-02-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Visual waveform display with playback position tracking and click-to-seek interaction. User sees the audio file's amplitude as a waveform, can click to seek, and sees a moving playhead during playback. Frequency coloring (Phase 4) and extended format support (Phase 5) are out of scope.

</domain>

<decisions>
## Implementation Decisions

### Waveform Appearance
- Vertical bars style, mirrored from center (bars extend symmetrically up and down from center line)
- Dense spacing (1-2px gap between bars)
- Bars have rounded tops
- Bar width adapts to pane width (more bars in wider panes, fewer in narrower)
- Amplitude gradient: cool tones (dark blue for quiet, bright cyan for loud)
- Gradient adapts to light/dark Explorer theme (adjust brightness/saturation)
- Subtle center line (thin, low-contrast horizontal line at zero amplitude)
- Subtle panel background behind the waveform area (slightly different shade from Explorer's background)
- Minimum bar height of 1-2px even for silent sections (track shape always visible)
- Time labels: start time (0:00) at left and total duration at right — no intermediate tick marks
- Waveform takes balanced vertical space (40-50% of preview pane)
- Played portion: bars behind playhead have reduced opacity (same blue-to-cyan colors, just faded)
- Dimming transition exactly at the playhead position (sharp edge)

### Seek Interaction
- Click on waveform = instant seek (jump on click, not on release)
- Click-and-drag = visual-only scrub (playhead moves visually, audio plays on release)
- Control bar time display updates on release, not during drag
- Crosshair cursor when hovering over waveform
- Hover shows time tooltip near cursor position
- Hover shows thin vertical guide line on waveform at cursor position
- Clicking while audio is stopped starts playback from clicked position
- Waveform IS the seekbar — no separate timeline/seekbar element
- No right-click action on waveform

### Position Indicator
- White vertical line with small downward-pointing triangle marker at top of waveform
- Smooth animation (playhead glides between positions, not snapping bar-to-bar)
- No floating time label near playhead — current time shown only in control bar
- Dimming of played bars transitions exactly at playhead position

### Loading Experience
- Progressive reveal: bars appear left-to-right as they're computed (just pop in, no fade animation)
- Playback allowed immediately — user can play audio while waveform still generating
- Switching files cancels current waveform generation and starts fresh for new file
- Waveform data cached in system temp folder
- Cache size limit (e.g., 50MB) with oldest entries evicted when exceeded
- Generation failure shows "Waveform unavailable" text in the waveform area — playback still works

### Claude's Discretion
- Exact gradient color values and theme adaptation algorithm
- Bar width calculation formula based on pane dimensions
- Waveform generation chunking strategy and thread management
- Cache file format and key scheme (file hash, path, etc.)
- Hover tooltip and guide line styling details
- Triangle marker size and proportions
- Exact opacity value for played/dimmed bars

</decisions>

<specifics>
## Specific Ideas

- Bars mirrored from center like SoundCloud's waveform style
- Crosshair cursor for precision positioning (not hand cursor)
- Progressive reveal feels like watching the waveform build — bars pop in left-to-right as computed
- Waveform click when stopped should start playing (not just set position) — reduces clicks to hear audio

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 03-basic-waveform-visualization*
*Context gathered: 2026-02-16*
