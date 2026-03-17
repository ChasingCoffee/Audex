# Phase 2: BASS Audio Integration - Context

**Gathered:** 2026-02-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Audio playback with transport controls, volume control, and basic metadata display within the existing Explorer preview handler. User can play, pause, stop, and adjust volume for WAV, MP3, and FLAC files. Metadata displays technical info (sample rate, bit depth, channels) and basic tags (title, artist, album) from files. Waveform visualization is Phase 3; extended format support is Phase 5.

</domain>

<decisions>
## Implementation Decisions

### Playback Controls
- Play/Pause toggle + separate Stop button (stop resets position to start)
- Icon buttons only (unicode symbols), no text labels — compact
- All transport buttons same size (no oversized play button)
- Bottom bar with subtle separator line from content above
- Layout: seek bar full width on top row, buttons centered + volume on right on bottom row
- Seek bar above buttons, with elapsed time flanking left and total time flanking right (e.g. 1:23 ━━━━ 4:56)
- Custom-drawn seek bar (owner-drawn, theme-aware) — not WinForms TrackBar
- Highlight on hover for buttons, subtle press effect on click
- Bottom bar follows Explorer dark/light theme (same theming as Phase 1)

### Playback Behavior
- Manual play only — no autoplay (autoplay toggle comes in Phase 7)
- When track finishes: stop and reset position to start, button shows Play
- When switching files while playing: immediately stop current, load new file ready to play (no crossfade)

### Metadata Display
- Technical info is most prominent (sample rate, bit depth, channels, format)
- Grid/table layout for metadata (two-column label-value pairs)
- Positioned below filename header, filling main content area above bottom control bar
- Missing tag fields (title/artist/album) are hidden entirely — only show tags that exist

### Volume Behavior
- Horizontal volume slider on right side of bottom bar button row
- Custom-drawn slider matching seek bar style (theme-aware)
- Speaker icon with mute toggle (click to mute/unmute)
- Speaker icon: 2 states only — muted (X) and unmuted (speaker)
- No volume percentage text — slider position only
- Volume persists across sessions (saved to config file, restored on next launch)
- Default volume on first use: 50%

### BASS Library Setup
- bass.dll bundled directly in project, deployed in same directory as handler DLL
- ManagedBass NuGet package for .NET wrapper
- WASAPI shared mode output (doesn't block other apps)
- BASS initialized once when handler loads, kept alive until unload (not per-file)
- BASS license: freeware for development, evaluate commercial license before public release
- If BASS fails to initialize: full error panel replacing entire preview content with troubleshooting hint

### Tag & Metadata Sources
- TagLib# NuGet library for tag reading (title, artist, album) — richer format support, positions for Phase 5
- BASS for technical metadata (sample rate, bit depth, channels, bitrate) — accurate from decoded stream
- Phase 1 header parsers (AudioHeaderParserFactory) kept as fallback if BASS and TagLib# both fail

### Claude's Discretion
- IStream handling approach (temp file copy vs direct IStream reading via BASS callbacks)
- Whether basswasapi.dll plugin is needed or BASS has built-in WASAPI support
- Whether bassflac.dll plugin is needed or BASS has built-in FLAC support
- Exact icon glyphs for play, pause, stop, speaker
- Spacing and padding within the bottom bar
- Error message wording and troubleshooting hints

</decisions>

<specifics>
## Specific Ideas

- Bottom bar layout inspired by standard media players: seek bar spanning full width, transport buttons centered below with volume on the right
- Time display flanking the seek bar: "1:23 ━━━━ 4:56" pattern
- Preview pane is narrow — all controls must work in a constrained horizontal space
- Controls should feel integrated with Explorer, not like a separate app embedded in the pane

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 02-bass-audio-integration*
*Context gathered: 2026-02-16*
