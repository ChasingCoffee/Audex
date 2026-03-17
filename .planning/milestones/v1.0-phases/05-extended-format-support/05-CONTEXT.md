# Phase 5: Extended Format Support - Context

**Gathered:** 2026-02-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Add playback support for extended audio formats (AIFF, OGG, AAC, WMA, OPUS, M4A, module formats) via BASS plugins, display BPM and musical key from existing file tags, and update registration/unregistration scripts for the expanded format list.

</domain>

<decisions>
## Implementation Decisions

### Plugin Loading Strategy
- All BASS plugin DLLs placed in the same directory as the main DLL (no subfolder)
- Plugin load timing: Claude's discretion (eager vs lazy)
- Missing plugin: waveform and controls grey out, waveform area displays "Format Unavailable: {reason}" (e.g., "OPUS plugin not found")
- Only register file extensions whose plugin DLLs are actually present at registration time
- Plugins must be pre-installed — no runtime downloading
- All formats equally important — no priority ordering
- Module formats (.mod, .xm, .it, .s3m): simpler mono-color waveform only (no frequency coloring)
- Module format metadata: show what's available, hide fields that don't apply (no N/A)
- Use BASS WMA plugin for WMA (not Windows Media Foundation)
- Use BASS AAC plugin for AAC/M4A (not Windows Media Foundation)
- Researcher identifies which specific BASS plugins cover which formats
- BASS licensing undecided — Claude's discretion on whether to design for easy license switching
- Plugin load success/failure logged to diag.log only (not surfaced in UI)

### Unsupported Format Handling
- When format can't decode: waveform + playback controls grey out; metadata grid still shows whatever TagLib can read
- Error message includes reason (e.g., "Format Unavailable: OPUS plugin not found")
- Unrecognized file types / corrupt files: Claude's discretion
- Format detection (magic bytes vs extension-only): Claude's discretion
- Header parser extension for new formats: Claude's discretion

### BPM & Key Display
- New "Music Info" section with visible header, separate from technical metadata
- Section always visible — show dashes for missing values (placeholder for Phase 6 detection)
- Display order: Key first, then BPM
- Key format: standard notation (Am, C#m, F) — normalize from raw tag values
- BPM format: whole number only (round to nearest integer)
- Read BPM/key from ALL tag types (ID3v2, Vorbis Comments, APE) — use whichever has value
- Read Serato, Traktor, and rekordbox custom BPM/key tags as fallbacks
- When multiple sources conflict: most precise value wins (DJ tool analysis often more accurate)
- Display value only — no source indicator (no "from Serato" etc.)

### Format Registration
- Take over from any existing preview handler for registered extensions
- Supported formats list stored in config.ini [Formats] section — user-editable
- All formats (including module formats) registered by default
- Register both .aif and .aiff for AIFF
- OGG container handling: Claude's discretion (how BASS handles Vorbis vs Opus)
- AAC extensions (.aac vs .m4a): Claude's discretion based on BASS capabilities
- Additional formats beyond requirements: Claude's discretion (add what BASS supports easily)
- Config.ini changes and re-registration: Claude's discretion on runtime vs re-register approach
- Registration script update deferred to Phase 7 installer — keep hardcoded for now
- Unregister script updated in Phase 5 to cleanly remove all new format registrations
- Save backup of previous preview handler registrations to %LOCALAPPDATA%\Audex\prev-handlers.json
- Unregister restores previous handlers from backup

### Claude's Discretion
- Plugin load timing (eager at startup vs lazy on demand)
- Format detection approach (magic bytes fallback vs extension-only)
- Header parser factory extension strategy for new formats
- Handling of corrupt/partially-decodable files
- Unrecognized file types (show our error vs let Explorer handle)
- Whether to design plugin architecture for easy license switching
- Additional format extensions beyond requirements
- OGG container codec detection
- AAC/M4A extension registration scope
- Runtime config reload vs re-registration for format changes

</decisions>

<specifics>
## Specific Ideas

- All BASS plugins — consistent codec path through BASS for everything (no Windows Media Foundation fallback)
- DJ software tag support: read Serato, Traktor, and rekordbox custom tag frames for BPM/key — many DJ files only have analysis data in these custom frames, not in standard ID3 TBPM/TKEY
- "Most precise wins" for conflicting BPM values — DJ tool analysis is typically more accurate than manually-entered round numbers
- Key normalization: convert various text representations ("a minor", "Amin", "a-minor") to standard "Am" notation

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 05-extended-format-support*
*Context gathered: 2026-02-17*
