# Phase 1: COM Shell Extension Foundation - Context

**Gathered:** 2026-02-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Preview handler appears in Windows Explorer when user selects audio file, with proper resource cleanup and low-integrity process compatibility. This phase delivers the COM shell extension infrastructure, placeholder UI, error handling, and file type registration. Audio playback, waveform visualization, and metadata parsing beyond basic file headers are separate phases.

</domain>

<decisions>
## Implementation Decisions

### Placeholder UI
- File info panel showing filename, file size, format, and parsed header info (duration, sample rate, bit depth, channel count)
- Explorer-native visual style — match Windows Explorer's current theme (respects system light/dark mode)
- Layout skeleton showing grayed-out regions where waveform and playback controls will appear in future phases
- "Playback coming soon" note for registered formats that don't have audio support yet

### Error Presentation
- Inline text messages in the preview pane (no icons)
- Error appears as a banner overlay at top of preview, previous content stays dimmed underneath
- User-friendly language with log file path: "This file can't be previewed. See log for details: [path]"
- No retry link in the error banner — user re-selects the file in Explorer to retry
- Single rolling log file in %LOCALAPPDATA%/Audex/logs/
- Errors + warnings logged normally, with debug toggle via config file in AppData
- Config file (JSON/INI) in %LOCALAPPDATA%/Audex/ controls log level

### File Type Registration
- Register ALL planned audio format extensions upfront (WAV, MP3, FLAC, AIFF, OGG, AAC, WMA, OPUS, M4A)
- Defer tracker/module formats (.mod, .xm, .it, .s3m) to Phase 5 when BASS plugins are integrated
- Unrecognized/unsupported formats show file info panel + "Playback support coming in a future update" note
- Audex replaces any existing default preview handler for registered audio formats
- Both installer-based (Phase 7) and manual regsvr32/script registration for dev/testing
- Extension list is config-driven (read from config file), not hardcoded — new formats can be added without recompiling
- Config file for extensions lives in %LOCALAPPDATA%/Audex/

### Loading & Transitions
- Instant swap when switching between files (no animation/fade)
- Loading indicator (spinner) shown only if loading takes >200ms — avoids flicker on fast loads
- Short debounce (~150ms) when rapidly browsing files — skip intermediate files, only load the final selection
- First file selection loads immediately (no debounce), debounce only on subsequent rapid switches
- During rapid browsing, suppress errors for debounced/skipped files — only show error if the final "winning" file fails
- Instant dispose when navigating away from audio file to non-audio file — release resources immediately, let Explorer handle visual transition
- Responsive resize — relayout content when user resizes the preview pane
- Consistent ~150ms debounce regardless of phase — don't tune per-phase

### Claude's Discretion
- Exact spinner style and placement
- Layout skeleton proportions and placeholder appearance
- File header parsing approach (how to read duration/sample rate without full audio library)
- COM registration implementation details
- Config file format choice (JSON vs INI)

</decisions>

<specifics>
## Specific Ideas

- Explorer-native means following Windows system theme — if the user has dark mode, the preview pane should be dark
- Layout skeleton should hint at the final product layout (waveform area, controls area) without making specific UI promises
- The config-driven extension list is important for future extensibility — adding a new format should be a config change, not a rebuild

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-com-shell-extension-foundation*
*Context gathered: 2026-02-16*
