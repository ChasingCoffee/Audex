# Architecture Research

**Domain:** Windows Shell Extension - Audio Preview Handler
**Researched:** 2026-02-16
**Confidence:** MEDIUM

## Standard Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    Windows Explorer Process                     │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │              Preview Pane (Host Window)                  │   │
│  │  ┌────────────────────────────────────────────────────┐  │   │
│  │  │         Prevhost.exe (Surrogate Process)           │  │   │
│  │  │  ┌──────────────────────────────────────────────┐  │  │   │
│  │  │  │       Audex Handler DLL            │  │  │   │
│  │  │  │  ┌────────────────────────────────────────┐  │  │  │   │
│  │  │  │  │  COM Interfaces (IPreviewHandler)      │  │  │  │   │
│  │  │  │  ├────────────────────────────────────────┤  │  │  │   │
│  │  │  │  │  HwndSource (WPF → Win32 Bridge)       │  │  │  │   │
│  │  │  │  ├────────────────────────────────────────┤  │  │  │   │
│  │  │  │  │  WPF UserControl (Main Preview UI)     │  │  │  │   │
│  │  │  │  │  ┌──────────────┬──────────────────┐   │  │  │  │   │
│  │  │  │  │  │  Waveform    │  Metadata Panel  │   │  │  │  │   │
│  │  │  │  │  │  Canvas      │  (BPM, Key, etc) │   │  │  │  │   │
│  │  │  │  │  └──────────────┴──────────────────┘   │  │  │  │   │
│  │  │  │  │  ┌──────────────────────────────────┐  │  │  │  │   │
│  │  │  │  │  │  Playback Controls (Play/Pause)  │  │  │  │  │   │
│  │  │  │  │  └──────────────────────────────────┘  │  │  │  │   │
│  │  │  │  └────────────────────────────────────────┘  │  │  │   │
│  │  │  │  ┌────────────────────────────────────────┐  │  │  │   │
│  │  │  │  │  Audio Engine (BASS.NET)               │  │  │  │   │
│  │  │  │  │  - WASAPI Output (default)             │  │  │  │   │
│  │  │  │  │  - ASIO Output (optional)              │  │  │  │   │
│  │  │  │  └────────────────────────────────────────┘  │  │  │   │
│  │  │  └──────────────────────────────────────────────┘  │  │   │
│  │  └────────────────────────────────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| **PreviewHandlerShim** | COM interface implementation, lifecycle management | C# class implementing IPreviewHandler, IInitializeWithStream, IObjectWithSite, IOleWindow |
| **HwndSource** | Bridges WPF content to Win32 HWND | WPF Interop class that creates child HWND for WPF content |
| **PreviewControl** | Main UI container for preview | WPF UserControl hosting waveform, metadata, controls |
| **WaveformRenderer** | Renders frequency-colored waveform | Custom WPF Canvas with Shape drawing or WriteableBitmap |
| **MetadataPanel** | Displays audio file metadata | WPF Grid/StackPanel with TextBlocks for BPM, key, bitrate, etc. |
| **AudioEngine** | Audio playback and analysis | BASS.NET wrapper managing streams, decoding, output |
| **ConfigurationManager** | Loads/saves JSON settings | C# class reading from %AppData%\Audex\config.json |

## Recommended Project Structure

```
Audex/
├── src/
│   ├── Core/                      # COM interface implementations
│   │   ├── PreviewHandler.cs      # IPreviewHandler implementation
│   │   ├── PreviewHandlerShim.cs  # COM interop shim
│   │   └── Interfaces/            # COM interface definitions
│   │       ├── IPreviewHandler.cs
│   │       ├── IInitializeWithStream.cs
│   │       └── IObjectWithSite.cs
│   ├── UI/                        # WPF user interface
│   │   ├── PreviewControl.xaml    # Main preview UserControl
│   │   ├── WaveformCanvas.cs      # Custom waveform renderer
│   │   ├── MetadataPanel.xaml     # Metadata display panel
│   │   └── PlaybackControls.xaml  # Play/pause UI
│   ├── Audio/                     # Audio processing
│   │   ├── AudioEngine.cs         # BASS.NET wrapper
│   │   ├── WaveformAnalyzer.cs    # FFT/waveform generation
│   │   └── OutputManager.cs       # WASAPI/ASIO selection
│   ├── Config/                    # Configuration
│   │   ├── ConfigManager.cs       # JSON config loading
│   │   └── PreviewSettings.cs     # Settings model
│   └── Registration/              # COM registration
│       ├── RegistryHelper.cs      # Registry key creation
│       └── Install.ps1            # Installation script
├── native/                        # BASS.NET native DLLs
│   ├── bass.dll
│   ├── bassasio.dll
│   └── basswasapi.dll
└── config/
    └── default-config.json        # Default configuration template
```

### Structure Rationale

- **Core/:** Separates COM plumbing from business logic. IPreviewHandler requires specific method signatures; isolating this allows UI and audio components to remain COM-agnostic.
- **UI/:** WPF components are self-contained. PreviewControl is the root visual; it composes waveform, metadata, and controls. Can be developed/tested standalone before COM integration.
- **Audio/:** Encapsulates BASS.NET dependency. AudioEngine handles initialization, cleanup, and device selection. WaveformAnalyzer generates visual data from audio stream.
- **Registration/:** Keeps installer/uninstaller logic separate. Registry manipulation is error-prone; isolating it simplifies debugging and enables unattended installation.

## Architectural Patterns

### Pattern 1: COM Interop Shim

**What:** C# class implementing IPreviewHandler serves as thin adapter between COM and managed WPF code.

**When to use:** Always for shell extension handlers. Explorer expects COM interfaces; WPF expects managed object model.

**Trade-offs:**
- **Pros:** Separates concerns; WPF UI can be developed without COM knowledge
- **Cons:** Extra layer adds complexity; debugging crosses managed/unmanaged boundary

**Example:**
```csharp
[ComVisible(true)]
[Guid("YOUR-GUID-HERE")]
[ClassInterface(ClassInterfaceType.None)]
public class PreviewHandler : IPreviewHandler, IInitializeWithStream,
                               IObjectWithSite, IOleWindow
{
    private HwndSource _hwndSource;
    private PreviewControl _previewControl;

    // IPreviewHandler.SetWindow - Receives parent HWND from Explorer
    public void SetWindow(IntPtr hwnd, ref RECT rect)
    {
        if (_hwndSource == null)
        {
            // Create HwndSource to host WPF content
            var parameters = new HwndSourceParameters("Preview")
            {
                ParentWindow = hwnd,
                PositionX = rect.left,
                PositionY = rect.top,
                Width = rect.right - rect.left,
                Height = rect.bottom - rect.top,
                WindowStyle = WS_CHILD | WS_VISIBLE
            };

            _hwndSource = new HwndSource(parameters);
            _previewControl = new PreviewControl();
            _hwndSource.RootVisual = _previewControl;
        }
    }

    // IPreviewHandler.DoPreview - Actually load and render preview
    public void DoPreview()
    {
        if (_stream != null && _previewControl != null)
        {
            _previewControl.LoadAudioFromStream(_stream);
        }
    }

    // IPreviewHandler.Unload - Clean up resources
    public void Unload()
    {
        _previewControl?.Dispose();
        _hwndSource?.Dispose();
        _stream?.Dispose();
    }
}
```

### Pattern 2: Lazy Preview Rendering

**What:** Delay expensive operations (waveform generation, FFT analysis) until DoPreview() is called, not during initialization.

**When to use:** Always for preview handlers. Explorer calls Initialize and SetWindow for multiple files as user navigates; only selected file gets DoPreview().

**Trade-offs:**
- **Pros:** Avoids wasted work; improves Explorer responsiveness
- **Cons:** Slight delay when preview appears (mitigate with progress indicator)

**Example:**
```csharp
// IInitializeWithStream - Store stream, but don't process yet
public void Initialize(IStream stream, uint grfMode)
{
    _stream = stream; // Keep reference only
    // DO NOT read audio, generate waveform, or initialize BASS here
}

// IPreviewHandler.DoPreview - Now do the heavy lifting
public void DoPreview()
{
    // Initialize BASS on first use
    if (!_bassInitialized)
    {
        Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
        _bassInitialized = true;
    }

    // Load stream and generate waveform
    int channel = Bass.BASS_StreamCreateFile(_stream, 0, 0, BASSFlag.BASS_STREAM_DECODE);
    var waveformData = WaveformAnalyzer.Generate(channel);
    _previewControl.RenderWaveform(waveformData);
}
```

### Pattern 3: Separate AppID for Managed Handlers

**What:** Create custom AppID registry entry pointing to Prevhost.exe as DllSurrogate, ensuring unique CLR process.

**When to use:** Always for managed (.NET) preview handlers. Default shared Prevhost.exe instance may have wrong CLR version or conflicting dependencies.

**Trade-offs:**
- **Pros:** Guarantees correct .NET runtime version; isolates crashes
- **Cons:** Extra process overhead (one Prevhost.exe per handler instance)

**Example:**
```
HKEY_CLASSES_ROOT
   AppID
      {YOUR-HANDLER-APPID}
         DllSurrogate = Prevhost.exe    # Use surrogate process
   CLSID
      {YOUR-HANDLER-CLSID}
         AppID = {YOUR-HANDLER-APPID}    # Link to custom AppID
         InprocServer32
            (Default) = C:\Path\To\Audex.dll
            ThreadingModel = Apartment
```

### Pattern 4: Stream-Based Initialization

**What:** Initialize handler using IInitializeWithStream rather than IInitializeWithFile or IInitializeWithItem.

**When to use:** Always, unless file path is absolutely required. Microsoft strongly recommends stream initialization for security and stability.

**Trade-offs:**
- **Pros:** Runs in low-integrity process (sandbox); no direct file system access; protects against buffer overruns
- **Cons:** Can't use file path directly (but BASS.NET supports stream input)

**Example:**
```csharp
public void Initialize(IStream stream, uint grfMode)
{
    // Convert COM IStream to .NET Stream
    _managedStream = new ComStreamWrapper(stream);

    // BASS.NET can load from Stream
    // Store for later use in DoPreview()
}

// Later in DoPreview()
int channel = Bass.BASS_StreamCreateFile(_managedStream, 0, 0, BASSFlag.BASS_STREAM_DECODE);
```

## Data Flow

### Initialization Flow

```
Explorer detects file selection
    ↓
Explorer loads Audex.dll via Prevhost.exe surrogate
    ↓
CoCreateInstance({YOUR-HANDLER-CLSID}) → PreviewHandler object
    ↓
IObjectWithSite.SetSite(IPreviewHandlerFrame) → Store host reference
    ↓
IInitializeWithStream.Initialize(IStream) → Store stream (NO PROCESSING)
    ↓
IPreviewHandler.SetWindow(HWND, RECT) → Create HwndSource, instantiate WPF UserControl
    ↓
Handler waits in initialized state...
```

### Preview Rendering Flow

```
User selects file in Explorer (or preview pane auto-updates)
    ↓
IPreviewHandler.DoPreview() called
    ↓
AudioEngine.Initialize() → Bass.BASS_Init()
    ↓
AudioEngine.LoadStream() → Bass.BASS_StreamCreateFile() from IStream
    ↓
WaveformAnalyzer.Generate() → Read PCM data, perform FFT, create frequency bands
    ↓
WaveformCanvas.Render() → Draw frequency-colored waveform shapes
    ↓
MetadataPanel.Update() → Bass.BASS_ChannelGetTags() for ID3/etc, display BPM/key
    ↓
Preview visible in pane
```

### Resize Flow

```
User resizes preview pane
    ↓
IPreviewHandler.SetRect(newRect) called
    ↓
Update HwndSource dimensions
    ↓
WPF layout system reflows PreviewControl
    ↓
WaveformCanvas redraws at new size (may regenerate waveform bins for new width)
```

### Cleanup Flow

```
User navigates away or closes preview pane
    ↓
IPreviewHandler.Unload() called
    ↓
AudioEngine.Stop() → Bass.BASS_ChannelStop(), Bass.BASS_StreamFree()
    ↓
PreviewControl.Dispose() → Release WPF resources
    ↓
HwndSource.Dispose() → Destroy child HWND
    ↓
Stream.Dispose() → Release file handle
    ↓
Handler can be reused or released by COM
```

## Threading Model

### COM Apartment Threading

**Threading Model:** Single-Threaded Apartment (STA)

**Why STA:**
- IPreviewHandler must be STA (Windows Explorer runs preview pane on STA thread)
- WPF requires STA thread for UI operations
- Registry: `ThreadingModel = Apartment`

**Implications:**
- All IPreviewHandler methods called on same thread
- BASS.NET audio callbacks may come from different thread (use Dispatcher.Invoke for UI updates)
- Cannot block STA thread during DoPreview() (show loading indicator, use async if needed)

### BASS Audio Thread

**BASS threading:** BASS uses internal worker threads for audio decoding/output.

**Pattern:**
```csharp
// BASS callback executes on BASS thread, NOT WPF thread
private void OnPlaybackPosition(int handle, int channel, int data, IntPtr user)
{
    // Must marshal back to UI thread for WPF updates
    _previewControl.Dispatcher.BeginInvoke(() =>
    {
        _previewControl.UpdatePlaybackIndicator(position);
    });
}
```

## Scaling Considerations

| Scale | Architecture Adjustments |
|-------|--------------------------|
| Single file preview | Current architecture sufficient; Prevhost.exe process per handler instance |
| Batch file viewing | Preview handler reused; ensure proper Unload() cleanup between files |
| Large audio files (>100MB) | Stream waveform generation (don't load entire file); use BASS decode channels |
| High-DPI displays | WPF handles DPI automatically; ensure waveform rendering uses vector shapes or scales bitmap |

### Scaling Priorities

1. **First bottleneck:** Waveform generation for long files (>10 min). **Fix:** Downsample audio, generate lower-resolution waveform (1 pixel = N samples).
2. **Second bottleneck:** Memory usage from multiple BASS streams. **Fix:** Free channel immediately after waveform generation; only keep playback channel alive.

## Anti-Patterns

### Anti-Pattern 1: Heavy Work in Initialize or SetWindow

**What people do:** Load audio file, generate waveform, decode metadata in IInitializeWithStream.Initialize() or IPreviewHandler.SetWindow().

**Why it's wrong:** Explorer calls Initialize and SetWindow for multiple files as user navigates file list. Only the selected file receives DoPreview(). Doing heavy work early wastes CPU and delays Explorer UI.

**Do this instead:** Store stream/HWND references only. Perform ALL audio processing in DoPreview().

### Anti-Pattern 2: Direct Registry Manipulation in User Code

**What people do:** Use regasm.exe or manual Registry.SetValue() calls without understanding shell extension requirements.

**Why it's wrong:** Preview handlers need specific registry structure (file type associations under `HKEY_CLASSES_ROOT\.ext\shellex\{PREVIEW-HANDLER-GUID}`). Regasm creates COM registration but not shell extension hookup.

**Do this instead:** Use dedicated installer (PowerShell script or WiX) that creates both COM registration and file type associations. Example:
```powershell
# Register COM component
regasm Audex.dll /codebase

# Associate with audio file types
$handlerGuid = "{YOUR-HANDLER-CLSID}"
@(".mp3", ".flac", ".wav", ".m4a") | ForEach-Object {
    New-Item -Path "HKCR:\$_\shellex\{8895b1c6-b41f-4c1c-a562-0d564250836f}" -Force
    Set-ItemProperty -Path "HKCR:\$_\shellex\{8895b1c6-b41f-4c1c-a562-0d564250836f}" `
                     -Name "(Default)" -Value $handlerGuid
}

# Notify shell of changes
SHChangeNotify(SHCNE_ASSOCCHANGED)
```

### Anti-Pattern 3: Blocking UI Thread During Waveform Generation

**What people do:** Synchronously generate waveform in DoPreview() on STA thread, freezing Explorer preview pane.

**Why it's wrong:** Large files can take seconds to decode and analyze. User sees frozen preview pane, poor experience.

**Do this instead:** Show loading indicator immediately, generate waveform on background task:
```csharp
public void DoPreview()
{
    _previewControl.ShowLoadingIndicator();

    Task.Run(async () =>
    {
        var waveformData = await WaveformAnalyzer.GenerateAsync(_stream);

        // Marshal back to UI thread
        await _previewControl.Dispatcher.InvokeAsync(() =>
        {
            _previewControl.HideLoadingIndicator();
            _previewControl.RenderWaveform(waveformData);
        });
    });
}
```

### Anti-Pattern 4: Forgetting to Initialize BASS for 64-bit

**What people do:** Call `Bass.BASS_Init(-1, ...)` without checking platform architecture, causing crash on 64-bit Windows.

**Why it's wrong:** BASS requires matching architecture (32-bit BASS for 32-bit process, 64-bit for 64-bit). Preview handlers on 64-bit Windows run in 64-bit Prevhost.exe.

**Do this instead:** Build for x64 explicitly (or use AnyCPU with Prefer32bit=false). Deploy correct BASS DLLs:
```csharp
// In PreviewHandler constructor or static initializer
if (Environment.Is64BitProcess)
{
    Bass.LoadLibrary(@"native\x64\bass.dll");
}
else
{
    Bass.LoadLibrary(@"native\x86\bass.dll");
}
```

## Integration Points

### External Dependencies

| Dependency | Integration Pattern | Notes |
|---------|---------------------|-------|
| BASS.NET | P/Invoke via NuGet package | Must deploy native bass.dll, basswasapi.dll, bassasio.dll alongside handler DLL |
| Windows Shell | COM Activation | Explorer loads handler via CoCreateInstance; handler MUST be registered in HKCR\CLSID |
| .NET Runtime | CLR hosting in Prevhost.exe | Custom AppID ensures correct CLR version loaded |
| Configuration File | JSON deserialization | Store in %AppData%\Audex\config.json; create if missing |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| PreviewHandler ↔ PreviewControl | Direct method calls | Handler owns UserControl lifetime; calls Load/Unload methods |
| PreviewControl ↔ AudioEngine | Event-based | AudioEngine raises PositionChanged, PlaybackEnded events; Control subscribes |
| AudioEngine ↔ BASS.NET | P/Invoke | AudioEngine wraps BASS API; handles error codes, device selection |
| WaveformCanvas ↔ WaveformAnalyzer | Data transfer | Analyzer returns float[] arrays (one per frequency band); Canvas renders |
| ConfigManager ↔ File System | File I/O | Read JSON on handler load; write on settings change (if handler provides UI) |

## COM Registration Structure

### Required Registry Keys

```
HKEY_CLASSES_ROOT
   AppID
      {YOUR-HANDLER-APPID}              # Custom AppID for managed handler
         DllSurrogate = Prevhost.exe    # Use surrogate process
   CLSID
      {YOUR-HANDLER-CLSID}              # Handler class ID
         (Default) = "Audex Handler"
         AppID = {YOUR-HANDLER-APPID}   # Link to custom AppID
         InprocServer32
            (Default) = C:\Path\To\Audex.dll
            ThreadingModel = Apartment  # STA required for WPF
         Implemented Categories
            {62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}  # Preview handler category
   .mp3                                 # Register for each file type
      shellex
         {8895b1c6-b41f-4c1c-a562-0d564250836f}  # Preview handler GUID
            (Default) = {YOUR-HANDLER-CLSID}
   .flac
      shellex
         {8895b1c6-b41f-4c1c-a562-0d564250836f}
            (Default) = {YOUR-HANDLER-CLSID}
   # Repeat for .wav, .m4a, .ogg, etc.
```

### Key Takeaways

- **{8895b1c6-b41f-4c1c-a562-0d564250836f}:** Windows-defined GUID for preview handlers (same for all preview handlers)
- **Custom AppID:** Mandatory for managed handlers to avoid CLR version conflicts
- **File type association:** Must be created for EACH file extension handler supports
- **Category registration:** Optional but recommended for discoverability

## Build Order Implications

### Suggested Phase Order

1. **Phase 1: COM Shim + Basic WPF UI**
   - Implement IPreviewHandler skeleton (no audio logic)
   - Create basic WPF UserControl with placeholder text
   - Verify HwndSource hosting works (control appears in Explorer)
   - **Validate:** Can register handler, see "Preview not available" or placeholder in preview pane

2. **Phase 2: BASS Integration + Basic Playback**
   - Initialize BASS in DoPreview()
   - Load audio file from IStream, get basic metadata
   - Display filename and duration (no waveform yet)
   - **Validate:** Preview pane shows real audio file info

3. **Phase 3: Waveform Generation**
   - Implement WaveformAnalyzer (decode PCM, generate samples)
   - Render simple monochrome waveform on Canvas
   - **Validate:** Preview pane shows waveform shape

4. **Phase 4: Frequency Coloring**
   - Add FFT analysis to WaveformAnalyzer
   - Map frequency bands to colors
   - **Validate:** Waveform has color gradient

5. **Phase 5: Playback Controls + Advanced Features**
   - Add Play/Pause buttons
   - Implement WASAPI output
   - Add ASIO support (optional)
   - Show BPM/key metadata
   - **Validate:** Full audio preview with playback

6. **Phase 6: Configuration + Polish**
   - Load JSON config from AppData
   - Add settings for output device, colors
   - Installer/uninstaller scripts
   - **Validate:** Production-ready handler

### Dependency Graph

```
COM Shim + WPF Hosting
    ↓
BASS Initialization ─────→ Stream Loading
    ↓                          ↓
Waveform Generation  ←────── PCM Decoding
    ↓
Frequency Analysis (FFT)
    ↓
Colored Rendering
    ↓
Playback Controls ─→ Output Selection (WASAPI/ASIO)
```

## Sources

- **Microsoft Learn - Preview Handlers:** https://learn.microsoft.com/en-us/windows/win32/shell/preview-handlers (HIGH confidence - official documentation)
- **Microsoft Learn - IPreviewHandler Interface:** https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ipreviewhandler (HIGH confidence - official API reference)
- **Microsoft Learn - Registering Shell Extensions:** https://learn.microsoft.com/en-us/windows/win32/shell/reg-shell-exts (HIGH confidence - official documentation)
- **Microsoft Learn - WPF and Win32 Interoperation:** https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-win32-interoperation (HIGH confidence - official documentation on HwndSource)

---
*Architecture research for: Windows Audio Preview Handler*
*Researched: 2026-02-16*
