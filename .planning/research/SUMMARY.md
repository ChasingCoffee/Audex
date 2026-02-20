# Project Research Summary

**Project:** Audex
**Domain:** Windows Shell Extension - COM Audio Preview Handler
**Researched:** 2026-02-16
**Confidence:** MEDIUM

## Executive Summary

Audex is a Windows Explorer preview pane handler that displays frequency-colored waveforms, playback controls, and metadata (including BPM/key detection) for audio files. This is a specialized domain combining three distinct technical areas: COM shell extensions, WPF UI development, and professional audio processing. The recommended approach uses .NET 8 with WPF for the UI, BASS.NET for audio engine capabilities, and direct COM interop (without frameworks like SharpShell) for maximum control and debuggability.

The critical success factor is proper COM lifecycle management and resource cleanup. Shell extensions that leak resources or fail to release file handles cause Explorer crashes and create terrible user experiences. The architecture must be built from day one with explicit disposal patterns, stream-based initialization for low-integrity process compatibility, and async processing to avoid blocking the Explorer UI thread.

The main risk areas are: COM threading model violations (causes intermittent crashes), BASS resource leaks (system instability after previewing many files), and synchronous waveform processing blocking Explorer. These are all preventable with proper architecture from Phase 1, but expensive to retrofit later. The differentiating features (frequency-colored waveform, BPM/key detection) should be added only after the core preview handler is stable and properly handles resource cleanup.

## Key Findings

### Recommended Stack

The technology stack centers on .NET 8 for modern C# features and performance, WPF for hardware-accelerated UI rendering, and BASS audio library for professional-grade audio capabilities. Alternatives like NAudio lack BASS's low-latency features and plugin ecosystem, while frameworks like SharpShell add abstraction layers that complicate debugging.

**Core technologies:**
- **.NET 8 SDK**: Runtime framework with WPF support — long-term support release, modern COM interop improvements, required for C# 12 features
- **BASS.NET with plugins**: Audio engine — industry standard for Windows audio, extensive format support, low-latency WASAPI/ASIO output, proven FFT capabilities for waveform analysis
- **Microsoft.Windows.CsWin32**: Windows API interop — source-generated P/Invoke for type-safe COM definitions, replaces manual interop and avoids SharpShell dependency
- **WriteableBitmapEx**: Waveform rendering — optimized WPF bitmap manipulation with hardware acceleration, better native integration than SkiaSharp for Windows-only apps
- **TagLibSharp**: Metadata reading — industry standard for ID3/metadata tags across all common formats, includes BPM/key tag reading
- **System.Text.Json**: Configuration — built-in, fast, modern JSON handling for settings stored in %APPDATA%

**Critical version notes:**
- Must build for x64 explicitly (shell extensions must match 64-bit Explorer process architecture)
- BASS is not on NuGet, requires manual download from un4seen.com with proper plugin deployment
- BASS requires commercial license (~100 EUR) unless distributed as true freeware

### Expected Features

Research reveals that basic preview handler features (playback, seek, volume, metadata display) are table stakes that users assume exist. The competitive differentiators are frequency-colored waveforms and BPM/key detection, which target the DJ/producer audience but require significant implementation effort. The MVP should focus on core functionality with basic waveforms, then add frequency coloring and analysis features once stability is proven.

**Must have (table stakes):**
- Basic playback controls (play/pause/stop) — core function, users won't use without this
- Seek/scrub timeline — essential for navigating audio files
- Waveform visualization — visual representation expected by DJ/producer audience
- File metadata display — format, duration, sample rate, bit depth expected
- Volume control — standard in all audio players
- Core format support (WAV, MP3, FLAC) — mandatory formats, 90% of use cases
- Explorer integration — update on file selection, proper resource cleanup
- Proper resource cleanup — critical to avoid file locking frustration

**Should have (competitive):**
- Frequency-colored waveform — key differentiator, shows frequency content (bass/mids/highs) at a glance
- BPM detection & display — critical for DJ workflow, read tags first then analyze if missing
- Musical key detection — complements BPM for harmonic mixing
- Keyboard shortcuts — power user efficiency (space=play/pause, arrows=seek)
- Extended format support — AAC, OGG, OPUS, WMA, module formats after core is stable
- Musical property cache — avoid re-analyzing files, store BPM/key by file hash
- Playback speed control — useful for DJ practice, BASS supports tempo without pitch shift

**Defer (v2+):**
- Loop region playback — useful but not critical, complex UI
- Cue points/markers — DJ software feature, wait for user demand validation
- A-B repeat — builds on loop/cue points, defer until those exist
- Waveform zoom — nice to have, requires rendering architecture decision

**Explicitly excluded (anti-features):**
- Album art display — Explorer already shows thumbnails, scope creep
- Video file support — different rendering pipeline, massive complexity
- Audio editing/effects — preview handler should be read-only
- Tag editing — file write operations complicate shell extension
- Playlist/multi-file — violates single-file preview handler contract
- Plugin system — complexity explosion, stability risk for shell extension

### Architecture Approach

The architecture follows the standard Windows preview handler pattern: COM shim implements IPreviewHandler and related interfaces, HwndSource bridges WPF content to the Win32 window handle provided by Explorer, and WPF UserControl contains the actual preview UI. The critical pattern is lazy initialization - store references during Initialize/SetWindow but delay all expensive operations (audio decoding, waveform generation) until DoPreview is called, since Explorer initializes handlers for multiple files but only calls DoPreview for the selected one.

**Major components:**
1. **PreviewHandlerShim** — COM interface implementation (IPreviewHandler, IInitializeWithStream, IOleWindow) managing lifecycle and marshalling between unmanaged Explorer and managed WPF
2. **HwndSource** — WPF interop bridge creating child HWND for WPF content within Explorer's parent window
3. **PreviewControl (WPF UserControl)** — Main UI container composing waveform canvas, metadata panel, and playback controls
4. **AudioEngine** — BASS.NET wrapper managing device initialization, stream creation/disposal, output selection (WASAPI/ASIO), must run on STA thread
5. **WaveformAnalyzer** — Decodes PCM samples, performs FFT analysis for frequency bands, generates rendering data on background thread
6. **MetadataPanel** — Displays file metadata from TagLib# (tags) and BASS (technical properties)

**Key patterns:**
- **Stream-based initialization**: Use IInitializeWithStream (not file path) for low-integrity process compatibility and security
- **Lazy preview rendering**: Delay waveform generation until DoPreview, not during Initialize (Explorer initializes many handlers)
- **Separate AppID**: Custom AppID registry entry ensures correct .NET runtime version in Prevhost.exe surrogate
- **Explicit disposal**: All BASS handles wrapped in IDisposable, never rely on GC for COM/native cleanup
- **Thread marshalling**: BASS callbacks execute on audio thread, must use Dispatcher.BeginInvoke for WPF updates

### Critical Pitfalls

The research identified ten critical pitfalls that cause crashes or require major rewrites if not addressed early. The most severe are COM lifecycle issues, threading violations, and BASS resource leaks - all architectural concerns that must be designed correctly from Phase 1.

1. **Improper COM lifetime management** — Failing to release resources in IPreviewHandler.Unload causes Explorer crashes. Must immediately stop BASS playback, dispose WPF controls, release COM references, never rely on GC. Test with rapid file switching (50+ files).

2. **Threading model violations** — STA/MTA mismatches cause intermittent crashes and deadlocks. WPF requires STA, BASS callbacks use different threads. All IPreviewHandler methods called on same STA thread. Must use Dispatcher.BeginInvoke to marshal BASS callbacks to UI thread.

3. **Not running as low integrity level** — Disabling low IL process isolation creates security vulnerabilities. Must use IInitializeWithStream (not file path), store config in %LOCALAPPDATA%\Low (not %LOCALAPPDATA%), test in actual low IL environment from day one.

4. **BASS resource leaks** — Forgetting to call Bass.StreamFree, Bass.Free causes handle exhaustion after previewing 100-1000 files. Wrap all BASS handles in IDisposable wrappers, use try/finally, test with 1000+ file sequence.

5. **Synchronous large file processing** — Decoding entire file in DoPreview blocks Explorer UI thread. DoPreview must return within 200ms, decode waveform async with loading indicator, consider decoding first 60 seconds only for huge files.

6. **Missing error handling** — Exceptions swallowed by Explorer, silent failures impossible to debug. Wrap all IPreviewHandler methods in try-catch with logging to %LOCALAPPDATA%\Low\Audex\logs, log critical operations and full stack traces.

7. **WPF HwndSource lifetime mismatch** — Creating/disposing HwndSource at wrong time causes rendering failures or crashes. Create in SetWindow only, dispose in Unload with null guards, never recreate in SetRect (resize).

8. **32-bit vs 64-bit binary mismatch** — Platform target "Any CPU" with wrong BASS.DLL variant causes DllNotFoundException. Build for x64 explicitly, use 64-bit bass.dll, validate in installer.

9. **Registry registration errors** — Missing/incorrect COM registration prevents handler from loading. Use WiX/InstallShield for production, test on clean machine, verify HKCR\CLSID and file type associations under shellex\{preview-handler-guid}.

10. **BASS license violation** — Shipping BASS.DLL without commercial license creates legal liability. BASS is free for freeware only, requires ~100 EUR license for commercial/shareware. Decide licensing model in Phase 0.

## Implications for Roadmap

Based on the research findings, I recommend a six-phase approach that prioritizes architectural stability before adding differentiating features. The dependency analysis shows that COM infrastructure, BASS integration, and resource cleanup must be bulletproof before attempting frequency-colored waveforms or BPM/key detection. This ordering prevents expensive retrofitting and ensures a stable foundation.

### Phase 1: COM Shell Extension Foundation

**Rationale:** Shell extension lifecycle management is the highest-risk area. Must establish proper COM interop, resource cleanup, and threading patterns before any audio features. If built incorrectly, requires major architectural rewrites. Build skeleton now, add features incrementally.

**Delivers:**
- Functional preview handler that appears in Explorer preview pane
- Proper IPreviewHandler, IInitializeWithStream, IOleWindow implementation
- HwndSource hosting of WPF UserControl
- Stream-based file access working in low-integrity process
- Logging infrastructure to %LOCALAPPDATA%\Low for debugging
- COM registration working on clean test machine
- Placeholder UI (no audio yet) to validate hosting

**Addresses:**
- Explorer integration (table stakes feature)
- Proper resource cleanup (table stakes feature)

**Avoids:**
- Pitfall 1 (COM lifetime) — builds disposal patterns from start
- Pitfall 2 (Threading) — establishes STA threading model
- Pitfall 3 (Low IL) — uses stream initialization, correct AppData paths
- Pitfall 6 (Error handling) — logging framework in place
- Pitfall 7 (HwndSource) — correct creation/disposal in lifecycle
- Pitfall 9 (Registry) — registration tested and validated

**Research flag:** SKIP — Well-documented Windows API with official Microsoft documentation. Standard patterns for IPreviewHandler.

### Phase 2: BASS Integration & Basic Playback

**Rationale:** Establish audio engine foundation with proper resource management before attempting complex waveform analysis. Validates that BASS works correctly in preview handler context (threading, disposal, device access). Basic playback is table stakes feature.

**Delivers:**
- BASS.NET initialization in preview handler
- Audio stream loading from IStream (not file path)
- Basic playback controls (play/pause/stop)
- Volume control
- WASAPI output device selection
- Basic file metadata display (duration, format, sample rate from BASS)
- Proper BASS cleanup (StreamFree, Free) in Unload
- Core format support (WAV, MP3, OGG - native BASS formats)

**Addresses:**
- Basic playback controls (table stakes)
- Volume control (table stakes)
- File metadata display (table stakes)
- Core format support (table stakes)

**Avoids:**
- Pitfall 4 (BASS leaks) — builds IDisposable wrappers for all handles
- Pitfall 5 (Synchronous processing) — establish async patterns now
- Pitfall 8 (32/64-bit) — validate correct BASS.DLL deployment
- Pitfall 10 (BASS license) — decide freeware vs commercial, purchase if needed

**Research flag:** SKIP — BASS.NET has established patterns, well-documented API for initialization and playback.

### Phase 3: Basic Waveform Visualization

**Rationale:** Add visual representation before frequency coloring complexity. Validates PCM decoding, rendering pipeline, and async generation patterns. Basic waveform is table stakes, frequency coloring is differentiator that can be added incrementally.

**Delivers:**
- PCM sample extraction from BASS decode streams
- Mono waveform generation (peak/RMS per time bucket)
- WriteableBitmapEx rendering to WPF Canvas
- Async waveform generation with loading indicator
- Seek/scrub timeline integrated with waveform
- Click-to-seek on waveform
- Proper high-DPI rendering

**Addresses:**
- Waveform visualization (table stakes)
- Seek/scrub timeline (table stakes)

**Avoids:**
- Pitfall 5 (Synchronous processing) — waveform generated async, DoPreview returns quickly
- Pitfall 12 (High DPI) — query DPI, limit bitmap to logical pixels

**Uses:**
- BASS decode channels (BASS_STREAM_DECODE) for sample access
- WriteableBitmapEx for efficient WPF bitmap manipulation
- Background Task for decode to avoid blocking UI thread

**Research flag:** MODERATE — Waveform generation algorithms are straightforward, but performance tuning (bucket sizing, downsampling strategy) may need experimentation. Consider `/gsd:research-phase` if performance issues arise.

### Phase 4: Frequency-Colored Waveform (Differentiator)

**Rationale:** Now that basic waveform rendering is stable, add the key differentiator. Requires FFT analysis per time bucket and frequency-to-color mapping. This is what sets the tool apart from generic preview handlers.

**Delivers:**
- FFT analysis using BASS_ChannelGetData with BASS_DATA_FFT8192
- Frequency band extraction (low/mid/high frequency ranges)
- Color mapping (e.g., low=red, mid=yellow, high=blue)
- Layered rendering of frequency bands on waveform
- Visual distinction of bass drops, vocal sections, etc.

**Addresses:**
- Frequency-colored waveform (key differentiator for DJ/producer audience)

**Implements:**
- WaveformAnalyzer enhancement (FFT processing)
- Multi-layer rendering strategy in WaveformCanvas

**Avoids:**
- Pitfall 15 (Format testing) — validate FFT works across all formats

**Research flag:** MODERATE — FFT windowing, frequency band thresholds, and color mapping strategies may need research if visual results are poor. Consider `/gsd:research-phase` for color scheme and band selection research if needed.

### Phase 5: Extended Format Support & Metadata

**Rationale:** Expand format coverage using BASS plugins and add proper metadata reading. Improves utility for diverse music collections. TagLib# integration for rich metadata (BPM/key tags if present).

**Delivers:**
- BASS plugin loading (bassflac.dll, bass_aac.dll, basswma.dll, bassopus.dll)
- Extended format support (FLAC, AAC/M4A, WMA, OPUS)
- TagLibSharp integration for ID3/metadata reading
- Display BPM/key from tags (if present in files)
- Graceful "Unsupported format" messaging
- Format validation (not just extension checking)

**Addresses:**
- Extended format support (should-have feature)
- Enhanced metadata display showing tags

**Avoids:**
- Pitfall 13 (Format crashes) — handle BASS_ERROR_FILEFORM gracefully
- Pitfall 14 (Missing plugins) — validate plugins loaded, show clear error if missing
- Pitfall 15 (Format testing) — comprehensive format test matrix

**Research flag:** SKIP — BASS plugin loading is well-documented, TagLibSharp has standard patterns.

### Phase 6: BPM & Key Detection (Differentiator)

**Rationale:** Add audio analysis for files missing BPM/key tags. High value for DJ audience but complex implementation. Requires external tools (Essentia/KeyFinder CLI) or custom BASS-based analysis. This is the final differentiator.

**Delivers:**
- BPM detection via Essentia CLI or BASS-based onset detection + autocorrelation
- Musical key detection via KeyFinder CLI or BASS-based chroma features
- Tag precedence (read from TagLib# first, analyze only if missing)
- Analysis progress indicator (can take 5-10 seconds)
- Musical property cache in %LOCALAPPDATA%\Low (file hash -> properties)
- Optional "reanalyze" action if cached results seem wrong

**Addresses:**
- BPM detection & display (key differentiator for DJs)
- Musical key detection & display (complements BPM)
- Musical property history/cache (avoid re-analysis)

**Implements:**
- External CLI tool integration via Process.Start() or native BASS analysis
- Caching layer with JSON serialization of analysis results

**Avoids:**
- Pitfall 5 (Synchronous processing) — analysis runs async with progress UI
- Pitfall 11 (Metadata blocking) — analysis on background thread

**Research flag:** HIGH — BPM/key detection algorithms have accuracy vs. performance trade-offs. External tool integration (Essentia, KeyFinder) needs verification of Windows binary availability and CLI interface stability. STRONGLY RECOMMEND `/gsd:research-phase` for this phase to evaluate:
- Available BPM/key detection tools and libraries
- Accuracy benchmarks for different approaches
- Performance characteristics (time to analyze typical track)
- Caching strategy and invalidation logic

### Phase 7: Configuration & Polish

**Rationale:** Add user configurability and production deployment artifacts. Low risk, improves usability. Installer is critical for proper COM registration on user machines.

**Delivers:**
- JSON configuration file in %LOCALAPPDATA%\Low\Audex\config.json
- ConfigManager with validation and defaults fallback
- Settings for output device, waveform colors, analysis options
- Keyboard shortcuts (space=play/pause, arrows=seek)
- WiX or Advanced Installer MSI for COM registration
- Installer handles .NET 8 Runtime prerequisite
- Uninstaller with proper COM cleanup

**Addresses:**
- Keyboard shortcuts (should-have for power users)
- Playback speed control (should-have for DJ practice)

**Avoids:**
- Pitfall 14 (Config parse errors) — robust parsing with schema validation
- Pitfall 9 (Registry errors) — installer automates registration

**Research flag:** SKIP — Configuration management and installer creation have standard patterns.

### Phase Ordering Rationale

- **Phases 1-2 before features:** COM lifecycle and BASS resource management are architectural foundations. Building features first then retrofitting proper cleanup is expensive (HIGH recovery cost per pitfalls research).

- **Basic waveform (Phase 3) before frequency coloring (Phase 4):** Validates rendering pipeline and async patterns with simpler mono waveform. FFT analysis adds complexity that should be incremental.

- **Format support (Phase 5) before analysis (Phase 6):** BPM/key detection requires decoding all formats. Establishing plugin architecture first ensures analysis works across formats.

- **Configuration last (Phase 7):** Lowest risk, no architectural implications. Can be added anytime after core features stable.

- **Dependencies respected:** Each phase builds on previous. Phase 3 needs Phase 2 (BASS decoding). Phase 4 needs Phase 3 (rendering pipeline). Phase 6 needs Phase 5 (format support + metadata reading).

- **Pitfall prevention front-loaded:** Phases 1-2 address 8 of 10 critical pitfalls. This prevents expensive architectural rewrites.

### Research Flags

**Phases needing deeper research during planning:**

- **Phase 6 (BPM/Key Detection):** Complex audio analysis domain with multiple implementation approaches. Sparse documentation for Windows deployment of analysis tools. Accuracy vs. performance trade-offs need evaluation. STRONGLY RECOMMEND `/gsd:research-phase` to research:
  - Essentia and KeyFinder CLI tool availability/stability on Windows
  - BASS-based analysis feasibility (FFT -> beat tracking, chroma analysis)
  - Expected analysis time for typical 3-5 minute tracks
  - Caching strategy (file hash, invalidation on tag changes)

- **Phase 4 (Frequency Coloring):** May need research if color mapping produces poor visual results. FFT windowing and frequency band thresholds are design decisions that affect usability. OPTIONAL `/gsd:research-phase` if initial implementation doesn't visually distinguish song sections clearly.

- **Phase 3 (Waveform Performance):** May need research if waveform generation is too slow. Downsampling strategies and bucket sizing are performance vs. quality trade-offs. OPTIONAL `/gsd:research-phase` if generation takes >2 seconds for typical files.

**Phases with standard patterns (skip research):**

- **Phase 1 (COM Foundation):** Official Microsoft documentation for IPreviewHandler, established WPF HwndSource patterns, well-known COM interop techniques.

- **Phase 2 (BASS Integration):** BASS.NET has comprehensive documentation, established patterns for initialization and playback, many C# examples available.

- **Phase 5 (Format Support & Metadata):** BASS plugin loading is straightforward, TagLibSharp has standard usage patterns for tag reading.

- **Phase 7 (Configuration):** System.Text.Json and WiX installer are well-documented with standard patterns.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | MEDIUM | Core technologies (.NET 8, WPF, BASS) are well-established. Specific version compatibility (BASS.NET with .NET 8, CsWin32 version) needs NuGet verification. BASS license requirements clearly documented. |
| Features | MEDIUM-HIGH | Table stakes features validated against DAW/DJ software patterns (HIGH confidence). Differentiator value proposition clear. MVP definition aligns with standard preview handler scope. Some uncertainty around BPM/key detection implementation approaches. |
| Architecture | HIGH | IPreviewHandler patterns officially documented by Microsoft. WPF HwndSource interop well-established. COM threading model and lifecycle requirements explicit. Project structure follows proven shell extension organization. |
| Pitfalls | HIGH | Critical pitfalls based on official Microsoft docs (COM lifecycle, low IL, threading). BASS resource management pitfalls are well-known. Security considerations match Microsoft recommendations. Pattern recovery costs validated against similar projects. |

**Overall confidence:** MEDIUM-HIGH

Research provides solid foundation for roadmap creation. Core architecture patterns and critical pitfalls are well-documented with official sources. Main uncertainty is BPM/key detection implementation (Phase 6), which correctly flagged for deeper research during that phase planning. Technology stack choices are sound but need version verification from NuGet/official sources.

### Gaps to Address

**Version verification needed:**
- NuGet package versions for Microsoft.Windows.CsWin32, WriteableBitmapEx, TagLibSharp, CommunityToolkit.Mvvm — check NuGet during Phase 1 planning for latest stable versions compatible with .NET 8
- BASS.NET compatibility with .NET 8 — verify on un4seen.com during Phase 2 planning
- Windows 11 24H2+ preview handler compatibility — test on latest Windows during Phase 1 implementation

**BPM/Key detection approach (Phase 6):**
- Availability of Essentia/KeyFinder Windows binaries in 2026 — research during Phase 6 planning via `/gsd:research-phase`
- BASS-based analysis feasibility — prototype simple beat detection to validate approach
- Expected performance characteristics — benchmark with sample tracks to set user expectations
- Caching invalidation strategy — decide when to re-analyze (file modification date? tag changes?)

**Performance characteristics (Phase 3):**
- Waveform generation time for various file sizes — profile during Phase 3 implementation to determine if caching needed
- Optimal bucket size for typical screen resolutions — experiment to balance quality vs. generation speed
- High-DPI rendering strategy — test on 4K displays during Phase 3, may need multi-resolution approach

**Licensing decision (Phase 0/Planning):**
- BASS license purchase if project will be commercial/shareware — decide before Phase 2 implementation begins
- If project is true freeware (no monetization), document this clearly to justify free BASS usage

**Deployment validation (Phase 7):**
- Windows 11 COM registration changes — test installer on Windows 11 24H2 and latest Insider builds
- .NET 8 Runtime deployment strategy — decide on bundled vs. web installer for prerequisite

## Sources

### Primary (HIGH confidence)

**Official Microsoft Documentation:**
- IPreviewHandler interface specification and lifecycle (https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ipreviewhandler)
- Preview Handlers architecture and registration (https://learn.microsoft.com/en-us/windows/win32/shell/preview-handlers)
- WPF and Win32 interoperation via HwndSource (https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-win32-interoperation)
- COM threading models and apartments (https://learn.microsoft.com/en-us/windows/win32/com/processes--threads--and-apartments)

These sources provide definitive information on COM shell extension architecture, IPreviewHandler requirements (stream initialization, low IL, threading), and WPF hosting patterns. Critical pitfalls related to COM lifecycle, threading, and security are derived directly from these official docs.

**Un4seen BASS Library Documentation:**
- BASS audio library licensing terms (free for freeware, commercial license required otherwise)
- BASS API reference for initialization, stream creation, plugin loading
- BASS.NET wrapper documentation for C# interop

Provides clear information on BASS capabilities (format support, FFT, WASAPI/ASIO), resource management requirements, and licensing obligations.

### Secondary (MEDIUM confidence)

**Established patterns from training data:**
- DAW browser features (Ableton Live, FL Studio, Cubase) — feature expectations for audio preview
- DJ software functionality (Rekordbox, Serato DJ Pro) — BPM/key detection, frequency waveforms
- Windows Quick Look tools (QuickLook, Seer) — file type preview handler patterns
- Shell extension development patterns — COM registration, handle cleanup, performance considerations

These sources inform feature prioritization (table stakes vs. differentiators) and architecture patterns (disposal, threading). Confidence is MEDIUM because specific tool features may have changed since training cutoff, but core patterns are stable.

### Tertiary (LOW confidence - needs verification)

**Library availability and versions:**
- NuGet package current versions (Microsoft.Windows.CsWin32, WriteableBitmapEx, TagLibSharp)
- BASS.NET .NET 8 compatibility status
- Essentia and KeyFinder Windows binary availability for BPM/key detection
- Windows 11 24H2 preview handler API changes

These need verification during implementation. Versions cited in STACK.md are based on training data (knowledge cutoff January 2025) without web verification. Functionality patterns are sound, but specific versions may differ.

---
*Research completed: 2026-02-16*
*Ready for roadmap: Yes*
