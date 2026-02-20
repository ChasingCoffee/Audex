---
phase: 02-bass-audio-integration
plan: 01
subsystem: audio-engine
tags: [bass, wasapi, taglib, audio-player, config-persistence]
dependency_graph:
  requires: [01-com-shell-extension-foundation]
  provides: [AudioPlayer, TagReader, AudioPlayerState, volume-config-persistence]
  affects: [02-02-ui-wiring]
tech_stack:
  added:
    - ManagedBass 4.0.2 (BASS .NET P/Invoke wrapper)
    - ManagedBass.Wasapi 4.0.1 (WASAPI shared-mode output)
    - ManagedBass.Flac 4.0.2 (FLAC decode support)
    - TagLibSharp 2.3.0 (tag metadata reading)
    - bass.dll / basswasapi.dll / bassflac.dll (un4seen x64 native DLLs)
  patterns:
    - GCHandle pinned byte-array streams for zero-copy BASS decode
    - WasapiProcedure callback with Interlocked end-of-stream flag
    - IFileAbstraction wrapping MemoryStream for TagLib# byte-array reads
    - INI round-trip persistence for Audio section (Volume, IsMuted)
key_files:
  created:
    - src/Audex/Audio/AudioPlayer.cs
    - src/Audex/Audio/AudioPlayerState.cs
    - src/Audex/Audio/TagReader.cs
    - src/Audex/native/x64/bass.dll
    - src/Audex/native/x64/basswasapi.dll
    - src/Audex/native/x64/bassflac.dll
  modified:
    - src/Audex/Audex.csproj (4 new NuGet packages + native DLL content items)
    - src/Audex/FileReader/AudioFileInfo.cs (Title, Artist, Album nullable fields)
    - src/Audex/Config/AppConfig.cs (Volume float, IsMuted bool)
    - src/Audex/Config/ConfigManager.cs ([Audio] section read + Save() method)
decisions:
  - "Bass.Init(0) NoSound device required for WASAPI decode-stream path — not Bass.Init(-1)"
  - "BassWasapi.SetVolume(Session) for volume control — Bass.ChannelSetAttribute does not work with decode streams"
  - "GCHandle must be freed AFTER Bass.StreamFree to avoid pointer-into-freed-memory crash"
  - "WasapiCallback restricted to Bass.ChannelGetData + Interlocked.Exchange only — no UI/logging"
  - "SetDllDirectory called in Initialize() so prevhost.exe (System32) can find bass.dll"
  - "End-of-stream detected via Interlocked flag polled from UI timer, not from audio thread"
metrics:
  duration: "5 minutes"
  completed_date: "2026-02-17"
  tasks_completed: 2
  files_created: 6
  files_modified: 4
---

# Phase 02 Plan 01: BASS/WASAPI Audio Engine and TagReader Summary

**One-liner:** BASS NoSound + WASAPI shared-mode decode engine with pinned byte-array streams, TagLib# IFileAbstraction tag reader, and INI-persisted volume config.

## What Was Built

### AudioPlayer.cs (src/Audex/Audio/AudioPlayer.cs)
Complete BASS/WASAPI audio engine with:
- `Initialize()`: Sets `SetDllDirectory` for prevhost.exe, calls `Bass.Init(0)` (NoSound), `BassWasapi.Init(-1, Shared)`, `BassWasapi.Start()`
- `LoadFile(byte[], string)`: Pins byte array via GCHandle, creates decode stream with `BassFlags.Decode | BassFlags.Float`, reads ChannelInfo and duration
- `Play()` / `Pause()` / `Stop()` / `Seek(double)`: State-machine transport controls
- `SetVolume(float)`: `BassWasapi.SetVolume(WasapiVolumeTypes.Session, value)` — session-level
- `SetMute(bool)`: `BassWasapi.SetMute(WasapiVolumeTypes.Session, muted)`
- `WasapiCallback(IntPtr, int, IntPtr)`: Audio-thread-safe; only calls `Bass.ChannelGetData` and `Interlocked.Exchange`
- `CheckEndOfStream()`: UI-timer method; atomically checks/resets end-of-stream flag, fires `PlaybackEnded` event
- `StopAndFreeStream()`: Frees `Bass.StreamFree` before `GCHandle.Free` (correct ordering)
- `Shutdown()`: `BassWasapi.Stop()` → `BassWasapi.Free()` → `Bass.Free()`

### AudioPlayerState.cs (src/Audex/Audio/AudioPlayerState.cs)
State enum: `Idle`, `Loading`, `Playing`, `Paused`, `Stopped`, `Error`

### TagReader.cs (src/Audex/Audio/TagReader.cs)
- `ReadTags(byte[], string)`: Creates `ByteArrayFileAbstraction`, calls `TagLib.File.Create`, extracts Title/Artist/Album
- `ByteArrayFileAbstraction`: Implements `TagLib.File.IFileAbstraction` with `MemoryStream(data, writable: false)`
- `TagInfo` record: `string? Title`, `string? Artist`, `string? Album`
- Catches `UnsupportedFormatException` and logs all other exceptions — always returns non-null `TagInfo`

### Extended Existing Files
- **AudioFileInfo**: Added `Title`, `Artist`, `Album` nullable string properties
- **AppConfig**: Added `Volume` (float, default 0.5f), `IsMuted` (bool, default false)
- **ConfigManager**: Added `[Audio]` section parsing in `Load()`, added `Save(AppConfig)` method with INI round-trip

### Native DLLs
Downloaded from un4seen.com, placed in `src/Audex/native/x64/`:
- `bass.dll` (166,872 bytes)
- `basswasapi.dll` (24,264 bytes)
- `bassflac.dll` (41,992 bytes)

## Verification Results

```
dotnet build src/Audex/Audex.csproj -c Debug
Build succeeded. 0 Warning(s). 0 Error(s).
```

All native DLLs present in output directory after build.

## Deviations from Plan

None — plan executed exactly as written.

The native DLLs were successfully downloaded automatically from un4seen.com (no manual step required). The plan noted that if download fails, create placeholder files and add a human-action checkpoint; download succeeded on first attempt via `https://www.un4seen.com/files/bass24.zip`, `basswasapi24.zip`, `bassflac24.zip`.

## Commits

| Hash     | Description |
|----------|-------------|
| 5716b80  | feat(02-01): add NuGet packages, native DLLs, and AudioPlayerState enum |
| fe7f9bf  | feat(02-01): implement AudioPlayer (BASS/WASAPI engine) and TagReader |

## Self-Check: PASSED

All created files verified present on disk. Both task commits (5716b80, fe7f9bf) confirmed in git log.
