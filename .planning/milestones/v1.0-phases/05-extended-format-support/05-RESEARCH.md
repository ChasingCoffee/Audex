# Phase 5: Extended Format Support - Research

**Researched:** 2026-02-17
**Domain:** BASS audio plugins, TagLib# BPM/key metadata, format registration
**Confidence:** HIGH

## Summary

Phase 5 extends the existing BASS-based audio engine with plugin support for AIFF, OGG Vorbis, AAC/M4A, WMA, Opus, and module formats (MOD/XM/IT/S3M), adds BPM and musical key display from file tags, and updates registration scripts. The research reveals that the current architecture is well-suited for this extension: core BASS already handles AIFF, OGG Vorbis, and module formats natively without plugins. Only three additional native DLLs are needed (bass_aac.dll, bassopus.dll, basswma.dll). The BASS plugin system allows transparent format detection through `Bass.PluginLoad`, meaning the existing `Bass.CreateStream` calls continue to work for plugin-supported formats without code changes. Module formats require a different API (`Bass.MusicLoad`) since they are not stream-based. BPM and key metadata can be read from standard ID3v2 TBPM/TKEY frames, Vorbis INITIALKEY/BPM fields, and APE tags using TagLib#'s existing APIs plus format-specific tag access.

**Primary recommendation:** Use `Bass.PluginLoad` for AAC/WMA/Opus (transparent with existing CreateStream calls), use `Bass.MusicLoad` for module formats, extend TagReader to read BPM/key from all tag types with DJ software fallback frames, and add a "Music Info" section to the LayoutRenderer.

<user_constraints>

## User Constraints (from CONTEXT.md)

### Locked Decisions
- All BASS plugin DLLs placed in the same directory as the main DLL (no subfolder)
- Plugin load timing: Claude's discretion (eager vs lazy)
- Missing plugin: waveform and controls grey out, waveform area displays "Format Unavailable: {reason}"
- Only register file extensions whose plugin DLLs are actually present at registration time
- Plugins must be pre-installed -- no runtime downloading
- All formats equally important -- no priority ordering
- Module formats (.mod, .xm, .it, .s3m): simpler mono-color waveform only (no frequency coloring)
- Module format metadata: show what's available, hide fields that don't apply (no N/A)
- Use BASS WMA plugin for WMA (not Windows Media Foundation)
- Use BASS AAC plugin for AAC/M4A (not Windows Media Foundation)
- Plugin load success/failure logged to diag.log only (not surfaced in UI)
- When format can't decode: waveform + playback controls grey out; metadata grid still shows whatever TagLib can read
- Error message includes reason (e.g., "Format Unavailable: OPUS plugin not found")
- New "Music Info" section with visible header, separate from technical metadata
- Section always visible -- show dashes for missing values (placeholder for Phase 6 detection)
- Display order: Key first, then BPM
- Key format: standard notation (Am, C#m, F) -- normalize from raw tag values
- BPM format: whole number only (round to nearest integer)
- Read BPM/key from ALL tag types (ID3v2, Vorbis Comments, APE) -- use whichever has value
- Read Serato, Traktor, and rekordbox custom BPM/key tags as fallbacks
- When multiple sources conflict: most precise value wins
- Display value only -- no source indicator
- Take over from any existing preview handler for registered extensions
- Supported formats list stored in config.ini [Formats] section -- user-editable
- All formats (including module formats) registered by default
- Register both .aif and .aiff for AIFF
- Registration script update deferred to Phase 7 installer -- keep hardcoded for now
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

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope

</user_constraints>

<phase_requirements>

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| FMT-02 | Supports AIFF, OGG, AAC, WMA, OPUS, and M4A playback | AIFF+OGG Vorbis native in BASS; AAC/M4A via bass_aac.dll plugin; WMA via basswma.dll; Opus via bassopus.dll. All use Bass.PluginLoad for transparent CreateStream support. |
| FMT-03 | Supports module formats (.mod, .xm, .it, .s3m) | Built into core BASS via Bass.MusicLoad -- no plugin needed. Supports MOD/XM/IT/S3M/MTM/UMX natively. Different API from CreateStream. |
| FMT-04 | Unsupported formats show clear error message (not crash) | Graceful degradation via try/catch around CreateStream/MusicLoad; grey out waveform+controls, show "Format Unavailable: {reason}" in waveform area, still show TagLib metadata. |
| META-03 | Displays BPM and musical key from existing tags | TagLib# reads TBPM/TKEY (ID3v2), BPM/INITIALKEY (Vorbis), BPM/INITIALKEY (APE). DJ software fallbacks: Serato Autotags GEOB for BPM, standard TBPM/TKEY written by Traktor/Serato. |

</phase_requirements>

## Standard Stack

### Core (already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ManagedBass | 4.0.2 | .NET wrapper for BASS audio | Already used for playback/waveform |
| ManagedBass.Wasapi | 4.0.1 | WASAPI output | Already used for audio output |
| ManagedBass.Flac | 4.0.2 | FLAC plugin | Already loaded |
| ManagedBass.Mix | 4.0.1 | Mixer stream | Already used for sample rate conversion |
| TagLibSharp | 2.3.0 | Tag metadata reader | Already used for title/artist/album |
| ini-parser-netstandard | 2.5.2 | INI config parsing | Already used for config.ini |

### New (Phase 5 additions)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| ManagedBass.Aac | 4.0.2 | AAC/M4A plugin wrapper | Optional -- only needed if using BassAac.CreateStream directly |
| ManagedBass.Wma | 4.0.2 | WMA plugin wrapper | Optional -- only needed if using BassWma.CreateStream directly |
| ManagedBass.Opus | 4.0.2 | Opus plugin wrapper | Optional -- only needed if using BassOpus.CreateStream directly |

### Native DLLs (new -- must be placed alongside bass.dll)
| DLL | Source | Formats | Notes |
|-----|--------|---------|-------|
| bass_aac.dll | un4seen BASS_AAC addon (x64) | AAC, M4A (MP4 audio) | P/Invoke name: "bass_aac" |
| basswma.dll | un4seen BASSWMA addon (x64) | WMA | Windows-only; P/Invoke name: "basswma" |
| bassopus.dll | un4seen BASSOPUS addon (x64) | Opus (.opus), Opus-in-OGG | P/Invoke name: "bassopus" |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Bass.PluginLoad (transparent) | BassAac/BassWma/BassOpus.CreateStream (addon-specific) | PluginLoad is simpler -- existing Bass.CreateStream works unchanged. Addon-specific gives extra format options but requires separate code paths. **Use PluginLoad.** |
| ManagedBass.Aac NuGet package | Just the native bass_aac.dll + Bass.PluginLoad | NuGet package adds managed wrapper with CreateStream overloads, but PluginLoad makes Bass.CreateStream transparent. **NuGet packages are optional** -- only native DLLs are strictly required. |

**Installation (NuGet -- optional):**
```bash
dotnet add package ManagedBass.Aac --version 4.0.2
dotnet add package ManagedBass.Wma --version 4.0.2
dotnet add package ManagedBass.Opus --version 4.0.2
```

**Native DLLs (required):**
Download x64 versions from un4seen.com and place in `src/Audex/native/x64/`:
- `bass_aac.dll` from `bass_aac24.zip`
- `basswma.dll` from `basswm24.zip`
- `bassopus.dll` from `bassopus24.zip`

## Architecture Patterns

### Recommended Changes to Project Structure
```
src/Audex/
  native/x64/
    bass.dll              # existing
    basswasapi.dll        # existing
    bassflac.dll          # existing
    bassmix.dll           # existing
    bass_aac.dll          # NEW
    basswma.dll           # NEW
    bassopus.dll          # NEW
  Audio/
    AudioPlayer.cs        # MODIFY: add PluginLoad, MusicLoad support
    PluginManager.cs      # NEW: centralize plugin loading + format capability tracking
    TagReader.cs           # MODIFY: add BPM/key reading
    MusicKeyNormalizer.cs  # NEW: normalize key notation to standard form
    WaveformGenerator.cs   # MODIFY: handle module format mono-color waveform
    FrequencyColorMapper.cs # unchanged
  FileReader/
    AudioHeaderParserFactory.cs  # MODIFY: route new formats
    AudioFileInfo.cs             # MODIFY: add BPM, Key fields
  UI/
    LayoutRenderer.cs      # MODIFY: add "Music Info" section
    PreviewWindow.cs       # MODIFY: handle grey-out state for unsupported formats
    WaveformRenderer.cs    # MODIFY: show "Format Unavailable" message
  Config/
    AppConfig.cs           # MODIFY: add SupportedFormats list
    ConfigManager.cs       # MODIFY: read [Formats] section
  PreviewHandler/
    AudioPreviewHandler.cs # MODIFY: orchestrate format detection + error handling
scripts/
  register.ps1             # MODIFY: add module format extensions, backup previous handlers
  unregister.ps1            # MODIFY: add module format extensions, restore previous handlers
```

### Pattern 1: Plugin Loading via Bass.PluginLoad (Transparent Format Extension)
**What:** Load native BASS plugin DLLs at startup so `Bass.CreateStream` transparently handles new formats
**When to use:** For all plugin-based formats (AAC, WMA, Opus)
**Why:** The current codebase calls `Bass.CreateStream(ptr, 0, data.Length, BassFlags.Decode | BassFlags.Float)` in both AudioPlayer.LoadFile and WaveformGenerator.Generate. With PluginLoad, these calls automatically detect and decode plugin-supported formats without any code changes to the stream creation path.

```csharp
// Source: BASS documentation - BASS_PluginLoad
// In PluginManager.cs or AudioPlayer.Initialize()
public static class PluginManager
{
    private static readonly Dictionary<string, int> _loadedPlugins = new();
    private static readonly Dictionary<string, string> _pluginFormats = new()
    {
        { "bass_aac",  ".aac,.m4a" },
        { "basswma",   ".wma" },
        { "bassopus",  ".opus" }
    };

    public static void LoadPlugins(string dllDirectory)
    {
        foreach (var kvp in _pluginFormats)
        {
            string pluginName = kvp.Key;
            string pluginPath = Path.Combine(dllDirectory, pluginName + ".dll");

            if (!File.Exists(pluginPath))
            {
                Logger.Info($"[PluginManager] Plugin not found: {pluginPath}");
                continue;
            }

            int handle = Bass.PluginLoad(pluginPath);
            if (handle != 0)
            {
                _loadedPlugins[pluginName] = handle;
                Logger.Info($"[PluginManager] Loaded: {pluginName} (handle={handle})");
            }
            else
            {
                Logger.Error($"[PluginManager] Failed to load {pluginName}: {Bass.LastError}");
            }
        }
    }

    public static bool IsFormatSupported(string extension)
    {
        // AIFF, OGG Vorbis, WAV, MP3, FLAC: always supported (core BASS)
        // MOD/XM/IT/S3M: always supported (core BASS MusicLoad)
        // AAC/M4A: requires bass_aac plugin
        // WMA: requires basswma plugin
        // Opus: requires bassopus plugin
        // ...
    }

    public static string? GetUnsupportedReason(string extension)
    {
        // Returns null if supported, or reason string if not
        // e.g., "OPUS plugin not found"
    }
}
```

### Pattern 2: Module Format Playback via Bass.MusicLoad
**What:** Module formats (MOD/XM/IT/S3M) use a different BASS API than stream-based formats
**When to use:** When file extension matches a known module format
**Critical difference from streams:**
- `Bass.MusicLoad` copies data internally -- GCHandle pinning is NOT needed after the call returns
- MusicLoad returns a "music" handle, not a "stream" handle
- Use `Bass.ChannelGetLength`, `Bass.ChannelBytes2Seconds`, `Bass.ChannelGetPosition`, `Bass.ChannelSetPosition` the same way (they work on both music and stream handles)
- Use `Bass.MusicFree` instead of `Bass.StreamFree` to release

```csharp
// Source: BASS documentation - BASS_MusicLoad
// Module format loading (in AudioPlayer.cs)
public (int SampleRate, int Channels, double DurationSeconds) LoadModuleFile(byte[] data, string fileName)
{
    // MusicLoad copies data internally -- no GCHandle needed
    int music = Bass.MusicLoad(data, 0, data.Length,
        BassFlags.Decode | BassFlags.Float | BassFlags.MusicPrescan,
        0); // freq=0 means use Bass.Init frequency

    if (music == 0)
        throw new InvalidOperationException($"BASS MusicLoad failed: {Bass.LastError}");

    // Channel operations work the same as streams
    Bass.ChannelGetInfo(music, out ChannelInfo info);
    double duration = Bass.ChannelBytes2Seconds(music, Bass.ChannelGetLength(music));

    // Add to mixer same as decode streams
    BassMix.MixerAddChannel(_mixerStream, music,
        BassFlags.MixerChanDownMix | BassFlags.MixerChanPause);

    return (info.Frequency, info.Channels, duration);
}
```

### Pattern 3: BPM/Key Tag Reading with DJ Software Fallbacks
**What:** Read BPM and musical key from multiple tag sources with priority resolution
**When to use:** For every file loaded, after TagLib reads standard tags

```csharp
// Source: TagLib# API, Serato/Traktor tag documentation
// In TagReader.cs -- extend ReadTags or add new method
public static MusicInfo ReadMusicInfo(byte[] data, string fileName)
{
    string? bpm = null;
    string? key = null;

    using var abstraction = new ByteArrayFileAbstraction(fileName, data);
    using var tagFile = TagLib.File.Create(abstraction);

    // 1. Standard tags first (most common)
    // ID3v2: TBPM frame, TKEY frame
    var id3v2 = tagFile.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
    if (id3v2 != null)
    {
        bpm = GetId3v2TextFrame(id3v2, "TBPM");
        key = GetId3v2TextFrame(id3v2, "TKEY");
    }

    // Vorbis Comments: BPM, INITIALKEY fields
    var xiph = tagFile.GetTag(TagLib.TagTypes.Xiph) as TagLib.Ogg.XiphComment;
    if (xiph != null)
    {
        bpm ??= xiph.GetFirstField("BPM");
        key ??= xiph.GetFirstField("INITIALKEY");
    }

    // APE: BPM, INITIALKEY fields
    var ape = tagFile.GetTag(TagLib.TagTypes.Ape) as TagLib.Ape.Tag;
    if (ape != null)
    {
        bpm ??= GetApeField(ape, "BPM");
        key ??= GetApeField(ape, "INITIALKEY");
    }

    // 2. DJ software fallbacks (TXXX user-defined text frames)
    if (id3v2 != null && (bpm == null || key == null))
    {
        // Serato writes to standard TBPM/TKEY -- already captured above
        // Serato Autotags GEOB contains BPM as ASCII string
        if (bpm == null)
            bpm = ReadSeratoAutotagsBpm(id3v2);

        // Mixed in Key, Traktor may use TXXX frames
        // Check common TXXX descriptions
    }

    // 3. Normalize values
    int? bpmInt = NormalizeBpm(bpm);
    string? normalizedKey = MusicKeyNormalizer.Normalize(key);

    return new MusicInfo(bpmInt, normalizedKey);
}
```

### Pattern 4: Key Normalization
**What:** Convert various key representations to standard notation (Am, C#m, F, etc.)
**When to use:** Before displaying key values from tags

```csharp
// Key normalization lookup -- handles Camelot, Open Key, and text variations
public static class MusicKeyNormalizer
{
    // Standard notation: C, C#, D, Eb, E, F, F#, G, Ab, A, Bb, B
    // Minor: Cm, C#m, Dm, Ebm, Em, Fm, F#m, Gm, Abm, Am, Bbm, Bm

    // Camelot to Standard mapping (for files analyzed by Mixed in Key)
    private static readonly Dictionary<string, string> CamelotMap = new()
    {
        { "1A", "Abm" }, { "1B", "B" },
        { "2A", "Ebm" }, { "2B", "F#" },
        { "3A", "Bbm" }, { "3B", "Db" },
        { "4A", "Fm" },  { "4B", "Ab" },
        { "5A", "Cm" },  { "5B", "Eb" },
        { "6A", "Gm" },  { "6B", "Bb" },
        { "7A", "Dm" },  { "7B", "F" },
        { "8A", "Am" },  { "8B", "C" },
        { "9A", "Em" },  { "9B", "G" },
        { "10A", "Bm" }, { "10B", "D" },
        { "11A", "F#m" },{ "11B", "A" },
        { "12A", "C#m" },{ "12B", "E" },
    };

    // Open Key to Standard mapping
    private static readonly Dictionary<string, string> OpenKeyMap = new()
    {
        { "1d", "C" },   { "1m", "Am" },
        { "2d", "G" },   { "2m", "Em" },
        { "3d", "D" },   { "3m", "Bm" },
        { "4d", "A" },   { "4m", "F#m" },
        { "5d", "E" },   { "5m", "C#m" },
        { "6d", "B" },   { "6m", "Abm" },
        { "7d", "F#" },  { "7m", "Ebm" },
        { "8d", "Db" },  { "8m", "Bbm" },
        { "9d", "Ab" },  { "9m", "Fm" },
        { "10d", "Eb" }, { "10m", "Cm" },
        { "11d", "Bb" }, { "11m", "Gm" },
        { "12d", "F" },  { "12m", "Dm" },
    };

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string trimmed = raw.Trim();

        // Already standard notation? (e.g., "Am", "C#m", "F")
        if (IsStandardNotation(trimmed)) return trimmed;

        // Camelot? (e.g., "8A", "11B")
        if (CamelotMap.TryGetValue(trimmed.ToUpperInvariant(), out var camelotResult))
            return camelotResult;

        // Open Key? (e.g., "1d", "8m")
        if (OpenKeyMap.TryGetValue(trimmed.ToLowerInvariant(), out var openKeyResult))
            return openKeyResult;

        // Text variations: "a minor" -> "Am", "C# minor" -> "C#m"
        // ... pattern matching with regex
        return trimmed; // Return as-is if unrecognized
    }
}
```

### Pattern 5: Module Format Waveform (Mono-Color)
**What:** Module formats get simpler waveform without frequency coloring
**When to use:** When file is detected as module format

```csharp
// In WaveformGenerator.cs -- skip FFT analysis for module formats
public static WaveformData? Generate(byte[] audioData, CancellationToken ct,
    bool isModuleFormat = false, Action<int, float>? onBarReady = null)
{
    // ... existing peak generation code ...

    if (!isModuleFormat)
    {
        // Existing FFT frequency color analysis
        int fftBytesRead = Bass.ChannelGetData(waveStream, fftBuffer, (int)DataFlags.FFT2048);
        // ...
    }
    // Module formats: peaks only, no FrequencyColors
    // WaveformRenderer already handles null FrequencyColors (falls back to mono color)
}
```

### Anti-Patterns to Avoid
- **Separate CreateStream code paths per format:** Don't use BassAac.CreateStream / BassWma.CreateStream / BassOpus.CreateStream. Use Bass.PluginLoad + standard Bass.CreateStream for uniform handling.
- **Mixing MusicLoad and StreamFree:** Module handles must use `Bass.MusicFree`, not `Bass.StreamFree`. Track whether the current handle is a music or stream handle.
- **Pinning memory for module formats:** `Bass.MusicLoad(byte[])` copies data internally. Don't pin with GCHandle -- it's wasted and the pin will outlive the need.
- **Reading tags only from ID3v2:** Files can have Vorbis Comments (OGG/FLAC), APE tags (APE/WavPack/MOD), or multiple tag types. Read from all available types.
- **Hardcoding key notation:** DJ files use Camelot (8A), Open Key (1d), text ("A minor"), and standard (Am). Must normalize all to standard notation.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| AAC/M4A decoding | Custom decoder | BASS bass_aac plugin | AAC has multiple profiles/containers (ADTS, MP4/M4A), plugin handles all |
| WMA decoding | Custom decoder | BASS basswma plugin | WMA requires Windows Media Format SDK, plugin wraps it |
| Opus decoding | Custom decoder | BASS bassopus plugin | Opus in OGG container requires proper framing, plugin handles it |
| Module format playback | Custom MOD player | BASS MusicLoad (built-in) | IT/XM/S3M/MOD have dozens of effects/commands; BASS uses XMPlay engine |
| ID3v2 frame parsing | Custom binary parser | TagLib# GetFrames API | ID3v2 has v2.3, v2.4, encoding variations, unsync; TagLib handles all |
| Key notation conversion | Simple string replace | Full Camelot/OpenKey/text lookup table | 24 keys x 3 notations x text variations = hundreds of edge cases |
| Serato GEOB parsing | Skip it | Binary parse of Serato Autotags format | Simple ASCII-in-binary format; worth supporting for accurate BPM |

**Key insight:** BASS's plugin architecture is designed exactly for this use case -- transparent format extension. The existing Bass.CreateStream calls will "just work" once plugins are loaded. Module formats are the only exception requiring a different API path.

## Common Pitfalls

### Pitfall 1: MusicLoad vs StreamFree Mismatch
**What goes wrong:** Calling `Bass.StreamFree(handle)` on a handle returned by `Bass.MusicLoad` causes undefined behavior or silent failure.
**Why it happens:** BASS has two distinct handle namespaces -- music handles and stream handles. They share channel operations (GetPosition, SetPosition, GetInfo, ChannelGetData) but have different free functions.
**How to avoid:** Track whether the current handle is a music or stream handle. Call `Bass.MusicFree()` for module formats, `Bass.StreamFree()` for everything else.
**Warning signs:** Module files play once but can't replay, or BASS error codes on unload.

### Pitfall 2: Module Format GCHandle Pinning
**What goes wrong:** Unnecessarily pinning byte[] with GCHandle for module files, leading to memory that stays pinned until explicitly freed.
**Why it happens:** The existing AudioPlayer.LoadFile pattern pins data for stream lifetime. MusicLoad does NOT need this -- it copies data internally.
**How to avoid:** Use the `Bass.MusicLoad(byte[], long, int, BassFlags, int)` overload directly. No GCHandle needed. BASS documentation confirms "you can do whatever you want with the memory" after MusicLoad returns.
**Warning signs:** Unnecessary Gen2 GC pressure, memory pinned longer than needed.

### Pitfall 3: OGG Vorbis vs OGG Opus Container Confusion
**What goes wrong:** Assuming all .ogg files are Vorbis, or that BASSOPUS handles all .ogg files.
**Why it happens:** .ogg is a container format. It can contain Vorbis (core BASS handles), Opus (needs BASSOPUS plugin), or FLAC (needs BASSFLAC plugin -- already loaded).
**How to avoid:** BASS handles this automatically when plugins are loaded. `Bass.CreateStream` tries core decoders first, then loaded plugins in order. No special detection code needed -- just ensure BASSOPUS is loaded before attempting .ogg files that might contain Opus.
**Warning signs:** .ogg files that fail with "unknown format" despite OGG being listed as supported.

### Pitfall 4: Plugin DLL Not Found in prevhost.exe Context
**What goes wrong:** Plugins load fine in test console but fail when running inside prevhost.exe (Explorer's preview host).
**Why it happens:** prevhost.exe has its own working directory. The BASS plugin path must be absolute or the DLL search path must include the assembly directory.
**How to avoid:** The existing code already calls `SetDllDirectory(assemblyDir)` in AudioPlayer.Initialize(). Use absolute paths with `Bass.PluginLoad(Path.Combine(assemblyDir, "bass_aac.dll"))` to be safe.
**Warning signs:** Plugins load in debug but not when registered as preview handler.

### Pitfall 5: BPM Precision Conflicts
**What goes wrong:** Standard TBPM has "120" while Serato Autotags has "119.97", and displaying "120" loses precision.
**Why it happens:** Standard ID3v2 TBPM is text with no format requirement. DJ tools often store fractional BPM. Manual entries are typically rounded.
**How to avoid:** Parse all BPM sources as doubles. Per user decision: "most precise value wins" -- prefer the value with more decimal places. Then round to integer for display.
**Warning signs:** BPM shows 120 when the actual analyzed BPM is 119.97 (user expects 120).

### Pitfall 6: Registration Backup/Restore Atomicity
**What goes wrong:** Backup file is written but registration fails partway, leaving some extensions with our handler and some without.
**Why it happens:** Registry operations are not transactional. If the script is interrupted, state is inconsistent.
**How to avoid:** Write backup BEFORE modifying any registry keys. On unregister, read backup and restore per-extension. Handle missing backup gracefully (just remove our handler).
**Warning signs:** After unregister, some file types have no preview handler at all.

## Code Examples

### Loading All Plugins at Startup
```csharp
// Source: BASS PluginLoad documentation, existing AudioPlayer.Initialize pattern
// Call after Bass.Init(0) in AudioPlayer.Initialize()

string assemblyDir = Path.GetDirectoryName(typeof(AudioPlayer).Assembly.Location);

// Plugin DLLs must be in same directory as bass.dll
string[] plugins = { "bass_aac", "basswma", "bassopus" };

foreach (string plugin in plugins)
{
    string path = Path.Combine(assemblyDir, plugin + ".dll");
    if (File.Exists(path))
    {
        int handle = Bass.PluginLoad(path);
        if (handle != 0)
            Logger.Info($"[AudioPlayer] Plugin loaded: {plugin}");
        else
            Logger.Error($"[AudioPlayer] Plugin failed: {plugin} - {Bass.LastError}");
    }
    else
    {
        Logger.Info($"[AudioPlayer] Plugin not present: {plugin}");
    }
}
// After this, Bass.CreateStream automatically handles AAC, WMA, Opus
```

### Reading BPM from ID3v2 TBPM Frame
```csharp
// Source: TagLib# API - TextInformationFrame.Get
private static string? GetId3v2TextFrame(TagLib.Id3v2.Tag tag, string frameId)
{
    var frames = tag.GetFrames(new TagLib.ByteVector(frameId));
    if (frames.Count == 0) return null;

    var textFrame = frames[0] as TagLib.Id3v2.Frames.TextInformationFrame;
    if (textFrame == null || textFrame.Text == null || textFrame.Text.Length == 0)
        return null;

    string value = textFrame.Text[0];
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

### Reading Serato Autotags BPM from GEOB Frame
```csharp
// Source: Holzhaus/serato-tags documentation
// Serato Autotags GEOB: header (2 bytes) + BPM as null-terminated ASCII + gain values
private static string? ReadSeratoAutotagsBpm(TagLib.Id3v2.Tag tag)
{
    var geobFrames = tag.GetFrames(new TagLib.ByteVector("GEOB"));
    foreach (var frame in geobFrames)
    {
        var geob = frame as TagLib.Id3v2.Frames.GeneralEncapsulatedObjectFrame;
        if (geob == null) continue;
        if (geob.Description != "Serato Autotags") continue;

        byte[] data = geob.Object?.Data;
        if (data == null || data.Length < 4) continue;

        // Skip 2-byte header, read null-terminated ASCII string (BPM)
        int start = 2;
        int end = Array.IndexOf(data, (byte)0, start);
        if (end < 0) end = data.Length;

        string bpmStr = System.Text.Encoding.ASCII.GetString(data, start, end - start);
        if (!string.IsNullOrWhiteSpace(bpmStr))
            return bpmStr;
    }
    return null;
}
```

### Reading Vorbis Comment Fields
```csharp
// Source: TagLib# Ogg.XiphComment API
private static string? GetXiphField(TagLib.Ogg.XiphComment tag, string fieldName)
{
    string[] values = tag.GetField(fieldName);
    if (values == null || values.Length == 0) return null;
    return string.IsNullOrWhiteSpace(values[0]) ? null : values[0].Trim();
}

// Usage:
var xiph = tagFile.GetTag(TagLib.TagTypes.Xiph) as TagLib.Ogg.XiphComment;
if (xiph != null)
{
    string? bpm = GetXiphField(xiph, "BPM");
    string? key = GetXiphField(xiph, "INITIALKEY");
}
```

### Updated csproj for New Native DLLs
```xml
<!-- Add to Audex.csproj ItemGroup for Content -->
<Content Include="native\x64\bass_aac.dll">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  <Link>bass_aac.dll</Link>
</Content>
<Content Include="native\x64\basswma.dll">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  <Link>basswma.dll</Link>
</Content>
<Content Include="native\x64\bassopus.dll">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  <Link>bassopus.dll</Link>
</Content>
```

### Previous Handler Backup/Restore in PowerShell
```powershell
# Backup existing handlers before registration
$backupPath = Join-Path $env:LOCALAPPDATA "Audex\prev-handlers.json"
$backup = @{}

foreach ($ext in $extensions) {
    $sysAssocPath = "Registry::HKCR\SystemFileAssociations\$ext\shellex\$previewHandlerIID"
    if (Test-Path $sysAssocPath) {
        $existingClsid = (Get-ItemProperty $sysAssocPath).'(default)'
        if ($existingClsid -and $existingClsid -ne $clsid) {
            $backup[$ext] = $existingClsid
        }
    }
}

$backupDir = Split-Path $backupPath
if (-not (Test-Path $backupDir)) { New-Item $backupDir -ItemType Directory -Force | Out-Null }
$backup | ConvertTo-Json | Set-Content $backupPath -Encoding UTF8
```

## Discretion Recommendations

### Plugin Load Timing: Eager at Startup
**Recommendation:** Load all plugins eagerly during `AudioPlayer.Initialize()`, immediately after `Bass.Init(0)`.
**Rationale:** Plugins are tiny DLLs (50-200KB each). Loading takes <1ms each. Eager loading means `Bass.CreateStream` is ready for any format on first file. Lazy loading would require tracking which plugins are loaded, re-trying on failure, and adds complexity with no measurable benefit.

### Format Detection: Extension-Based + Existing Magic Bytes Fallback
**Recommendation:** Keep the existing `AudioHeaderParserFactory.DetectFormatFromStream` magic bytes logic as fallback, extend it with AIFF/M4A/WMA signatures, but primarily rely on file extension for routing.
**Rationale:** The file extension is available from IStream.Stat (or the filename passed by Explorer). Magic bytes are only needed when the filename is "Unknown". The existing code already handles this pattern. For BASS, format detection is automatic -- `Bass.CreateStream` + loaded plugins try all decoders. The extension is mainly needed for the header parser and for deciding between CreateStream vs MusicLoad.

### Header Parser Extension: Delegate to BASS for New Formats
**Recommendation:** For new formats (AIFF, OGG, AAC, WMA, Opus, M4A), don't write custom header parsers. Instead, let BASS provide sample rate/channels/duration via `Bass.ChannelGetInfo` and `Bass.ChannelBytes2Seconds`. The existing pattern in `AudioPreviewHandler.DoPreviewInternal` already overrides header-parsed values with BASS values.
**Rationale:** Writing header parsers for AAC (ADTS/MP4), WMA (ASF), and Opus (OGG framing) is complex and error-prone. BASS already parses these headers correctly. The only value from custom parsers was bit depth (for WAV/FLAC), which is less meaningful for compressed formats.

### Corrupt/Partially-Decodable Files: Show Error from BASS
**Recommendation:** If `Bass.CreateStream` or `Bass.MusicLoad` returns 0, show "Format Unavailable: {Bass.LastError description}". If stream creation succeeds but waveform generation fails partway, show whatever waveform was generated (partial peaks).
**Rationale:** BASS already validates file headers during CreateStream. Partial decoding errors during waveform generation are already handled by the existing WaveformGenerator (returns null on failure, which triggers "Waveform unavailable").

### Unrecognized File Types: Let Explorer Handle
**Recommendation:** If the file extension is not in our supported formats list, don't even attempt loading. Return without showing our preview (let Explorer fall back to its default handler or show nothing).
**Rationale:** The COM IPreviewHandler contract means Explorer only sends us files matching our registered extensions. If we receive an unrecognized extension, something is wrong with registration -- better to fail silently than show a confusing error.

### License Switching: No Special Design Needed
**Recommendation:** Don't add abstraction layers for license switching. The plugin system already isolates format support by DLL presence.
**Rationale:** BASS has a free non-commercial license. If commercial licensing is needed later, the same DLLs are used -- only the license key changes (via `Bass.UpdatePeriod` or embedded key). No architecture change needed.

### Additional Format Extensions
**Recommendation:** Also register `.aif` (alias for AIFF, per user decision) and `.m4a` (MP4 audio, handled by bass_aac). Do NOT add obscure formats like .mpc, .ape, .tta, .ofr unless specifically requested.
**Rationale:** Focus on formats users actually encounter. The config.ini [Formats] section allows power users to add more later.

### OGG Container Detection
**Recommendation:** Register .ogg extension and let BASS handle codec detection automatically. BASS tries core OGG Vorbis decoder first, then BASSOPUS plugin for Opus-in-OGG.
**Rationale:** BASS's plugin chain handles this transparently. No special code needed for codec detection within OGG containers.

### Config Reload: Re-registration Required
**Recommendation:** Config.ini format changes require re-running the register script. No runtime hot-reload of format list.
**Rationale:** Shell extension format associations are in the Windows registry. Changing which extensions we handle requires registry modifications, which require admin elevation. Runtime reload is not possible for the registration aspect.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Format-specific CreateStream calls | Bass.PluginLoad + standard CreateStream | Always available in BASS | Simpler code, automatic format detection |
| Custom MOD players | BASS MusicLoad (XMPlay engine) | Built into BASS | Professional-quality module playback |
| Manual ID3v2 binary parsing | TagLib# 2.3.0 frame-level API | TagLibSharp 2.x | Safe access to any ID3v2 frame type |

**Deprecated/outdated:**
- Bass.NET (radio42): Older .NET wrapper, superseded by ManagedBass
- FMOD for module playback: BASS's built-in module support is equivalent quality
- Windows Media Foundation for WMA: User decision explicitly chose BASS WMA plugin instead

## Open Questions

1. **Serato Autotags GEOB Binary Parsing Reliability**
   - What we know: Format is documented (2-byte header + null-terminated ASCII BPM + gain values)
   - What's unclear: Whether all Serato versions use identical format, edge cases with unicode
   - Recommendation: Implement with try/catch, fall back to standard TBPM if parsing fails. LOW risk -- format is simple.

2. **Traktor PRIV Frame BPM/Key Access**
   - What we know: Traktor stores BPM/key in a proprietary binary PRIV frame ("Traktor4")
   - What's unclear: Binary format is not publicly documented
   - Recommendation: Don't parse Traktor PRIV frames. Traktor ALSO writes standard TBPM/TKEY frames, which we already read. The PRIV frame is redundant for our use case.

3. **Rekordbox Tag Writing Behavior**
   - What we know: Rekordbox writes TKEY to ID3 tags (when enabled in settings) but does NOT write TBPM
   - What's unclear: Whether rekordbox-analyzed files have BPM in any tag at all
   - Recommendation: Accept this limitation. If rekordbox users want BPM in tags, they need to use a third-party tool (Mp3tag, tuneXplorer). Our Phase 6 BPM detection will cover this gap.

4. **ManagedBass.MusicLoad Exact Method Signature**
   - What we know: BASS.NET has `Bass.BASS_MusicLoad(IntPtr, long, int, BASSFlag, int)` and byte[] overloads
   - What's unclear: Exact ManagedBass 4.0.2 method name (may be `Bass.MusicLoad` per wrapper conventions)
   - Recommendation: Verify at implementation time by checking ManagedBass API. If no managed overload exists, use P/Invoke with GCHandle pinning as fallback (even though BASS copies data, the P/Invoke call itself needs valid memory).

## Sources

### Primary (HIGH confidence)
- [un4seen BASS documentation](https://www.un4seen.com/bass.html) - Core format support (AIFF, OGG, MOD built-in), plugin list, PluginLoad API
- [BASS_PluginLoad docs](https://www.un4seen.com/doc/bass/BASS_PluginLoad.html) - Plugin loading mechanism, transparent format extension
- [BASS_MusicLoad docs](https://documentation.help/BASS/BASS_MusicLoad.html) - Module format loading API, memory handling
- [ManagedBass GitHub](https://github.com/ManagedBass/ManagedBass) - .NET wrapper source, NuGet packages
- [ManagedBass NuGet profile](https://www.nuget.org/profiles/ManagedBass) - Package versions (all 4.0.2, updated Oct 2025)
- [TagLib# GitHub](https://github.com/mono/taglib-sharp) - ID3v2 frame access, XiphComment API, GEOB frames
- [Holzhaus/serato-tags](https://github.com/Holzhaus/serato-tags) - Serato Autotags GEOB binary format documentation
- Existing codebase: AudioPlayer.cs, TagReader.cs, AudioHeaderParserFactory.cs, WaveformGenerator.cs

### Secondary (MEDIUM confidence)
- [Abyssmedia BPM/Key metadata reference](https://www.abyssmedia.com/tunexplorer/bpm-key-metadata.shtml) - Tag field names across formats (TBPM, TKEY, BPM, INITIALKEY)
- [Native Instruments community](https://community.native-instruments.com/discussion/45433) - Traktor writes TBPM/TKEY to standard ID3 tags
- [Pioneer DJ community](https://forums.pioneerdj.com/hc/en-us/community/posts/203052319) - Rekordbox writes TKEY but NOT TBPM to ID3 tags
- [DeepWiki TagLib# API](https://deepwiki.com/mono/taglib-sharp/7-api-reference-and-usage) - Code examples for ID3v2 frame access, TXXX, GEOB

### Tertiary (LOW confidence)
- [Mp3tag community on Traktor PRIV frame](https://community.mp3tag.de/t/decoding-the-traktor-4-binary-data-inside-a-priv-tag/54352) - Traktor proprietary binary format (not needed -- standard tags suffice)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Verified against existing codebase, un4seen docs, NuGet packages
- Architecture (PluginLoad): HIGH - BASS documentation explicitly describes this pattern; verified DLL naming
- Architecture (MusicLoad): HIGH - BASS documentation confirms memory copying, separate free function
- Architecture (BPM/Key tags): MEDIUM - Standard tag fields verified; Serato GEOB format documented by third party
- Pitfalls: HIGH - Based on BASS documentation and COM shell extension experience from earlier phases
- Key normalization: MEDIUM - Camelot/OpenKey mappings from multiple DJ community sources; 24 keys verified
- Native DLL filenames: HIGH - Confirmed: bass_aac.dll, basswma.dll, bassopus.dll

**Research date:** 2026-02-17
**Valid until:** 2026-03-17 (stable libraries, no rapid changes expected)
