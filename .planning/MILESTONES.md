# Milestones

## v1.0 MVP (Shipped: 2026-02-20)

**Phases completed:** 8 phases, 21 plans
**Lines of code:** 10,745 C#
**Commits:** 129
**Timeline:** 3 days (2026-02-16 to 2026-02-18)
**Git range:** 5998a14..474e280

**Key accomplishments:**
1. COM shell extension foundation — Windows Explorer audio preview pane with lifecycle management and low-integrity process compatibility
2. Full audio playback — BASS/WASAPI engine with play/pause/stop, volume/mute, seek, and metadata display
3. Interactive frequency-colored waveform — mirrored bars with click-to-seek, drag-to-scrub, DJ-standard bass/mids/highs coloring, and disk cache
4. Extended format support — AAC, Opus, WMA, AIFF, OGG, M4A, and tracker formats (MOD/XM/IT/S3M) via BASS plugins
5. BPM & key detection — background audio analysis with Krumhansl-Schmuckler key detection, confidence display, and binary cache
6. Settings & keyboard shortcuts — JSON config, autoplay/loop, WASAPI device selection, waveform height presets, full keyboard control
7. Inno Setup installer — COM registration, .NET detection, format checkboxes, and audit-driven tech debt fixes

**Tech debt (documented in audit):**
- DiagLogPath hardcoded to dev machine path
- StartLoading() never called (loading spinner logic unused)
- ManagedBass.Flac.dll shipped but unused (FLAC works via native bassflac.dll)

---

