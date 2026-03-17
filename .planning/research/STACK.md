# Technology Stack

**Project:** Audex
**Researched:** 2026-02-16
**Overall Confidence:** MEDIUM (training data only, web verification unavailable)

## Recommended Stack

### Core Framework

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| .NET 8.0 SDK | 8.0.x | Runtime framework | Long-term support (LTS), best performance for WPF, required for C# 12 features. Modern COM interop improvements. |
| WPF (Windows Presentation Foundation) | Built-in .NET 8 | UI framework | Native Windows UI, hardware acceleration for waveform rendering, XAML databinding for controls. Standard for Windows desktop apps. |
| C# | 12.0 | Programming language | Excellent COM interop, strong typing for audio buffer management, async/await for file I/O. |

**Confidence:** MEDIUM - .NET 8 was LTS as of training cutoff, WPF remains standard for Windows desktop UI.

### Audio Engine

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| BASS Audio Library | 2.4.x | Core audio engine | Industry standard for Windows audio, low-latency playback, extensive format support via plugins, WASAPI/ASIO support. |
| BASS.NET | 2.4.x | C# wrapper for BASS | Official .NET wrapper, handles marshaling, integrates with .NET memory management. |

**Confidence:** LOW - Training data from 2024-2025, versions need verification from un4seen.com.

**BASS Plugin Matrix for Format Support:**

| Format | Plugin | Version | NuGet/Source | Notes |
|--------|--------|---------|--------------|-------|
| WAV, AIFF | bass.dll | Core | un4seen.com | Native support, no plugin |
| MP3 | bass.dll | Core | un4seen.com | Native support, no plugin |
| OGG Vorbis | bass.dll | Core | un4seen.com | Native support, no plugin |
| FLAC | bassflac.dll | 2.4.x | un4seen.com | Free plugin |
| AAC/M4A | bass_aac.dll | 2.4.x | un4seen.com | Free plugin, supports ALAC |
| WMA | basswma.dll | 2.4.x | un4seen.com | Free plugin, Windows Media codecs |
| OPUS | bassopus.dll | 2.4.x | un4seen.com | Free plugin |
| Module formats (MOD, XM, IT, S3M) | bassmod.dll or bass.dll | 2.4.x | un4seen.com | Some formats in core, others need plugin |

**Installation Note:** BASS plugins are native DLLs that must be deployed alongside bass.dll. They're auto-detected at runtime when in the same directory.

**Confidence:** MEDIUM - Plugin architecture is stable, but versions need verification.

### COM Shell Extension

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| System.Runtime.InteropServices | Built-in .NET 8 | COM interop | Standard .NET COM marshaling, [ComVisible], [Guid] attributes. |
| Microsoft.Windows.CsWin32 | 0.3.x+ | Windows API interop | Source-generated P/Invoke, type-safe Windows APIs, eliminates manual COM definitions. Preferred over SharpShell. |

**Confidence:** LOW - CsWin32 version needs verification from NuGet.

**Alternatives Considered:**

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| COM Framework | Direct COM interop + CsWin32 | SharpShell | SharpShell abstracts COM but adds complexity, maintenance concerns (last significant updates ~2019-2020), harder to debug COM registration issues. Direct COM gives full control. |
| COM Registration | Manual regasm + registry | SharpShell's registration | Direct control over registration, easier troubleshooting, no framework dependency at install time. |

### Waveform Rendering

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| WriteableBitmapEx | 1.6.x+ | WPF bitmap manipulation | Fast direct pixel access, optimized for WPF, hardware acceleration compatible. Better than SkiaSharp for WPF integration. |
| BASS Sample Data API | Core BASS | Audio sample access | BASS_ChannelGetData() provides decoded PCM samples for analysis/visualization. |

**Confidence:** MEDIUM - WriteableBitmapEx is established library, BASS API is stable.

**Rendering Approach:**
- Use BASS to decode entire file to PCM samples
- Calculate peak/RMS values per time bucket (e.g., 2048 samples)
- Use FFT (BASS_ChannelGetData with BASS_DATA_FFT8192) for frequency analysis per bucket
- Map frequency bands to colors (low=red, mid=yellow, high=blue)
- Render to WriteableBitmap via WriteableBitmapEx extensions
- Display in WPF Image control

**Alternatives Considered:**

| Recommended | Alternative | Why Not |
|-------------|-------------|---------|
| WriteableBitmapEx | SkiaSharp | SkiaSharp adds cross-platform rendering overhead unnecessary for Windows-only app. WPF's WriteableBitmap has better native integration. |
| WriteableBitmapEx | Custom DrawingVisual | DrawingVisual requires more boilerplate, WriteableBitmapEx provides pixel-level control with less code. |
| Render on demand | Pre-render entire waveform | IPreviewHandler should render quickly; pre-render strategy reduces UI thread blocking. |

### Metadata Handling

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| TagLibSharp | 2.3.x+ | ID3/metadata reading | Industry standard, supports all common tag formats (ID3v1/v2, Vorbis Comments, APEv2, MP4), maintained, NuGet available. |
| ATL.NET | 4.x+ | Advanced tagging | Optional alternative for exotic formats, supports more niche module formats, but TagLibSharp sufficient for stated requirements. |

**Confidence:** LOW - TagLibSharp versions need NuGet verification.

**Metadata Strategy:**
1. Use TagLibSharp first for standard tags (artist, title, album, BPM, key)
2. Check custom tags for BPM/key (various DAW formats: "BPM", "TBPM", "INITIALKEY", "KEY")
3. Fallback to audio analysis if tags missing

### BPM/Key Detection

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| QM-DSP via P/Invoke or CLI | Latest | BPM/beat detection | Queen Mary DSP library, research-grade algorithms, used by Sonic Visualiser. C++ library requires wrapper. |
| Essentia (CLI) | 2.1+ | Key/tempo detection | Comprehensive audio analysis, pre-built Windows binaries, call via Process.Start(), parse JSON output. |
| KeyFinder CLI | 2.x | Musical key detection | Specialized for key detection, open-source, Windows binary available. |

**Confidence:** LOW - Ecosystem may have changed, need verification.

**Fallback Approach:**
- BASS provides FFT data (BASS_ChannelGetData with BASS_DATA_FFT*)
- Implement basic BPM detection via onset detection + autocorrelation
- Implement basic key detection via chroma features + template matching
- Lower accuracy than specialized libraries but zero external dependencies

**Recommended Strategy:** Start with external CLI tools (Essentia/KeyFinder), add BASS-based fallback in later phase if needed.

### Configuration Management

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| System.Text.Json | Built-in .NET 8 | JSON parsing | Modern, fast, built-in, preferred over Newtonsoft.Json for greenfield projects. Source generators for AOT-friendly serialization. |

**Confidence:** HIGH - System.Text.Json is standard for modern .NET.

**Config Location:** `%APPDATA%/Audex/config.json`

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Extensions.Logging | 8.x | Logging framework | Production builds, troubleshooting COM registration issues, debugging preview handler lifecycle. |
| Serilog + Serilog.Sinks.File | Latest | Logging implementation | For file-based logging to `%TEMP%` (COM process can't use console). |
| CommunityToolkit.Mvvm | 8.x+ | MVVM helpers | Simplifies databinding, INotifyPropertyChanged, RelayCommand. Reduces boilerplate in WPF viewmodels. |

**Confidence:** MEDIUM - These are stable, widely-used libraries but versions need NuGet verification.

## Installation Commands

### NuGet Packages

```powershell
# Core Framework (WPF project targeting net8.0-windows)
dotnet new wpf -n Audex -f net8.0-windows

# COM Interop
dotnet add package Microsoft.Windows.CsWin32

# Waveform Rendering
dotnet add package WriteableBitmapEx

# Metadata
dotnet add package TagLibSharp

# Configuration
# System.Text.Json is built-in, no package needed

# Logging
dotnet add package Microsoft.Extensions.Logging
dotnet add package Serilog
dotnet add package Serilog.Sinks.File

# MVVM Helpers
dotnet add package CommunityToolkit.Mvvm
```

### BASS Audio Library

BASS is not on NuGet. Manual installation required:

```powershell
# Download from un4seen.com
# bass.dll (core)
# bassflac.dll, bass_aac.dll, basswma.dll, bassopus.dll (plugins)
# BASS.NET.dll (managed wrapper)

# Place in project directory:
# /lib/bass/x64/bass*.dll
# /lib/bass/Bass.Net.dll

# Add to .csproj:
<ItemGroup>
  <None Include="lib\bass\x64\*.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>%(Filename)%(Extension)</Link>
  </None>
  <Reference Include="Bass.Net">
    <HintPath>lib\bass\Bass.Net.dll</HintPath>
  </Reference>
</ItemGroup>
```

**Confidence:** MEDIUM - BASS installation pattern is stable but verify current NuGet wrapper status.

## COM Registration Approach

### Registration Strategy

**Recommended:** Manual COM registration via custom installer or regasm.

```powershell
# Development registration
regasm /codebase Audex.dll

# Production registration (via installer)
# Use WiX Toolset or Advanced Installer to:
# 1. Register COM server
# 2. Create registry entries under HKLM\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers
# 3. Associate file extensions
```

**Registry Keys Required:**

```
HKLM\Software\Classes\CLSID\{YOUR-GUID}\InprocServer32
HKLM\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers\{YOUR-GUID}
HKLM\Software\Classes\.mp3\ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}
HKLM\Software\Classes\.flac\ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}
... (repeat for all supported extensions)
```

**Confidence:** HIGH - COM registration pattern is stable Windows API.

### Development Setup

```powershell
# Enable preview pane in Explorer
# Set HKCU\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers\DisablePreview = 0

# Restart Explorer after registration
taskkill /f /im explorer.exe & start explorer.exe
```

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| NAudio for BASS replacement | NAudio lacks low-latency WASAPI Exclusive mode, no built-in ASIO support, plugin ecosystem not as mature as BASS. | BASS.NET with plugins |
| Newtonsoft.Json | Heavier, slower, not needed for greenfield .NET 8+ projects. System.Text.Json is built-in and faster. | System.Text.Json |
| SharpShell framework | Maintenance concerns, abstracts away COM details making debugging harder. Adds dependency. | Direct COM interop with CsWin32 |
| .NET Framework 4.x | Legacy, slower, missing modern C# features. .NET 8 has better performance and cross-platform tooling. | .NET 8 |
| SkiaSharp for waveforms | Overkill for Windows-only WPF app, adds cross-platform overhead. | WriteableBitmapEx + WPF |
| WPF MediaElement | No low-level control, can't extract samples for waveform, can't access WASAPI Exclusive. | BASS.NET |

**Confidence:** MEDIUM to HIGH - These anti-patterns are based on stable architectural concerns.

## Target Configuration

### Project File (.csproj) Key Settings

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Platforms>x64</Platforms>
    <EnableComHosting>true</EnableComHosting>
    <GenerateAssemblyInfo>true</GenerateAssemblyInfo>
    <LangVersion>12.0</LangVersion>
  </PropertyGroup>
</Project>
```

**Why 64-bit only:**
- Explorer is 64-bit on modern Windows
- COM preview handlers must match Explorer process architecture
- Simplifies BASS plugin deployment (no x86/x64 dual deployment)

**Confidence:** HIGH - Windows architecture requirements are stable.

## Version Compatibility Matrix

| Component | Version | Compatible With | Notes |
|-----------|---------|-----------------|-------|
| .NET SDK | 8.0.x | Windows 10 1809+ | Requires .NET 8 Runtime installed |
| BASS.dll | 2.4.x | Windows 7+ | 64-bit version required |
| WriteableBitmapEx | 1.6.x | .NET 8 WPF | Check NuGet for .NET 8 compatibility |
| TagLibSharp | 2.3.x+ | .NET Standard 2.0+ | Compatible with .NET 8 |
| CommunityToolkit.Mvvm | 8.x | .NET 8 | Source generators require C# 10+ |

**Confidence:** LOW - Versions need verification from NuGet/official sources.

## Performance Considerations

### Audio Decoding
- BASS decoding is highly optimized, decode entire file to memory for files < 100MB
- For larger files, implement streaming with chunked waveform rendering

### Waveform Rendering
- Target 1000-2000 buckets per waveform (represents ~0.1-0.2s per pixel on typical screen)
- Use background thread for analysis, marshal WriteableBitmap updates to UI thread
- Cache rendered waveforms in memory (cleared on preview close)

### COM Thread Safety
- IPreviewHandler methods called on STA thread
- BASS must be initialized on same thread as playback
- Use Dispatcher for all WPF UI updates from BASS callbacks

**Confidence:** HIGH - These are established patterns for preview handlers and audio apps.

## Deployment Strategy

### Installer Requirements
1. .NET 8 Desktop Runtime (prereq check/install)
2. Visual C++ Redistributable 2015-2022 (BASS dependency)
3. Application DLLs (Audex.dll, BASS libs)
4. COM registration (regasm or WiX component)
5. Registry entries for preview handler + file associations

**Recommended Installer:** WiX Toolset 4.x (MSI) or Advanced Installer (proprietary, easier GUI).

**Confidence:** MEDIUM - Standard Windows deployment but verify WiX 4.x status.

## Sources

**Confidence Warning:** All recommendations based on training data (knowledge cutoff January 2025). Web verification tools were unavailable during research. Versions, availability, and best practices should be verified against:

- Microsoft Docs: https://learn.microsoft.com/en-us/windows/win32/shell/preview-handlers
- BASS Library: https://www.un4seen.com/
- NuGet Package Manager: https://www.nuget.org/packages/[package-name]
- TagLibSharp GitHub: https://github.com/mono/taglib-sharp
- WriteableBitmapEx GitHub: https://github.com/teichgraf/WriteableBitmapEx

**Recommended Verification Steps:**
1. Check NuGet for latest versions of all packages
2. Verify BASS.NET version and .NET 8 compatibility on un4seen.com
3. Confirm CsWin32 supports preview handler interfaces
4. Test COM registration on Windows 11 24H2+

---
*Stack research for: Windows Explorer Audio Preview Pane Handler*
*Researched: 2026-02-16*
*Confidence: MEDIUM (training data only, requires verification)*
