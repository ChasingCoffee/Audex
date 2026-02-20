# Phase 2: BASS Audio Integration - Research

**Researched:** 2026-02-16
**Domain:** Audio playback (BASS/WASAPI), tag reading (TagLib#), owner-drawn WinForms controls
**Confidence:** MEDIUM-HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Playback Controls
- Play/Pause toggle + separate Stop button (stop resets position to start)
- Icon buttons only (unicode symbols), no text labels — compact
- All transport buttons same size (no oversized play button)
- Bottom bar with subtle separator line from content above
- Layout: seek bar full width on top row, buttons centered + volume on right on bottom row
- Seek bar above buttons, with elapsed time flanking left and total time flanking right (e.g. 1:23 ━━━━ 4:56)
- Custom-drawn seek bar (owner-drawn, theme-aware) — not WinForms TrackBar
- Highlight on hover for buttons, subtle press effect on click
- Bottom bar follows Explorer dark/light theme (same theming as Phase 1)

#### Playback Behavior
- Manual play only — no autoplay (autoplay toggle comes in Phase 7)
- When track finishes: stop and reset position to start, button shows Play
- When switching files while playing: immediately stop current, load new file ready to play (no crossfade)

#### Metadata Display
- Technical info is most prominent (sample rate, bit depth, channels, format)
- Grid/table layout for metadata (two-column label-value pairs)
- Positioned below filename header, filling main content area above bottom control bar
- Missing tag fields (title/artist/album) are hidden entirely — only show tags that exist

#### Volume Behavior
- Horizontal volume slider on right side of bottom bar button row
- Custom-drawn slider matching seek bar style (theme-aware)
- Speaker icon with mute toggle (click to mute/unmute)
- Speaker icon: 2 states only — muted (X) and unmuted (speaker)
- No volume percentage text — slider position only
- Volume persists across sessions (saved to config file, restored on next launch)
- Default volume on first use: 50%

#### BASS Library Setup
- bass.dll bundled directly in project, deployed in same directory as handler DLL
- ManagedBass NuGet package for .NET wrapper
- WASAPI shared mode output (doesn't block other apps)
- BASS initialized once when handler loads, kept alive until unload (not per-file)
- BASS license: freeware for development, evaluate commercial license before public release
- If BASS fails to initialize: full error panel replacing entire preview content with troubleshooting hint

#### Tag & Metadata Sources
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

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| PLAY-01 | User can play, pause, and stop audio playback | BASS ChannelPlay/ChannelPause/ChannelStop + UI timer for position updates |
| PLAY-03 | User can adjust volume independently of system volume | BassWasapi.SetVolume() + custom-drawn volume slider |
| FMT-01 | Supports WAV, MP3, and FLAC playback | Bass.CreateStream (WAV/MP3 native) + BassFlac plugin for FLAC |
| META-01 | Displays duration, sample rate, bit depth, and channel count | Bass.ChannelGetInfo() + Bass.ChannelGetLength() / Bass.ChannelBytes2Seconds() |
| META-02 | Displays title, artist, and album from ID3/Vorbis tags | TagLib# via IFileAbstraction pattern over copied byte buffer |
</phase_requirements>

---

## Summary

BASS 2.4 natively supports WAV and MP3 without plugins. FLAC requires the separate `bassflac.dll` plugin (ManagedBass.Flac NuGet package). WASAPI output requires the separate `basswasapi.dll` plugin (ManagedBass.Wasapi NuGet package). The correct WASAPI initialization sequence is: `Bass.Init(0)` with the "No Sound" device, load plugins, then `BassWasapi.Init(-1, 0, 0, WasapiInitFlags.Shared)` to open the default output device in shared mode. A WASAPI procedure callback pulls decoded PCM float samples from a decode-only BASS stream and writes them to the WASAPI output buffer.

For IStream handling (Claude's discretion): the recommended approach is to copy the IStream bytes into a `MemoryStream`/byte array on first load, then create a BASS decode stream from the byte buffer in memory. This avoids temp file cleanup complexity and works safely in the low-integrity prevhost.exe sandbox. The same byte buffer feeds both BASS (for playback + technical metadata) and TagLib# via `IFileAbstraction` (for tags), with no double-copy needed.

The existing `PreviewWindow` is a double-buffered owner-drawn UserControl — the bottom bar controls (seek bar, transport buttons, volume slider) extend the same GDI+ pattern already established in Phase 1. A `System.Windows.Forms.Timer` (not `System.Threading.Timer`) is used for position polling (every 250ms) because it fires on the STA UI thread, avoiding `Control.Invoke` calls.

**Primary recommendation:** Use ManagedBass 4.0.2 + ManagedBass.Wasapi 4.0.1 + ManagedBass.Flac 4.0.2 + TagLibSharp 2.3.0. Initialize BASS once with device 0 (NoSound) at handler construction, create per-file decode streams, feed them to a persistent WASAPI output via WasapiProcedure callback.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ManagedBass | 4.0.2 | .NET wrapper for BASS audio (WAV/MP3 playback, channel APIs) | Official managed wrapper, targets net48/netstandard2.0, actively maintained (Oct 2025) |
| ManagedBass.Wasapi | 4.0.1 | WASAPI shared-mode output plugin wrapper | Required separate plugin — WASAPI is NOT built into base BASS |
| ManagedBass.Flac | 4.0.2 | FLAC playback plugin wrapper | Required separate plugin — FLAC is NOT built into base BASS |
| TagLibSharp | 2.3.0 | Read ID3v2/Vorbis comment/FLAC tags (title, artist, album) | Only maintained C# tag library with LGPL license and broad format support |

### NuGet Installation
```xml
<PackageReference Include="ManagedBass" Version="4.0.2" />
<PackageReference Include="ManagedBass.Wasapi" Version="4.0.1" />
<PackageReference Include="ManagedBass.Flac" Version="4.0.2" />
<PackageReference Include="TagLibSharp" Version="2.3.0" />
```

### Native DLLs to Bundle (x64 builds, same directory as Audex.dll)
| DLL | Source | Purpose |
|-----|--------|---------|
| bass.dll | un4seen.com download (v2.4.18.3) | Core BASS audio engine |
| basswasapi.dll | un4seen.com download | WASAPI output support |
| bassflac.dll | un4seen.com download | FLAC decoding |

**IMPORTANT:** The NuGet packages contain the .NET P/Invoke wrappers only. The native `.dll` files must be downloaded from [un4seen.com](https://www.un4seen.com/bass.html) and placed in the project's output directory alongside `Audex.dll`. This is where prevhost.exe will find them via `LoadLibrary` search order.

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| TagLibSharp | BASS ChannelGetTags | BASS tag reading is limited; TagLib# supports all formats and is user's locked decision |
| ManagedBass.Wasapi | Bass.Init with real device | Real device occupies exclusive access risk; WASAPI plugin handles shared mode correctly |
| MemoryStream approach | Temp file in %TEMP% | Temp files require cleanup and may fail in low-integrity sandbox; memory is simpler and safe |

---

## Architecture Patterns

### Recommended Project Structure Changes
```
src/Audex/
├── Audio/                      # NEW - all BASS playback logic
│   ├── AudioPlayer.cs          # BASS init, WASAPI, stream lifecycle
│   ├── AudioPlayerState.cs     # Enum: Idle/Loading/Playing/Paused/Stopped
│   └── TagReader.cs            # TagLib# IFileAbstraction, tag extraction
├── UI/
│   ├── PreviewWindow.cs        # MODIFIED - wire up audio player events
│   ├── LayoutRenderer.cs       # MODIFIED - metadata grid, remove placeholders
│   ├── ControlBarRenderer.cs   # NEW - seek bar, buttons, volume slider drawing
│   ├── ThemeHelper.cs          # EXISTING - add ControlBar color helpers
│   └── ...
├── Config/
│   └── AppConfig.cs            # MODIFIED - add Volume float, IsMuted bool
```

### Pattern 1: BASS/WASAPI Initialization (Once Per Handler Lifetime)

**What:** Initialize BASS with NoSound device, load plugins, open WASAPI shared output. Called once in `AudioPreviewHandler` constructor or first `DoPreview`.
**When to use:** On handler construction; keep alive until `Unload()` is called on the handler.

```csharp
// Source: BASS documentation (un4seen.com) + ManagedBass GitHub
// In AudioPlayer.Initialize():

// 1. Load add-on plugins before Bass.Init
BassFlac.Load();   // loads bassflac.dll from same directory
// BassWasapi loads automatically when BassWasapi.Init is called

// 2. Init BASS with NoSound device (device=0)
// WASAPI requires NoSound device, not a real output device
if (!Bass.Init(0))
{
    // Bass.LastError has the error code
    throw new InvalidOperationException($"BASS init failed: {Bass.LastError}");
}

// 3. Init WASAPI shared mode on default output device (-1)
// freq=0 and chans=0 = use WASAPI mixer's native format
if (!BassWasapi.Init(-1, 0, 0, WasapiInitFlags.Shared, 0f, 0f,
    _wasapiProc, IntPtr.Zero))
{
    throw new InvalidOperationException($"WASAPI init failed: {BassWasapi.LastError}");
}
BassWasapi.Start();  // start the WASAPI output (no-op until proc fires)
```

### Pattern 2: Per-File Stream Loading (Decode-Only Stream from Memory)

**What:** Copy IStream to byte array once. Create BASS decode stream from memory. Extract technical metadata via ChannelGetInfo. Extract tags via TagLib#. Ready to play.
**When to use:** In `DoPreviewInternal()`, replaces direct header-parser approach.

```csharp
// Source: ManagedBass API docs + TagLib# IFileAbstraction pattern

// Step 1: Copy IStream to byte array (done once, reused for both BASS and TagLib#)
private byte[] CopyStreamToBytes(IStream pstream)
{
    using (var ms = new MemoryStream())
    {
        var buffer = new byte[65536];
        while (true)
        {
            pstream.Read(buffer, buffer.Length, out int bytesRead);
            if (bytesRead <= 0) break;
            ms.Write(buffer, 0, bytesRead);
        }
        return ms.ToArray();
    }
}

// Step 2: Create BASS decode stream from byte array pinned in memory
// BassFlags.Decode = decode only (no direct output), required for WASAPI feed
// BassFlags.Float = 32-bit float PCM, required for WASAPI
private int CreateDecodeStream(byte[] data)
{
    var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
    _pinnedBuffer = handle;  // must keep pinned while stream is alive
    int stream = Bass.CreateStream(handle.AddrOfPinnedObject(), 0, data.Length,
        BassFlags.Decode | BassFlags.Float);
    if (stream == 0)
        throw new InvalidOperationException($"CreateStream failed: {Bass.LastError}");
    return stream;
}

// Step 3: Read technical metadata from BASS stream
Bass.ChannelGetInfo(stream, out ChannelInfo info);
// info.Frequency = sample rate (Hz)
// info.Channels = channel count
double durationSec = Bass.ChannelBytes2Seconds(stream,
    Bass.ChannelGetLength(stream));

// Step 4: Read tags from TagLib# using IFileAbstraction
var abstraction = new ByteArrayFileAbstraction(fileName, data);
using (var tagFile = TagLib.File.Create(abstraction))
{
    string title = tagFile.Tag.Title;
    string[] performers = tagFile.Tag.Performers;
    string album = tagFile.Tag.Album;
}
```

### Pattern 3: WASAPI Procedure (PCM Data Feed)

**What:** The `WasapiProcedure` callback fires on WASAPI's buffer-request thread. It reads from the decode stream and writes float PCM to the output buffer.
**When to use:** Set as callback in `BassWasapi.Init`. This is the audio pipeline.

```csharp
// Source: ManagedBass.Wasapi BassWasapi.cs + un4seen BASSWASAPI docs

private int WasapiProc(IntPtr buffer, int length, IntPtr user)
{
    if (_currentStream == 0 || _playbackState != AudioPlayerState.Playing)
    {
        // Output silence (zero-fill)
        // Note: DO NOT use Marshal.Copy with zero array here in tight loop
        // Windows will zero-fill unwritten buffers automatically
        return length; // return 0 to stop WASAPI, return length for silence
    }

    // Read from decode stream into WASAPI buffer
    int bytesRead = Bass.ChannelGetData(_currentStream, buffer, length);
    if (bytesRead <= 0)
    {
        // End of stream reached
        OnPlaybackEnded();
        return 0;
    }
    return bytesRead;
}
```

### Pattern 4: TagLib# IFileAbstraction from Byte Array

**What:** TagLib# normally works with file paths. For IStream-sourced data, implement `IFileAbstraction` wrapping a `MemoryStream` over the byte array.
**When to use:** Every file load. The file extension on the "name" determines TagLib#'s format detection.

```csharp
// Source: TagLib# GitHub (mono/taglib-sharp) + community pattern

private class ByteArrayFileAbstraction : TagLib.File.IFileAbstraction
{
    private readonly byte[] _data;

    public ByteArrayFileAbstraction(string name, byte[] data)
    {
        Name = name;  // Extension used for format detection (e.g. "track.mp3")
        _data = data;
    }

    public string Name { get; }

    public Stream ReadStream => new MemoryStream(_data, writable: false);

    public Stream WriteStream => throw new NotSupportedException();

    public void CloseStream(Stream stream) => stream?.Dispose();
}
```

### Pattern 5: Position Polling with WinForms Timer

**What:** A `System.Windows.Forms.Timer` polls `Bass.ChannelGetPosition` and fires on the STA UI thread, updating the seek bar without needing `Control.Invoke`.
**When to use:** Start timer on Play, stop on Pause/Stop. Interval: 250ms.

```csharp
// Source: WinForms Timer documentation (fires on UI thread)

_positionTimer = new System.Windows.Forms.Timer { Interval = 250 };
_positionTimer.Tick += (s, e) =>
{
    if (_player.State == AudioPlayerState.Playing)
    {
        double pos = _player.CurrentPositionSeconds;
        double total = _player.TotalDurationSeconds;
        _controlBar.UpdatePosition(pos, total);
        _controlBar.Invalidate();
    }
};
```

### Pattern 6: Owner-Drawn Seek Bar

**What:** A seek bar drawn in GDI+ inside `OnPaint`. Hit-tested on `MouseDown`/`MouseMove`/`MouseUp` to calculate seek position from mouse X.
**When to use:** Rendered in `ControlBarRenderer.DrawSeekBar()`.

```csharp
// Source: WinForms custom control pattern (Microsoft Learn)

// Draw track
g.FillRectangle(trackBrush, trackRect);
// Draw filled portion
int fillWidth = (int)(trackRect.Width * (position / duration));
g.FillRectangle(fillBrush, trackRect.X, trackRect.Y, fillWidth, trackRect.Height);
// Draw time labels
g.DrawString(FormatTime(position), font, textBrush, leftX, labelY);
g.DrawString(FormatTime(duration), font, textBrush, rightX - rightWidth, labelY);

// Hit test in MouseDown:
if (trackRect.Contains(e.Location))
{
    float ratio = (float)(e.X - trackRect.X) / trackRect.Width;
    double seekTo = ratio * _totalDuration;
    _player.Seek(seekTo);
}
```

### Anti-Patterns to Avoid
- **Using Bass.Init with a real output device when WASAPI is also used:** WASAPI requires `Bass.Init(0)` (NoSound device). Using a real device causes BASS to try to output directly AND through WASAPI simultaneously.
- **Creating BASS streams without `BassFlags.Decode`:** Non-decode streams try to play through BASS's own mixer, which won't work when using WASAPI as the output device.
- **Not setting `BassFlags.Float` on decode streams:** WASAPI expects 32-bit float PCM. Mixing flags causes format mismatch.
- **Calling `Bass.ChannelFree()` inside the WasapiProcedure callback:** The callback runs on WASAPI's thread. Channel cleanup must happen on a different thread (post a flag, handle in UI timer or Thread.Pool).
- **Forgetting to unpin GCHandle after stream free:** If using `GCHandle.Alloc` to pin the byte array for `Bass.CreateStream(IntPtr ...)`, you MUST call `handle.Free()` AFTER `Bass.ChannelFree(stream)`. Free the stream first, then the GCHandle. Reverse order causes crash.
- **Using `System.Threading.Timer` for UI position updates:** It fires on ThreadPool (MTA). Use `System.Windows.Forms.Timer` instead — it fires on the STA UI thread without needing `Control.Invoke`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| MP3 decoding | Custom MP3 decoder | BASS native (ManagedBass) | Frame sync, VBR, ID3 headers, gapless — hundreds of edge cases |
| FLAC decoding | Custom FLAC decoder | BassFlac plugin | Reference FLAC decoder; bit-perfect decoding |
| WASAPI initialization | Raw Win32 P/Invoke to IAudioClient | ManagedBass.Wasapi | Device enumeration, format negotiation, exclusive/shared mode, buffer management |
| ID3/Vorbis tag parsing | Custom tag parser | TagLib# | ID3v1/v2/APE/Vorbis Comment/FLAC metadata all unified; handles malformed tags gracefully |
| Seek bar control | WinForms TrackBar subclass | GDI+ owner-draw in existing UserControl | WinForms TrackBar cannot be theme-aware in dark mode; owner-draw reuses existing double-buffer pattern |
| Volume level persistence | Custom serialization | Extend existing INI config via ConfigManager | ConfigManager + ini-parser-netstandard already in place |

**Key insight:** BASS handles the hardest parts of audio decoding (VBR seeking, gapless, sample-rate conversion for WASAPI) automatically. The application only needs to implement the WASAPI callback procedure that pulls data from BASS decode streams.

---

## Common Pitfalls

### Pitfall 1: BASS Init Device Mismatch with WASAPI
**What goes wrong:** Calling `Bass.Init(-1)` (default output device) when also using WASAPI causes BASS to hold the audio device, and WASAPI initialization may fail or produce double audio.
**Why it happens:** BASS has its own output subsystem distinct from WASAPI. Using both simultaneously on the same device causes conflict.
**How to avoid:** Always use `Bass.Init(0)` (NoSound device = device index 0) when WASAPI is the output path. Source: BASS documentation and community patterns.
**Warning signs:** WASAPI Init returns false with `BASS_ERROR_DEVICE` or `BASS_ERROR_ALREADY`.

### Pitfall 2: GCHandle Pinned Buffer Lifetime
**What goes wrong:** `Bass.CreateStream(IntPtr, ...)` takes a pointer to a pinned byte array. If the `GCHandle` is freed before the BASS stream is freed, the GC can move the array, causing BASS to read garbage/crash prevhost.exe.
**Why it happens:** .NET GC can relocate managed objects unless pinned.
**How to avoid:** Store the `GCHandle` alongside the stream handle in `AudioPlayer`. Free order: `Bass.ChannelFree(stream)` THEN `gcHandle.Free()`.
**Warning signs:** Intermittent crashes in prevhost.exe with no .NET exception; crashes only under GC pressure.

### Pitfall 3: FLAC Plugin Not Loaded Before Stream Creation
**What goes wrong:** Creating a BASS stream for a .flac file before `BassFlac.Load()` returns 0 (stream creation fails silently with `BASS_ERROR_FILEFORM`).
**Why it happens:** BASS tries to identify the format at stream-create time. If bassflac.dll is not loaded, FLAC files are unrecognized.
**How to avoid:** Load all plugins (`BassFlac.Load()`) BEFORE `Bass.Init()`, or at minimum before any `Bass.CreateStream()` call.
**Warning signs:** FLAC files show `BASS_ERROR_FILEFORM` in `Bass.LastError`; WAV/MP3 work fine.

### Pitfall 4: WasapiProcedure Callback Thread Safety
**What goes wrong:** Calling UI methods, `Bass.ChannelFree`, or other BASS functions from inside the WasapiProcedure callback causes deadlocks or crashes.
**Why it happens:** The WASAPI callback runs on a high-priority audio thread owned by Windows. BASS and WinForms are not safe to call from that context.
**How to avoid:** The WASAPI proc should ONLY call `Bass.ChannelGetData()`. Set a flag (`_endOfStream = true`) when data runs out, and handle cleanup on the UI timer tick.
**Warning signs:** Deadlock in prevhost.exe when file ends; Explorer freezes momentarily.

### Pitfall 5: TagLib# Name Parameter for Format Detection
**What goes wrong:** `TagLib.File.Create(abstraction)` fails or reads wrong tag format if the `Name` property returns a filename without extension (e.g., "Unknown" from `STATFLAG_NONAME`).
**Why it happens:** TagLib# uses the file extension to determine which tag reader to use.
**How to avoid:** The IStream `STATFLAG_NONAME` path loses the filename. Try `STATFLAG_DEFAULT` (flag=0) first to get the real filename, fall back to `STATFLAG_NONAME` only if it throws. Store the filename in `_fileName` as currently done in `AudioPreviewHandler`. Pass `_fileName` to `ByteArrayFileAbstraction`.
**Warning signs:** Tags always empty even for well-tagged files; `TagLib.File.Create` throws `UnsupportedFormatException`.

### Pitfall 6: BASS Initialized in COM MTA Context
**What goes wrong:** BASS and WASAPI init called from an MTA thread (the COM apartment where `DoPreview` fires) but stream operations later called from STA. BASS is not apartment-aware.
**Why it happens:** `AudioPreviewHandler` is used from both STA (constructor) and MTA (DoPreview callback) threads.
**How to avoid:** Initialize BASS in the constructor (which runs on STA). BASS itself does not care about COM apartments for audio callbacks, but the GCHandle pinning and stream operations should be on a consistent thread. Keep BASS init/free in the constructor/Unload flow which runs STA.
**Warning signs:** `BASS_ERROR_INIT` on second file when `Bass.Init` is called on a different thread than expected.

### Pitfall 7: WASAPI Volume vs. BASS Channel Volume
**What goes wrong:** Setting `Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, value)` changes the BASS mix volume but has no effect when WASAPI is the output (PCM data bypasses BASS mixer).
**Why it happens:** When using decode streams fed directly to WASAPI, the BASS mixer is not in the signal path.
**How to avoid:** Use `BassWasapi.SetVolume(WasapiVolumeTypes.Session, value)` to control volume at the WASAPI session level. This correctly affects the app's audio session independently of system volume.
**Warning signs:** Volume slider moves but audio level doesn't change.

### Pitfall 8: BASS DLL Not Found in prevhost.exe
**What goes wrong:** `Bass.Init()` throws `DllNotFoundException` or `BadImageFormatException` because bass.dll is not in the DLL search path.
**Why it happens:** prevhost.exe runs from `C:\Windows\System32`. It loads `Audex.dll` via COM, but the DLL search path does not include the handler's directory by default.
**How to avoid:** Call `SetDllDirectory` or use `LoadLibraryEx` with a full path before `Bass.Init()`. Alternatively, use `AddDllDirectory` via P/Invoke to add the handler assembly directory to the search path.
**Warning signs:** `BASS.Init()` or plugin load fails with `DllNotFoundException`; other COM-visible DLLs load fine.

---

## Code Examples

Verified patterns from official and authoritative sources:

### BASS + WASAPI Initialization (Full Sequence)
```csharp
// Source: BASS docs (un4seen.com) + ManagedBass GitHub + BASS.NET community patterns

public static bool Initialize()
{
    // Add the directory containing bass.dll etc. to DLL search path
    string assemblyDir = Path.GetDirectoryName(
        typeof(AudioPlayer).Assembly.Location) ?? "";
    NativeMethods.SetDllDirectory(assemblyDir);

    // Load FLAC plugin (must happen before Bass.Init or at least before CreateStream)
    BassFlac.Load();

    // Initialize BASS with NoSound device (required for WASAPI output path)
    if (!Bass.Init(0))
    {
        // Bass.LastError gives specific error code
        return false;
    }

    // Initialize WASAPI shared mode on default output device
    // Device=-1 (default), Freq=0 (use mixer rate), Chans=0 (use mixer channels)
    if (!BassWasapi.Init(-1, 0, 0, WasapiInitFlags.Shared,
        buffer: 0f, period: 0f, procedure: _wasapiProc, user: IntPtr.Zero))
    {
        Bass.Free();
        return false;
    }

    BassWasapi.Start();
    return true;
}

// P/Invoke for DLL search path
private static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetDllDirectory(string lpPathName);
}
```

### Loading a File and Getting Technical Metadata
```csharp
// Source: ManagedBass API docs (managedbass.github.io)

// Reset stream to start, seek
pstream.Seek(0, 0, 0);

// Copy IStream to byte array
byte[] data = CopyIStream(pstream);

// Pin and create decode stream
var gcHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
int stream = Bass.CreateStream(gcHandle.AddrOfPinnedObject(), 0, data.Length,
    BassFlags.Decode | BassFlags.Float);

if (stream == 0)
{
    gcHandle.Free();
    throw new InvalidOperationException($"BASS CreateStream failed: {Bass.LastError}");
}

// Get technical metadata
Bass.ChannelGetInfo(stream, out ChannelInfo info);
int sampleRate = info.Frequency;
int channels = info.Channels;

long lengthBytes = Bass.ChannelGetLength(stream);
double durationSec = Bass.ChannelBytes2Seconds(stream, lengthBytes);

// Bit depth from BASS flags - BASS_SAMPLE_FLOAT = always 32-bit float for decode
// Actual bit depth from stream must be read from TagLib# or existing header parsers
```

### Reading Tags via TagLib# IFileAbstraction
```csharp
// Source: TagLib# GitHub (mono/taglib-sharp) - IFileAbstraction pattern

public class ByteArrayFileAbstraction : TagLib.File.IFileAbstraction
{
    private readonly byte[] _data;

    public ByteArrayFileAbstraction(string name, byte[] data)
    {
        Name = name;
        _data = data;
    }

    public string Name { get; }
    public Stream ReadStream => new MemoryStream(_data, writable: false);
    public Stream WriteStream => throw new NotSupportedException("Read-only");
    public void CloseStream(Stream stream) => stream?.Dispose();
}

// Usage:
try
{
    using var tagFile = TagLib.File.Create(new ByteArrayFileAbstraction(fileName, data));
    string? title = string.IsNullOrEmpty(tagFile.Tag.Title) ? null : tagFile.Tag.Title;
    string? artist = tagFile.Tag.Performers.Length > 0
        ? string.Join(", ", tagFile.Tag.Performers) : null;
    string? album = string.IsNullOrEmpty(tagFile.Tag.Album) ? null : tagFile.Tag.Album;
    // Only expose non-null values to UI (user decision: hide missing fields)
}
catch (TagLib.UnsupportedFormatException)
{
    // TagLib# can't read this format — fall back to header parsers
}
```

### WASAPI Procedure (Audio Pipeline)
```csharp
// Source: BASSWASAPI documentation (un4seen.com) + ManagedBass.Wasapi

private int WasapiProcedure(IntPtr buffer, int length, IntPtr user)
{
    // ONLY call Bass.ChannelGetData here — nothing else
    // No UI calls, no stream free, no logging
    if (_decodeStream == 0 || _state != AudioPlayerState.Playing)
    {
        // Return length = output silence (buffer will be zeroed by Windows)
        return length;
    }

    int bytesRead = Bass.ChannelGetData(_decodeStream, buffer, length);
    if (bytesRead == -1 || bytesRead == 0)
    {
        // End of stream — signal to UI thread, don't cleanup here
        _endOfStreamFlag = 1;  // Interlocked.Exchange target
        return 0;              // 0 = tell WASAPI we're done feeding data
    }
    return bytesRead;
}
```

### Transport Control: Stop + Reset
```csharp
// Stop resets position to start (user decision)
public void Stop()
{
    _state = AudioPlayerState.Stopped;
    if (_decodeStream != 0)
    {
        Bass.ChannelSetPosition(_decodeStream, 0);  // seek to start
    }
    // Notify UI: update button to Play state, reset seek bar to 0
    OnStateChanged(AudioPlayerState.Stopped);
}
```

### Volume via WASAPI Session
```csharp
// Source: ManagedBass.Wasapi documentation
// Use WASAPI session volume — BASS channel volume won't work with decode streams

// Set volume (0.0f = silence, 1.0f = full volume)
BassWasapi.SetVolume(WasapiVolumeTypes.Session, volumeFloat);

// Mute = set to 0, restore previous level on unmute
// Store volume separately from mute state
```

### Owner-Drawn Bottom Bar Layout
```csharp
// Pattern from Phase 1 LayoutRenderer.cs (GDI+ owner-drawn, DPI-aware)

private void DrawControlBar(Graphics g, Rectangle bounds)
{
    float dpiScale = g.DpiX / 96.0f;
    int padding = (int)(6 * dpiScale);
    int seekHeight = (int)(4 * dpiScale);
    int buttonSize = (int)(20 * dpiScale);
    int volumeWidth = (int)(80 * dpiScale);

    // Separator line at top of control bar
    using var sepPen = new Pen(ThemeHelper.GetBorderColor(), 1f);
    g.DrawLine(sepPen, bounds.Left, bounds.Top, bounds.Right, bounds.Top);

    // Row 1: seek bar with time labels
    // "1:23 ━━━━━━━━ 4:56" pattern
    int seekY = bounds.Top + padding;
    DrawSeekBar(g, bounds, seekY, seekHeight, dpiScale);

    // Row 2: centered transport buttons + right-aligned volume
    int buttonY = seekY + seekHeight + padding * 2;
    DrawTransportButtons(g, bounds, buttonY, buttonSize, dpiScale);
    DrawVolumeControl(g, bounds, buttonY, buttonSize, volumeWidth, dpiScale);
}
```

---

## Claude's Discretion — Resolved Recommendations

### IStream Handling: Use MemoryStream/Byte Array Copy
**Recommendation:** Copy IStream bytes to `byte[]` on each `DoPreviewInternal()` call.

**Rationale:**
- Temp file approach requires write access to %TEMP% — this works in prevhost.exe (low-integrity), but adds file cleanup complexity and risk of file locks if prevhost.exe crashes
- BASS callback approach (`FileProcedures`) requires implementing Close/Read/Seek/Length callbacks wrapping the COM `IStream` object; COM IStream is MTA-threaded but the WASAPI decode callback fires on another thread, creating cross-apartment marshalling risk
- Byte array copy is the simplest, safest approach: copy once, use for both BASS and TagLib#, no cleanup needed (GC handles it)
- For large files: the preview pane is typically used for files under 500MB; a 100MB WAV fits in managed heap fine for preview purposes
- **Verdict: Copy to byte array (MemoryStream). Pin it for BASS via GCHandle.**

### WASAPI Plugin: Required — Must Bundle basswasapi.dll
**Recommendation:** Bundle `basswasapi.dll` alongside `bass.dll`. WASAPI is NOT built into base BASS.

### FLAC Plugin: Required — Must Bundle bassflac.dll
**Recommendation:** Bundle `bassflac.dll` alongside `bass.dll`. FLAC is NOT built into base BASS.

### Icon Glyphs
**Recommendation:** Use these Unicode characters from Segoe UI Symbol / Segoe Fluent Icons:
- Play: `\u25B6` (BLACK RIGHT-POINTING TRIANGLE ▶)
- Pause: `\u23F8` (DOUBLE VERTICAL BAR ⏸)
- Stop: `\u23F9` (BLACK SQUARE FOR STOP ⏹)
- Speaker (unmuted): `\u1F50A` (SPEAKER WITH THREE SOUND WAVES 🔊) — or simpler: `\u1F509` (🔉)
- Speaker (muted): `\u1F507` (SPEAKER WITH CANCELLATION STROKE 🔇)

Fallback without emoji rendering issues: Use simple ASCII-art substitutes drawn in GDI+ as geometric shapes (triangle for play, two rectangles for pause, square for stop) if Unicode rendering is unreliable in prevhost.exe.

### Bottom Bar Spacing
**Recommendation:**
- Control bar total height: ~52px at 96 DPI (28px seek row + 4px separator + 24px button row + 8px top padding + 8px bottom padding)
- Seek bar track height: 4px; thumb: 10px diameter circle centered on track
- Time label font: Segoe UI 8pt, secondary text color
- Button size: 20x20px with 4px gap between buttons
- Volume slider width: 80px; preceded by 20x20 speaker icon

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| BASS.NET (radio42 wrapper) | ManagedBass (ManagedBass org) | ManagedBass is the actively maintained replacement; BASS.NET docs still appear in searches but the library is stagnant |
| WinMM waveOut | WASAPI shared mode | WASAPI allows per-app volume control, no device blocking, lower latency on Win10+ |
| TagLib# from file path | TagLib# via IFileAbstraction | Enables in-memory tag reading from IStream without temp files |
| Polling `Bass.ChannelActive` | WinForms Timer + `ChannelGetPosition` | Timer fires on UI thread; avoids cross-thread marshalling |

**Deprecated/outdated:**
- **BASS.NET (radio42.com):** Stagnant since ~2020, different API shape from ManagedBass. ManagedBass is the correct package. Search results still surface BASS.NET docs — don't confuse `BassWasapi.BASS_WASAPI_Init` (BASS.NET) with `BassWasapi.Init` (ManagedBass).
- **`BassFlags.Default` for decode streams:** Must use `BassFlags.Decode | BassFlags.Float` for WASAPI.

---

## Open Questions

1. **SetDllDirectory timing in COM host**
   - What we know: `SetDllDirectory` changes the DLL search path for the process; prevhost.exe's working directory is System32, not the handler directory
   - What's unclear: Whether prevhost.exe's security policy (low-integrity) allows `SetDllDirectory` from inside a COM extension; whether there's a safer alternative (e.g., `AddDllDirectory` with `LOAD_LIBRARY_SEARCH_USER_DIRS`)
   - Recommendation: Implement with `SetDllDirectory` first; if it fails in testing, fall back to `LoadLibraryExW` with full path to bass.dll before `Bass.Init()`

2. **WASAPI device format vs. BASS stream format mismatch**
   - What we know: WASAPI shared mode requires the mixer's native rate/channels (often 48kHz/2ch); BASS decode streams can be any rate (44.1kHz, 48kHz, etc.)
   - What's unclear: Whether ManagedBass.Wasapi's shared mode handles sample rate conversion automatically, or whether `Bass.ChannelSetAttribute(ChannelAttribute.SampleRate, ...)` is needed
   - Recommendation: Use `Freq=0, Chans=0` in `BassWasapi.Init` which tells WASAPI to use its mixer format; BASS will resample the decode stream to match

3. **Bit depth reporting via BASS**
   - What we know: BASS decode streams are always 32-bit float when `BassFlags.Float` is set; `ChannelGetInfo.Resolution` is not reliable for all formats
   - What's unclear: Whether BASS reliably reports the original source bit depth (e.g., 24-bit FLAC vs 16-bit FLAC) via ChannelGetInfo
   - Recommendation: Use the existing Phase 1 header parsers (`AudioHeaderParserFactory`) for bit depth display only; use BASS for everything else. This preserves the "fallback" role of header parsers.

---

## Sources

### Primary (HIGH confidence)
- [ManagedBass NuGet Gallery](https://www.nuget.org/packages/ManagedBass/) — version 4.0.2, targets net48/netstandard2.0/.net8
- [ManagedBass.Wasapi NuGet Gallery](https://www.nuget.org/packages/ManagedBass.Wasapi/) — version 4.0.1 (note: NuGet shows 4.0.2 but latest confirmed stable is 4.0.1)
- [ManagedBass.Flac NuGet Gallery](https://www.nuget.org/packages/ManagedBass.Flac/) — version 4.0.2
- [ManagedBass GitHub](https://github.com/ManagedBass/ManagedBass) — source for BassWasapi.Init signature, FileProcedures structure
- [un4seen.com BASS](https://www.un4seen.com/bass.html) — BASS 2.4.18.3 format support (WAV/MP3 native, FLAC via plugin)
- [TagLibSharp NuGet Gallery](https://www.nuget.org/packages/TagLibSharp) — version 2.3.0, released 2022-07-30, targets net462/netstandard2.0
- [TagLib# GitHub](https://github.com/mono/taglib-sharp) — IFileAbstraction interface pattern

### Secondary (MEDIUM confidence)
- [BASSWASAPI documentation](https://www.un4seen.com/doc/basswasapi/basswasapi.html) — shared mode initialization requirements
- [ManagedBass Wasapi API](https://managedbass.github.io/api/ManagedBass.Wasapi.BassWasapi.html) — `BassWasapi.Init` signature confirmed: WasapiInitFlags.Shared, device=-1 for default output
- [ManagedBass FileProcedures API](https://managedbass.github.io/api/ManagedBass.FileProcedures.html) — callback structure (Close/Read/Seek/Length)
- Multiple sources confirming BASS NoSound device required with WASAPI output

### Tertiary (LOW confidence)
- BASS.NET WASAPI examples (radio42.com) — API shape is different from ManagedBass; used only to understand WASAPI concepts, not API calls
- Community patterns for WasapiProcedure silence return value (`return length` vs `return 0`) — needs validation in testing

---

## Metadata

**Confidence breakdown:**
- Standard stack (libraries, versions): HIGH — verified against NuGet and GitHub
- WASAPI requires separate plugin: HIGH — verified against un4seen.com and ManagedBass source
- FLAC requires separate plugin: HIGH — verified against un4seen.com BASSFLAC page
- BASS NoSound device required for WASAPI: HIGH — multiple sources including BASS.NET examples
- IStream → byte array recommendation: MEDIUM — reasoned from constraints; no direct official guidance
- TagLib# IFileAbstraction pattern: MEDIUM — community pattern, not official docs example
- SetDllDirectory in prevhost.exe: LOW — may have security restrictions in low-integrity COM host
- WASAPI sample rate handling (auto-resampling): LOW — inferred from Freq=0 behavior, needs testing

**Research date:** 2026-02-16
**Valid until:** 2026-03-16 (BASS and ManagedBass are stable libraries; 30 days)
