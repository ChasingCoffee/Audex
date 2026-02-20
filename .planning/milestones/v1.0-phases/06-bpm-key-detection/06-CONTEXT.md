# Phase 6: BPM & Key Detection - Context

**Gathered:** 2026-02-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Detect BPM (tempo) and musical key via audio analysis when file tags are missing. Display detected values with confidence in the existing Music Info section. Analysis runs in the background with caching to avoid re-analysis. Files with existing BPM/key tags are NOT analyzed — tags are authoritative.

</domain>

<decisions>
## Implementation Decisions

### Analysis trigger & timing
- Auto-start analysis after a configurable delay (Claude decides delay duration) when file has no BPM/key tags
- Files WITH existing BPM/key tags skip analysis entirely — tags are authoritative
- Separate BASS decode stream for analysis (independent of playback, like waveform generation)
- For long files, analyze only the first 5 minutes of audio
- Cancel-vs-continue on file switch: Claude's discretion based on resource constraints
- Format eligibility (which formats to analyze): Claude's discretion based on format characteristics
- BPM and key analysis parallelism (simultaneous vs sequential): Claude's discretion
- Config toggle: "Enable BPM/Key Detection" setting, defaults to ON, user can disable

### Result display & accuracy
- Label the source: show "120 BPM (detected)" vs "120 BPM (tag)" to distinguish origin
- Confidence shown as percentage: "120 BPM (detected — 92%)"
- BPM displayed as integer (rounded to nearest whole number)
- Musical key in standard notation: Am, C, F#m, Bb, etc.
- Am/A format: lowercase 'm' suffix for minor, bare letter for major
- Standard enharmonic convention: Bb not A#, Eb not D#, F# not Gb, etc.
- Detection failure shows dash with reason: "— (unable to detect)"

### Caching & persistence
- Cache location: %TEMP%\Audex\analysis\ (consistent with waveform cache pattern)
- Cache key strategy: Claude's discretion (consistent with existing waveform cache approach)
- Cache size limit / cleanup: Claude's discretion (entries are tiny — BPM, key, confidence)
- Cache payload contents: Claude's discretion (at minimum BPM, key, confidence; optionally metadata for debugging)

### Re-analysis control
- Re-analyze button in Music Info section next to detected values
- Button style: refresh/reload icon with tooltip "Re-analyze BPM/Key"
- Button visibility: Claude's discretion (likely only for detected values, matching the "tags skip analysis" decision)
- During re-analysis: keep old values visible (dimmed/faded) while progress runs
- Cooldown after re-analysis to prevent accidental double-clicks
- Re-analyze on previous failures: Claude's discretion
- Change highlight after re-analysis: Claude's discretion
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

</decisions>

<specifics>
## Specific Ideas

- Progress indicator should show actual percentage based on how much audio has been processed, not an indeterminate spinner
- The "detected" vs "tag" label distinction is important — user wants to know where values came from
- Standard music theory enharmonic spelling (Bb, Eb, F#) rather than all-sharps or all-flats

</specifics>

<deferred>
## Deferred Ideas

- Write detected BPM/key back into file tags — new capability involving file modification, write permissions, tag format handling. Could be its own phase or added to Phase 7.

</deferred>

---

*Phase: 06-bpm-key-detection*
*Context gathered: 2026-02-17*
