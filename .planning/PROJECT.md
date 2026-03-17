# Audex

## What This Is

A Windows Explorer preview pane handler for audio files. When a user selects an audio file in Explorer, the preview pane renders a frequency-colored waveform, provides playback controls with keyboard shortcuts, and displays file metadata including musical properties like BPM and key (from tags or audio analysis). Built as a C#/.NET Framework 4.8 COM shell extension using the BASS audio engine with WASAPI output.

## Core Value

Select an audio file in Windows Explorer and immediately hear and see it — waveform, playback, metadata — without opening another application.

## Requirements

### Validated

- ✓ Windows Explorer preview pane handler (IPreviewHandler COM interface) — v1.0
- ✓ Audio playback via BASS engine with WASAPI output — v1.0
- ✓ Frequency-colored waveform display (bass/mids/highs color spectrum) — v1.0
- ✓ Playback position indicator on waveform — v1.0
- ✓ Playback controls (play, pause, stop, seek via waveform click) — v1.0
- ✓ Autoplay setting (auto-play on file select) with loop toggle — v1.0
- ✓ Broad format support: WAV, FLAC, AIFF, MP3, OGG, AAC, WMA, OPUS, M4A, MOD/XM/IT/S3M — v1.0
- ✓ Metadata display: title, artist, album, duration, sample rate, bit depth, channels — v1.0
- ✓ Musical metadata: BPM and key from tags (ID3, Vorbis comments) — v1.0
- ✓ BPM/key analysis when tags missing (Krumhansl-Schmuckler key detection) — v1.0
- ✓ Settings stored in JSON config file (AppData) — v1.0
- ✓ Settings UI accessible from preview pane (gear icon overlay) — v1.0
- ✓ Keyboard shortcuts (Ctrl+Space, Ctrl+arrows, Ctrl+L/M/,) — v1.0
- ✓ WASAPI device selection — v1.0
- ✓ Inno Setup installer with COM registration and .NET detection — v1.0

### Active

- [ ] Optional ASIO output (user-configurable, fallback to WASAPI)
- [ ] Playback speed adjustment without pitch shift
- [ ] Loop region / A-B repeat points
- [ ] Waveform zoom for detailed view
- [ ] Cue point markers on waveform

### Out of Scope

- Album art display — Explorer already shows thumbnails
- Video file support — different rendering pipeline, massive complexity
- Audio editing / effects — preview handler is read-only by design
- Tag editing / writing — write operations complicate shell extension, file locking
- Playlist / multi-file queue — violates single-file IPreviewHandler contract
- Streaming / URL support — shell extension targets local filesystem only
- Plugin system — stability risk for shell extension running inside Explorer
- Batch processing — preview handler is single-file focus
- macOS/Linux support — Windows Explorer integration only

## Context

Shipped v1.0 with 10,745 LOC C# across 137 files.
Tech stack: C# / .NET Framework 4.8, WinForms UserControl (reparented via SetParent), GDI+ rendering, BASS audio engine (ManagedBass), WASAPI output, Inno Setup installer.

Architecture: COM shell extension (IPreviewHandler + IPreviewHandlerVisuals) registered via regasm or Inno Setup installer. Runs inside Explorer's prevhost.exe (low-integrity, STA). Static renderers (LayoutRenderer, ControlBarRenderer, WaveformRenderer, SettingsOverlayRenderer) paint directly via GDI+ in OnPaint — no WPF, no WinForms controls beyond the host UserControl.

Known tech debt (from v1.0 audit):
- DiagLogPath hardcoded to dev machine path (fails silently on non-dev machines)
- StartLoading() never called (loading spinner logic exists but unused)
- ManagedBass.Flac.dll shipped but unused (FLAC works via native bassflac.dll)

## Constraints

- **Platform**: Windows only — tied to Explorer shell extension APIs
- **Tech Stack**: C# / .NET Framework 4.8, WinForms UserControl, GDI+ rendering
- **Audio Engine**: BASS (un4seen.com) — commercial license required for distribution
- **Architecture**: 64-bit only (modern Explorer is 64-bit process)
- **COM**: Must implement IPreviewHandler, register as shell extension with ThreadingModel=Apartment
- **Audio Output**: WASAPI default, ASIO as future optional setting
- **Settings**: JSON config file in AppData (not registry)
- **Host Process**: prevhost.exe — low-integrity, no WinForms Form ancestry, no WPF

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| C# / .NET Framework 4.8 over C++ | Easier UI dev, ManagedBass wrapper, faster iteration | ✓ Good |
| BASS audio engine | Broad format support, WASAPI/ASIO, proven library | ✓ Good |
| WASAPI default over ASIO | ASIO requires driver install, WASAPI works everywhere | ✓ Good |
| WinForms UserControl + GDI+ (not WPF) | WPF crashes prevhost.exe; raw Win32 CreateWindowEx also crashes | ✓ Good |
| Static renderers (no owned state) | Clean separation, consistent pattern across all UI areas | ✓ Good |
| Frequency-colored waveform | Richer visual info than plain amplitude, DJ-standard colors | ✓ Good |
| BPM/key analysis if tags missing | Broader coverage, Krumhansl-Schmuckler key detection works well | ✓ Good |
| JSON config in AppData | Easy to edit, back up, version — no registry dependency | ✓ Good |
| SHA-256 waveform cache in %TEMP% | Fast revisits, appropriate for derived transient data | ✓ Good |
| Binary .bka analysis cache in AppData | Avoids re-analyzing files, compact format | ✓ Good |
| Owner-drawn GDI+ tooltips | Only viable tooltip in prevhost.exe (WinForms ToolTip broken) | ✓ Good |
| BassMix for sample rate conversion | Required for decode streams feeding WASAPI output | ✓ Good |
| Inno Setup installer | COM registration, .NET detection, format checkboxes | ✓ Good |
| No album art | Focus on waveform and metadata, keep UI clean | ✓ Good |
| ThreadingModel=Apartment | Required for WinForms STA in prevhost.exe | ✓ Good |

---
*Last updated: 2026-02-20 after v1.0 milestone*
