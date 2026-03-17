# Phase 1: COM Shell Extension Foundation - Research

**Researched:** 2026-02-16
**Domain:** Windows COM Shell Extensions / IPreviewHandler
**Confidence:** HIGH

## Summary

Windows Preview Handlers are COM-based shell extensions that enable file content previewing in Windows Explorer's preview pane. They implement the `IPreviewHandler` interface along with initialization interfaces (`IInitializeWithStream` strongly preferred over `IInitializeWithFile` to avoid file locking), `IObjectWithSite`, and `IOleWindow`. Preview handlers run out-of-process in `prevhost.exe` by default, providing process isolation that protects Explorer from crashes. The standard stack is C++ with ATL (Active Template Library) for COM infrastructure, using Apartment threading model. Critical design principles include lazy loading (defer all data loading until `DoPreview()`, not initialization), proper resource cleanup, and low-integrity process compatibility for security.

**Primary recommendation:** Use C++ with ATL for COM infrastructure, implement `IInitializeWithStream` (not `IInitializeWithFile`) to avoid file locking, use spdlog for logging with rolling file appender, SimpleIni for configuration, and header-only libraries for audio metadata parsing. Follow Microsoft's guidance: always load data in `DoPreview()`, never in initialization methods.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Placeholder UI:**
- File info panel showing filename, file size, format, and parsed header info (duration, sample rate, bit depth, channel count)
- Explorer-native visual style — match Windows Explorer's current theme (respects system light/dark mode)
- Layout skeleton showing grayed-out regions where waveform and playback controls will appear in future phases
- "Playback coming soon" note for registered formats that don't have audio support yet

**Error Presentation:**
- Inline text messages in the preview pane (no icons)
- Error appears as a banner overlay at top of preview, previous content stays dimmed underneath
- User-friendly language with log file path: "This file can't be previewed. See log for details: [path]"
- No retry link in the error banner — user re-selects the file in Explorer to retry
- Single rolling log file in %LOCALAPPDATA%/Audex/logs/
- Errors + warnings logged normally, with debug toggle via config file in AppData
- Config file (JSON/INI) in %LOCALAPPDATA%/Audex/ controls log level

**File Type Registration:**
- Register ALL planned audio format extensions upfront (WAV, MP3, FLAC, AIFF, OGG, AAC, WMA, OPUS, M4A)
- Defer tracker/module formats (.mod, .xm, .it, .s3m) to Phase 5 when BASS plugins are integrated
- Unrecognized/unsupported formats show file info panel + "Playback support coming in a future update" note
- Audex replaces any existing default preview handler for registered audio formats
- Both installer-based (Phase 7) and manual regsvr32/script registration for dev/testing
- Extension list is config-driven (read from config file), not hardcoded — new formats can be added without recompiling
- Config file for extensions lives in %LOCALAPPDATA%/Audex/

**Loading & Transitions:**
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

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope

</user_constraints>

<phase_requirements>
## Phase Requirements

This phase addresses requirements PREV-01 through PREV-05. Research findings map to these requirements:

| ID | Description | Research Support |
|----|-------------|-----------------|
| PREV-01 | Preview pane appears in Windows Explorer when audio file selected | IPreviewHandler registration via file extension ProgID associations; COM CLSID registration in HKCR\CLSID and PreviewHandlers list; SetWindow/DoPreview implementation |
| PREV-02 | Preview updates when user selects a different audio file | IPreviewHandler lifecycle: Unload() releases resources, then re-Initialize() + DoPreview() for new file; debounce with SetTimer to skip intermediate selections during rapid browsing |
| PREV-03 | Audio file is released (not locked) when preview closes or file changes | IInitializeWithStream (not IInitializeWithFile) loads file via IStream copy, avoiding file lock; Unload() releases IStream immediately; prevhost.exe process isolation |
| PREV-04 | Errors are logged to AppData for debugging | spdlog with rolling_file_sink to %LOCALAPPDATA%/Audex/logs/; config file in AppData controls log level (error/warning vs debug); low-integrity process can write to LOCALAPPDATA |
| PREV-05 | Handler runs correctly in low-integrity process | Default prevhost.exe runs as low-integrity; avoid HKCU/HKLM writes during preview; use LOCALAPPDATA (low-integrity writable) not ProgramData; IInitializeWithStream recommended for low-integrity compatibility |

</phase_requirements>

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ATL (Active Template Library) | MSVC bundled | COM infrastructure, threading, object lifetime | Microsoft's official framework for COM development; handles IUnknown, reference counting, threading models; built into Visual Studio |
| Windows SDK | Latest (Win10+) | IPreviewHandler, shell interfaces, Win32 APIs | Required for shobjidl_core.h (IPreviewHandler), propsys.h (IInitializeWithStream), ocidl.h (IObjectWithSite) |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| spdlog | 1.x (latest) | Structured logging with rolling files | HIGH confidence - Fast, header-only, async logging with rolling file sinks; widely used in C++ projects |
| SimpleIni | Latest | INI config file parsing | HIGH confidence - Cross-platform, header-only, Unicode support, MIT license; simpler than JSON for user-editable config |
| nlohmann/json | 3.x (latest) | JSON config parsing (if chosen over INI) | HIGH confidence - Single-header, widely adopted, intuitive API; use if preferring JSON over INI |
| TagLib | 2.x (latest) | Audio metadata (ID3, Vorbis, FLAC tags) | HIGH confidence - Fast (6x faster than id3lib), supports all major formats, stable API; use for full metadata parsing in Phase 2+ |
| mp3_id3_tags | Single-header | Lightweight MP3 ID3 parsing for Phase 1 | MEDIUM confidence - Public domain, single header; sufficient for basic header info without full TagLib dependency in Phase 1 |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| SimpleIni | nlohmann/json | JSON more modern/structured, but INI more user-editable and simpler for key-value config |
| spdlog | Windows Event Log API | Event Log integrates with Windows ecosystem, but harder to access/read for users; spdlog files are human-readable |
| ATL | WRL (Windows Runtime Library) | WRL is newer C++11 template-based COM, but ATL is more mature, better documented, and standard for shell extensions |
| IInitializeWithStream | IInitializeWithFile | IInitializeWithFile simpler (direct file path), but causes file locking issues; IInitializeWithStream is Microsoft-recommended |

**Installation:**
```bash
# spdlog (header-only)
git clone https://github.com/gabime/spdlog.git
# or via vcpkg
vcpkg install spdlog

# SimpleIni (header-only, single file)
# Download SimpleIni.h from https://github.com/brofield/simpleini

# nlohmann/json (if using JSON)
vcpkg install nlohmann-json

# TagLib (for Phase 2+, not Phase 1)
vcpkg install taglib
```

## Architecture Patterns

### Recommended Project Structure

```
Audex/
├── src/
│   ├── PreviewHandler/       # COM preview handler implementation
│   │   ├── AudioPreviewHandler.h/cpp    # Main IPreviewHandler class
│   │   ├── AudioPreviewHandler.idl      # COM type library definition
│   │   ├── AudioPreviewHandler.rgs      # ATL registry script
│   │   ├── dllmain.cpp                  # DLL entry point, DllRegisterServer
│   │   └── stdafx.h/cpp                 # Precompiled headers
│   ├── UI/                    # Preview pane UI rendering
│   │   ├── PreviewWindow.h/cpp          # HWND management, WM_PAINT
│   │   ├── ThemeHelper.h/cpp            # Dark/light mode detection
│   │   └── LayoutRenderer.h/cpp         # Placeholder skeleton layout
│   ├── FileReader/            # Audio file header parsing
│   │   ├── AudioFileInfo.h/cpp          # File info struct (duration, sample rate, etc.)
│   │   ├── WavHeaderParser.h/cpp        # WAV RIFF header parser
│   │   ├── Mp3HeaderParser.h/cpp        # MP3 frame header parser
│   │   └── FlacHeaderParser.h/cpp       # FLAC metadata block parser
│   ├── Config/                # Configuration management
│   │   ├── ConfigManager.h/cpp          # Read/write config from AppData
│   │   └── FileTypeRegistry.h/cpp       # Extension list management
│   └── Utils/                 # Shared utilities
│       ├── Logger.h/cpp                 # spdlog wrapper, rolling file setup
│       └── PathHelper.h/cpp             # AppData path resolution (SHGetKnownFolderPath)
├── res/                       # Resources
│   └── AudioPreviewHandler.rc           # Version info, manifest
└── tests/                     # Unit tests (Phase 1: minimal, expand later)
```

### Pattern 1: COM Preview Handler Lifecycle (IPreviewHandler Implementation)

**What:** Preview handlers follow strict initialization → render → cleanup lifecycle. Microsoft mandates lazy loading: defer all data loading until `DoPreview()`.

**When to use:** All IPreviewHandler implementations (this is the standard pattern).

**Example:**
```cpp
// Source: https://learn.microsoft.com/en-us/windows/win32/shell/building-preview-handlers

class AudioPreviewHandler :
    public CComObjectRootEx<CComSingleThreadModel>,
    public CComCoClass<AudioPreviewHandler, &CLSID_AudioPreviewHandler>,
    public IPreviewHandler,
    public IInitializeWithStream,
    public IObjectWithSite,
    public IOleWindow
{
private:
    CComPtr<IStream> m_pStream;        // File stream (set in Initialize)
    CComPtr<IUnknown> m_pSite;         // Host site (set in SetSite)
    HWND m_hwndParent;                 // Parent window (set in SetWindow)
    HWND m_hwndPreview;                // Our preview window (created in DoPreview)
    RECT m_rcParent;                   // Preview area (set in SetWindow/SetRect)

public:
    // IInitializeWithStream - STORE stream, DON'T load data yet
    IFACEMETHODIMP Initialize(IStream *pStream, DWORD grfMode)
    {
        m_pStream = pStream;  // Store for later
        return S_OK;          // DO NOT read file here!
    }

    // IPreviewHandler::SetWindow - Store parent HWND and area
    IFACEMETHODIMP SetWindow(HWND hwnd, const RECT *prc)
    {
        m_hwndParent = hwnd;
        m_rcParent = *prc;

        // If already rendering, reparent and resize
        if (m_hwndPreview && IsWindow(m_hwndPreview))
        {
            SetParent(m_hwndPreview, m_hwndParent);
            SetWindowPos(m_hwndPreview, NULL,
                m_rcParent.left, m_rcParent.top,
                m_rcParent.right - m_rcParent.left,
                m_rcParent.bottom - m_rcParent.top,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }
        return S_OK;
    }

    // IPreviewHandler::DoPreview - NOW load data and render
    IFACEMETHODIMP DoPreview()
    {
        // 1. Create preview window if it doesn't exist
        if (!m_hwndPreview)
        {
            m_hwndPreview = CreateWindowEx(
                0, L"AudioPreviewWindowClass", NULL,
                WS_CHILD | WS_VISIBLE,
                m_rcParent.left, m_rcParent.top,
                m_rcParent.right - m_rcParent.left,
                m_rcParent.bottom - m_rcParent.top,
                m_hwndParent, NULL, g_hInst, this);
        }

        // 2. Load file data from m_pStream (NOW, not in Initialize!)
        AudioFileInfo fileInfo;
        HRESULT hr = ParseAudioStream(m_pStream, &fileInfo);
        if (FAILED(hr))
        {
            DisplayError(L"Failed to parse audio file");
            return hr;
        }

        // 3. Render to preview window
        RenderPreview(fileInfo);
        return S_OK;
    }

    // IPreviewHandler::Unload - Release ALL resources
    IFACEMETHODIMP Unload()
    {
        if (m_hwndPreview)
        {
            DestroyWindow(m_hwndPreview);
            m_hwndPreview = NULL;
        }
        m_pStream.Release();  // Critical: release file stream!
        return S_OK;
    }
};
```

### Pattern 2: Avoiding File Locks with IInitializeWithStream

**What:** Use `IInitializeWithStream` instead of `IInitializeWithFile`. The host (Explorer) creates an IStream from the file, which the handler reads from. This allows Explorer to manage the file handle and prevent locks.

**When to use:** Always prefer IInitializeWithStream. Only use IInitializeWithFile if absolutely necessary (e.g., third-party library requires file path).

**Example:**
```cpp
// Source: https://learn.microsoft.com/en-us/archive/msdn-magazine/2007/january/windows-vista-and-office-writing-your-own-preview-handlers

// Read file data from IStream without locking the file
HRESULT ParseAudioStream(IStream* pStream, AudioFileInfo* pInfo)
{
    // Reset stream to beginning
    LARGE_INTEGER liZero = {0};
    HRESULT hr = pStream->Seek(liZero, STREAM_SEEK_SET, NULL);
    if (FAILED(hr)) return hr;

    // Read header (e.g., WAV first 44 bytes)
    BYTE header[44];
    ULONG cbRead = 0;
    hr = pStream->Read(header, sizeof(header), &cbRead);
    if (FAILED(hr) || cbRead < sizeof(header))
        return E_FAIL;

    // Parse header (no file lock - stream is in-memory copy)
    pInfo->format = ParseFormat(header);
    pInfo->sampleRate = ParseSampleRate(header);
    pInfo->bitDepth = ParseBitDepth(header);
    pInfo->duration = CalculateDuration(header, pStream);

    // Stream is managed by host - no need to close
    return S_OK;
}
```

### Pattern 3: Debouncing Rapid File Selection with SetTimer

**What:** When user rapidly browses files (arrow keys), use `SetTimer` to debounce and only load the final selected file, avoiding intermediate loads.

**When to use:** In `Initialize()` or before `DoPreview()`, when detecting rapid succession of file changes.

**Example:**
```cpp
// Source: General Windows debouncing pattern
// Context: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-settimer

#define TIMER_DEBOUNCE_ID 1
#define DEBOUNCE_DELAY_MS 150  // User decision: ~150ms

class AudioPreviewHandler
{
private:
    bool m_isFirstLoad;        // Track if first file load
    UINT_PTR m_debounceTimer;  // Active timer ID

public:
    AudioPreviewHandler() : m_isFirstLoad(true), m_debounceTimer(0) {}

    IFACEMETHODIMP Initialize(IStream *pStream, DWORD grfMode)
    {
        // Store new stream
        m_pStream = pStream;

        // First load: no debounce, load immediately
        if (m_isFirstLoad)
        {
            m_isFirstLoad = false;
            return S_OK;  // DoPreview will be called immediately
        }

        // Subsequent loads: debounce to skip intermediate files
        if (m_debounceTimer != 0)
        {
            // Kill existing timer - user is still browsing
            KillTimer(m_hwndPreview, m_debounceTimer);
        }

        // Set new timer - DoPreview will be called after delay
        m_debounceTimer = SetTimer(m_hwndPreview, TIMER_DEBOUNCE_ID,
            DEBOUNCE_DELAY_MS, NULL);

        return S_OK;
    }

    // Window procedure handles WM_TIMER
    LRESULT OnTimer(UINT uMsg, WPARAM wParam, LPARAM lParam)
    {
        if (wParam == TIMER_DEBOUNCE_ID)
        {
            KillTimer(m_hwndPreview, m_debounceTimer);
            m_debounceTimer = 0;

            // Timer expired - load the file (final selection)
            DoPreview();
        }
        return 0;
    }
};
```

### Pattern 4: Dark Mode Detection (Registry-based)

**What:** Windows has no official dark mode API. Use registry key `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (0 = dark, 1 = light).

**When to use:** On initialization and when responding to `WM_SETTINGCHANGE` notifications.

**Example:**
```cpp
// Source: https://github.com/microsoft/WindowsAppSDK/issues/5542 (workaround)
// Registry approach: undocumented but widely used

bool IsSystemInDarkMode()
{
    DWORD value = 1;  // Default to light mode
    DWORD size = sizeof(value);

    RegGetValue(HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
        L"AppsUseLightTheme",
        RRF_RT_REG_DWORD,
        NULL,
        &value,
        &size);

    return (value == 0);  // 0 = dark mode, 1 = light mode
}

COLORREF GetThemeBackgroundColor()
{
    return IsSystemInDarkMode() ? RGB(32, 32, 32) : RGB(255, 255, 255);
}

COLORREF GetThemeTextColor()
{
    return IsSystemInDarkMode() ? RGB(255, 255, 255) : RGB(0, 0, 0);
}

// Listen for theme changes
LRESULT OnSettingChange(WPARAM wParam, LPARAM lParam)
{
    if (lParam && wcscmp((LPCWSTR)lParam, L"ImmersiveColorSet") == 0)
    {
        // Theme changed - refresh colors and repaint
        InvalidateRect(m_hwndPreview, NULL, TRUE);
    }
    return 0;
}
```

### Pattern 5: WAV Header Parsing (Minimal, No Full Decode)

**What:** Read WAV RIFF header (first 44 bytes) to extract sample rate, bit depth, channels, duration without decoding audio data.

**When to use:** Phase 1 file info display; full decoding deferred to Phase 2 (BASS library).

**Example:**
```cpp
// Source: https://truelogic.org/wordpress/2015/09/04/parsing-a-wav-file-in-c/
// Adapted for IStream

#pragma pack(push, 1)
struct WavHeader
{
    char riff[4];           // "RIFF"
    uint32_t fileSize;
    char wave[4];           // "WAVE"
    char fmt[4];            // "fmt "
    uint32_t fmtSize;
    uint16_t audioFormat;   // 1 = PCM
    uint16_t numChannels;
    uint32_t sampleRate;
    uint32_t byteRate;
    uint16_t blockAlign;
    uint16_t bitsPerSample;
    char data[4];           // "data"
    uint32_t dataSize;
};
#pragma pack(pop)

HRESULT ParseWavHeader(IStream* pStream, AudioFileInfo* pInfo)
{
    WavHeader header;
    ULONG cbRead = 0;

    // Read header
    HRESULT hr = pStream->Read(&header, sizeof(header), &cbRead);
    if (FAILED(hr) || cbRead < sizeof(header))
        return E_FAIL;

    // Validate RIFF/WAVE
    if (memcmp(header.riff, "RIFF", 4) != 0 ||
        memcmp(header.wave, "WAVE", 4) != 0)
        return E_INVALIDARG;

    // Extract info
    pInfo->sampleRate = header.sampleRate;
    pInfo->bitDepth = header.bitsPerSample;
    pInfo->channels = header.numChannels;

    // Calculate duration (seconds) = dataSize / byteRate
    pInfo->duration = (double)header.dataSize / header.byteRate;

    return S_OK;
}
```

### Anti-Patterns to Avoid

- **Loading data in Initialize():** Microsoft explicitly forbids this. Always defer to `DoPreview()`. Loading early causes performance issues and violates the contract.
- **Forgetting Unload() cleanup:** Not releasing `IStream` or destroying windows in `Unload()` causes file locks and memory leaks. Always pair resource allocation with cleanup.
- **Using IInitializeWithFile for convenience:** Causes file locking issues. Use `IInitializeWithStream` even though it requires more code.
- **Synchronous long operations in DoPreview():** DoPreview should complete quickly (<200ms). For Phase 1, header parsing is fast; Phase 2+ will need async loading with progress indication.
- **Writing to ProgramData or HKLM from handler:** Low-integrity process cannot write to these locations. Use `%LOCALAPPDATA%` (writable by low-integrity processes).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| COM object lifetime, IUnknown implementation | Manual AddRef/Release, QueryInterface | ATL CComObjectRootEx, COM_INTERFACE_MAP | ATL handles reference counting, threading, aggregation, tear-off interfaces; manual implementation is error-prone |
| Rolling log files with rotation | Custom file I/O with date/size checks | spdlog::rotating_file_sink | Handles multi-process locking, atomic rotation, async logging, header-only, battle-tested |
| INI/JSON parsing | Manual string parsing, GetPrivateProfileString | SimpleIni or nlohmann/json | SimpleIni handles Unicode, multi-line values, case-sensitivity; nlohmann/json is standards-compliant; manual parsing has injection/encoding bugs |
| Registry COM registration | Manual RegCreateKeyEx calls | ATL .rgs scripts + DllRegisterServer | .rgs files are declarative, versioned, support uninstall; manual registration misses HKCR\CLSID vs HKCU\Software\Classes subtleties |
| Audio file header parsing | Reading bytes and bit-shifting magic numbers | Format-specific libraries (mp3_id3_tags, TagLib Phase 2+) | WAV: non-standard headers, extended formats; MP3: variable header positions, frame sync; FLAC: metadata blocks; libraries handle edge cases |
| Dark mode theme detection | Hardcoded colors or GetSysColor | Registry query + WM_SETTINGCHANGE listener | GetSysColor doesn't support dark mode; registry query is undocumented but standard workaround until official API exists |

**Key insight:** COM shell extensions run in-process inside Explorer or out-of-process in surrogate hosts. Bugs cause Explorer crashes or slowdowns affecting millions of users. Use battle-tested libraries for all non-trivial operations. Microsoft's ATL exists specifically because manual COM is too error-prone for production use.

## Common Pitfalls

### Pitfall 1: File Locking After Preview

**What goes wrong:** After closing preview pane, user cannot delete/rename the file. "File is in use" error appears.

**Why it happens:**
- Using `IInitializeWithFile` instead of `IInitializeWithStream` leaves file handle open
- Forgetting to release `IStream` in `Unload()`
- Storing file path and reopening file in `DoPreview()` without closing

**How to avoid:**
- Always implement `IInitializeWithStream`, never `IInitializeWithFile` (unless third-party library absolutely requires file path)
- In `Unload()`, explicitly `Release()` the `IStream` and set pointer to NULL
- Never cache file handles or FILE* pointers across method calls
- Test: After previewing file, immediately try to delete it in Explorer

**Warning signs:**
- File explorer shows "waiting" cursor after closing preview pane
- Cannot delete file without restarting Explorer
- Process Explorer shows file handle held by `prevhost.exe`

### Pitfall 2: Explorer Crashes Due to Unhandled Exceptions

**What goes wrong:** Preview handler throws exception (null pointer, out of bounds), causing `prevhost.exe` or Explorer to crash.

**Why it happens:**
- COM methods must return HRESULTs, not throw exceptions
- Not validating IStream before reading (seek past end, read beyond data)
- Assuming 44-byte WAV header when file has extended format chunks
- Dereferencing NULL parent HWND in `SetWindow()` before checking

**How to avoid:**
- Wrap all preview handler methods in `try/catch`, convert exceptions to `E_FAIL` or specific HRESULT
- Validate all inputs: check IStream size before reading, check HWND validity before creating child windows
- Test with malformed files: truncated WAV, zero-byte file, non-audio file with .mp3 extension
- Use ATL's `ATLASSERT` in debug builds, graceful degradation in release

**Warning signs:**
- Event Viewer shows "Application Error: prevhost.exe" after selecting certain files
- Explorer preview pane shows blank, then Explorer freezes/restarts
- Visual Studio debugger breaks on unhandled exception during attach-to-process debugging

### Pitfall 3: Memory Leaks from Event Handler Registration

**What goes wrong:** Preview handler allocates memory or registers event handlers in `DoPreview()`, but never cleans up in `Unload()`, causing cumulative memory leaks as user browses files.

**Why it happens:**
- Forgetting to unsubscribe from window message handlers or timers in `Unload()`
- Allocating GDI objects (HBRUSH, HFONT) on each render without deleting old ones
- Creating child windows without destroying them when parent is destroyed
- Timer created in `Initialize()` debounce logic, but `KillTimer()` only called on window destruction (timer outlives window if Unload called before DoPreview)

**How to avoid:**
- Pair all resource allocations with cleanup: `CreateWindow` → `DestroyWindow`, `SetTimer` → `KillTimer`, `CreateFont` → `DeleteObject`
- In `Unload()`, explicitly release all: destroy timers, delete GDI objects, release COM interfaces, free memory
- Use RAII wrappers: `CComPtr` for COM, `unique_ptr` for heap memory, scoped objects for GDI (or ATL's `CFont`, `CBrush` wrappers)
- Test: Open Task Manager, preview 100 files rapidly while watching `prevhost.exe` memory usage (should stay constant after initial ramp)

**Warning signs:**
- `prevhost.exe` memory usage grows linearly with number of files previewed
- Visual Studio Diagnostic Tools Memory Usage shows increasing heap allocations
- After previewing many files, system feels sluggish; closing Explorer frees large memory

### Pitfall 4: Low-Integrity Process Permission Errors

**What goes wrong:** Preview handler attempts to write to `%ProgramData%`, `HKLM` registry, or user's Documents folder, resulting in "Access Denied" errors when running in low-integrity `prevhost.exe`.

**Why it happens:**
- By default, preview handlers run in low-integrity process for security (sandbox)
- Low-integrity processes can only write to low-integrity locations: `%LOCALAPPDATA%`, `%TEMP%`
- Attempting to write logs to `C:\Program Files\Audex\logs\` fails silently or throws

**How to avoid:**
- Always use `%LOCALAPPDATA%` (low-integrity writable) for logs, config, cache: `SHGetKnownFolderPath(FOLDERID_LocalAppData, ...)`
- Never write to `%APPDATA%` (roaming profile), `%ProgramData%`, or program installation directory from preview handler
- Registry writes (if any) should be to `HKCU\Software\...`, not `HKLM` (low-integrity can read HKLM, not write)
- Test in low-integrity mode: run `icacls prevhost.exe /setintegritylevel low` and verify logging/config still works

**Warning signs:**
- Logging works when running handler in test harness, fails in Explorer
- `GetLastError()` returns `ERROR_ACCESS_DENIED` when creating log file
- Config file changes in dev environment, but not when installed and run via Explorer

### Pitfall 5: Ignoring DPI Awareness (Blurry UI on High-DPI)

**What goes wrong:** Preview pane UI appears blurry on high-DPI displays (4K, Surface, etc.). Text is fuzzy, layout skeleton lines are thick/thin inconsistently.

**Why it happens:**
- Preview handler doesn't declare DPI awareness in manifest
- System assumes DPI-unaware, applies bitmap stretching (96 DPI → actual DPI)
- Window coordinates/fonts calculated for 96 DPI, then scaled up by OS

**How to avoid:**
- Add DPI awareness to application manifest: `<dpiAware>true/PM</dpiAware>` (Per-Monitor V2 awareness)
- Call `SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` in `DllMain` or class constructor
- Handle `WM_DPICHANGED` message: re-calculate font sizes, window positions when DPI changes (user drags window between monitors)
- Use `GetDpiForWindow(hwnd)` to get current DPI, scale all sizes accordingly: `scaledSize = (size * dpi) / 96`

**Warning signs:**
- UI looks crisp on 1080p monitor, blurry on 4K monitor
- Moving Explorer window between monitors causes preview pane to re-scale incorrectly
- Debugger shows `GetDpiForWindow()` returning 96 on 4K display (DPI virtualization active)

### Pitfall 6: Hardcoded Color Schemes (Light Mode Only)

**What goes wrong:** Preview pane always shows white background with black text, even when Windows is in dark mode. Blindingly bright in dark environments.

**Why it happens:**
- Using hardcoded colors: `RGB(255,255,255)` background, `RGB(0,0,0)` text
- Assuming `GetSysColor(COLOR_WINDOW)` returns dark colors in dark mode (it doesn't - returns light colors)
- Not listening for `WM_SETTINGCHANGE` to detect theme changes while preview pane is open

**How to avoid:**
- Query registry `HKCU\...\Personalize\AppsUseLightTheme` on initialization to detect dark mode
- Listen for `WM_SETTINGCHANGE` with `lParam = "ImmersiveColorSet"` to detect theme change, repaint with new colors
- Use theme-aware colors: if dark mode, use `RGB(32,32,32)` background + `RGB(255,255,255)` text; else reverse
- Optional: Implement `IPreviewHandlerVisuals` to receive host-provided colors (Explorer may provide these in future)

**Warning signs:**
- UI looks correct in light mode, but white/bright in dark mode
- Comparing to built-in preview handlers (TXT, PDF) shows they respect dark mode, yours doesn't
- User reports "blindingly bright preview pane at night"

## Code Examples

Verified patterns from official sources:

### COM Registration Script (.rgs file)

```rgs
// Source: https://learn.microsoft.com/en-us/windows/win32/shell/how-to-register-a-preview-handler
// ATL Registry Script for preview handler

HKCR
{
    NoRemove .mp3
    {
        val = s 'Audex.mp3'
    }
    Audex.mp3
    {
        NoRemove shellex
        {
            {8895b1c6-b41f-4c1c-a562-0d564250836f} = s '{YOUR-CLSID-GUID-HERE}'
        }
    }
    NoRemove CLSID
    {
        ForceRemove {YOUR-CLSID-GUID-HERE} = s 'Audex Handler'
        {
            val AppID = s '{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}'  // Prevhost.exe
            val DisplayName = s '@%MODULE%,-101'
            InprocServer32 = s '%MODULE%'
            {
                val ThreadingModel = s 'Apartment'
            }
            val ProgID = s 'Audex.mp3'
        }
    }
}

HKLM
{
    NoRemove SOFTWARE
    {
        NoRemove Microsoft
        {
            NoRemove Windows
            {
                NoRemove CurrentVersion
                {
                    NoRemove PreviewHandlers
                    {
                        ForceRemove {YOUR-CLSID-GUID-HERE} = s 'Audex Handler'
                    }
                }
            }
        }
    }
}
```

### Logging Setup with spdlog

```cpp
// Source: https://github.com/gabime/spdlog (official examples)
// Setup rolling log file in %LOCALAPPDATA%/Audex/logs/

#include <spdlog/spdlog.h>
#include <spdlog/sinks/rotating_file_sink.h>
#include <shlobj.h>

std::shared_ptr<spdlog::logger> InitializeLogger()
{
    // Get %LOCALAPPDATA% path
    PWSTR pszPath = NULL;
    SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, NULL, &pszPath);
    std::wstring localAppData(pszPath);
    CoTaskMemFree(pszPath);

    // Create log directory: %LOCALAPPDATA%/Audex/logs/
    std::wstring logDir = localAppData + L"\\Audex\\logs";
    CreateDirectoryW(logDir.c_str(), NULL);

    // Log file path
    std::wstring logPath = logDir + L"\\Audex.log";

    // Convert to UTF-8 for spdlog
    std::string logPathUtf8 = WStringToUtf8(logPath);

    // Create rotating logger: 10MB max size, 3 rotated files
    auto logger = spdlog::rotating_logger_mt(
        "Audex",
        logPathUtf8,
        1024 * 1024 * 10,  // 10MB
        3                   // Keep 3 old logs
    );

    // Set log level from config (default: info)
    logger->set_level(spdlog::level::info);
    logger->set_pattern("[%Y-%m-%d %H:%M:%S.%e] [%l] %v");

    return logger;
}

// Usage in preview handler
void AudioPreviewHandler::DoPreview()
{
    try
    {
        auto logger = spdlog::get("Audex");
        logger->info("Loading preview for file");

        // ... preview logic ...
    }
    catch (const std::exception& e)
    {
        auto logger = spdlog::get("Audex");
        logger->error("Preview failed: {}", e.what());
        return E_FAIL;
    }
}
```

### Config File Loading (SimpleIni)

```cpp
// Source: https://github.com/brofield/simpleini (official example)
// Read config from %LOCALAPPDATA%/Audex/config.ini

#include "SimpleIni.h"

struct PreviewConfig
{
    std::vector<std::wstring> supportedExtensions;
    spdlog::level::level_enum logLevel;
    int debounceMs;
};

PreviewConfig LoadConfig()
{
    // Get config path: %LOCALAPPDATA%/Audex/config.ini
    PWSTR pszPath = NULL;
    SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, NULL, &pszPath);
    std::wstring configPath = std::wstring(pszPath) + L"\\Audex\\config.ini";
    CoTaskMemFree(pszPath);

    CSimpleIniW ini;
    ini.SetUnicode();
    SI_Error rc = ini.LoadFile(configPath.c_str());

    PreviewConfig config;

    if (rc < 0)
    {
        // Config doesn't exist - create defaults
        config.supportedExtensions = {L".wav", L".mp3", L".flac", L".aiff",
            L".ogg", L".aac", L".wma", L".opus", L".m4a"};
        config.logLevel = spdlog::level::info;
        config.debounceMs = 150;
        return config;
    }

    // Read extensions (comma-separated)
    const wchar_t* exts = ini.GetValue(L"FileTypes", L"Extensions",
        L".wav,.mp3,.flac,.aiff,.ogg,.aac,.wma,.opus,.m4a");
    // Parse comma-separated into vector...

    // Read log level
    const wchar_t* level = ini.GetValue(L"Logging", L"Level", L"info");
    if (wcscmp(level, L"debug") == 0)
        config.logLevel = spdlog::level::debug;
    else if (wcscmp(level, L"warning") == 0)
        config.logLevel = spdlog::level::warn;
    else
        config.logLevel = spdlog::level::info;

    config.debounceMs = ini.GetLongValue(L"Performance", L"DebounceMs", 150);

    return config;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| IInitializeWithFile (direct file path) | IInitializeWithStream (stream-based) | Windows Vista (2007) | Avoids file locking, enables virtual file previews (e.g., inside ZIP), required for low-integrity compatibility |
| GetSysColor() for theming | Registry query + WM_SETTINGCHANGE | Windows 10 (2019) dark mode | GetSysColor doesn't support dark mode; registry workaround is unofficial but standard until API exists |
| Manual DPI scaling | Per-Monitor V2 DPI awareness | Windows 10 1607 (2016) | System DPI awareness causes blurry UI; Per-Monitor V2 handles multi-monitor DPI correctly |
| stdafx.h (precompiled header) | pch.h | Visual Studio 2017 | Naming convention change only; functionality identical |
| AppID points to DLL | AppID points to Prevhost.exe | Windows Vista (2007) | Out-of-process hosting protects Explorer from handler crashes |

**Deprecated/outdated:**
- **IExtractImage (thumbnail handlers):** Replaced by `IThumbnailProvider` in Vista. Old interface still works but deprecated.
- **32-bit handlers on 64-bit Windows without separate AppID:** 64-bit Windows requires 32-bit handlers use AppID `{534A1E02-D58F-44f0-B58B-36CBED287C7C}` for WOW64 compatibility.

## Open Questions

### 1. Official Dark Mode API Timeline

**What we know:** Windows 10/11 added dark mode to Explorer, but no public API for detecting it. Registry workaround (`AppsUseLightTheme`) works but is undocumented.

**What's unclear:** Will Microsoft provide official `IPreviewHandlerVisuals` implementation in Explorer, or is registry query the long-term solution?

**Recommendation:** Use registry query for Phase 1. Monitor Windows SDK updates for official API. If `IPreviewHandlerVisuals::SetBackgroundColor` is called by Explorer in future builds, respect it (Phase 7 refinement).

### 2. Performance Target for Header Parsing

**What we know:** User expects <200ms load time. WAV header parsing is ~1ms. MP3 frame scanning can be 10-100ms depending on file size (need to scan for first valid frame).

**What's unclear:** Should Phase 1 show partial info immediately (filename, size) then async-load header info, or block DoPreview until header parsed?

**Recommendation:** Block DoPreview for up to 50ms for header parsing. If parsing exceeds 50ms (large/malformed file), display partial info + "Analyzing..." text, then update when complete. Keep it simple for Phase 1; optimize in Phase 2+ if needed.

### 3. Config File Format: INI vs JSON

**What we know:**
- INI: Simpler, more user-editable, Windows-native (though GetPrivateProfileString is legacy)
- JSON: Modern, structured, better for nested config (future: per-format settings)

**What's unclear:** User preference not specified. Both are acceptable per "Claude's Discretion."

**Recommendation:** Use **INI for Phase 1** for simplicity. Extensions list is flat key-value, log level is simple string. If Phase 5+ requires nested config (per-format BASS plugin settings), migrate to JSON. SimpleIni is header-only, minimal dependency.

## Sources

### Primary (HIGH confidence)

- [Building Preview Handlers - Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/building-preview-handlers) - Official implementation guide, required interfaces, DoPreview lifecycle
- [How to Register a Preview Handler - Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-register-a-preview-handler) - Registry structure, CLSID/ProgID/AppID configuration
- [Preview Handlers and Shell Preview Host - Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/preview-handlers) - Process isolation, prevhost.exe surrogate, low-integrity process details
- [IInitializeWithFile vs IInitializeWithStream - MSDN Forums](https://social.msdn.microsoft.com/Forums/en-US/e6d87750-a206-4dba-8517-232a166348d8/iinitializewithfile-vs-iinitializewithstream) - File locking comparison
- [Writing Your Own Preview Handlers - Microsoft Learn Archive](https://learn.microsoft.com/en-us/archive/msdn-magazine/2007/january/windows-vista-and-office-writing-your-own-preview-handlers) - Historical context, IStream usage patterns
- [High DPI Desktop Application Development - Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows) - DPI awareness modes, WM_DPICHANGED handling

### Secondary (MEDIUM confidence)

- [spdlog GitHub](https://github.com/gabime/spdlog) - Performance benchmarks, rotating_file_sink examples
- [SimpleIni GitHub](https://github.com/brofield/simpleini) - API documentation, Unicode support
- [nlohmann/json GitHub](https://github.com/nlohmann/json) - Single-header JSON parser
- [TagLib Official Site](https://taglib.org/) - Audio metadata library, performance claims (6x faster than id3lib)
- [Parsing a WAV file in C - Truelogic Blog](https://truelogic.org/wordpress/2015/09/04/parsing-a-wav-file-in-c/) - WAV header structure details
- [How to detect Windows dark mode - Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/715081/how-to-detect-windows-dark-mode) - Registry workaround for dark mode detection
- [Windows Shell Extensions: Basics, Examples, and Common Problems - Apriorit](https://www.apriorit.com/dev-blog/357-shell-extentions-basics-samples-common-problems) - Memory management pitfalls, threading issues
- [.NET Shell Extensions - Shell Preview Handlers - CodeProject](https://www.codeproject.com/Articles/533948/NET-Shell-Extensions-Shell-Preview-Handlers) - Testing with Server Manager tool, debugging prevhost.exe

### Tertiary (LOW confidence, requires validation)

- [Feature Request: API to detect dark mode - Windows App SDK GitHub Issue](https://github.com/microsoft/WindowsAppSDK/issues/5542) - Community discussion on dark mode API absence (confirms no official API as of 2024)
- [Event Handler Leaks - Visual Studio Blog](https://devblogs.microsoft.com/visualstudio/unlocking-the-secrets-of-managed-memory-dive-into-event-handler-leak-insights/) - General memory leak patterns (not shell-extension-specific, but applicable)

## Metadata

**Confidence breakdown:**
- Standard stack: **HIGH** - ATL is Microsoft's official COM framework, IPreviewHandler is documented standard
- Architecture: **HIGH** - Microsoft documentation provides clear lifecycle requirements, patterns verified in production preview handlers
- Pitfalls: **MEDIUM-HIGH** - File locking, low-integrity, DPI issues verified in Microsoft docs; event handler leaks generalized from .NET guidance

**Research date:** 2026-02-16
**Valid until:** 90 days (2026-05-17) - COM/ATL/Windows Shell APIs are stable, dark mode workaround may change if Microsoft releases official API

**Technologies researched:**
- Windows COM Shell Extensions (IPreviewHandler, IInitializeWithStream)
- ATL (Active Template Library) for COM infrastructure
- Windows theming (dark mode detection, DPI awareness)
- Audio file header parsing (WAV, MP3, FLAC)
- Logging (spdlog) and configuration (SimpleIni, nlohmann/json)
- Process isolation (prevhost.exe, low-integrity processes)
- Registry-based registration and file type associations
