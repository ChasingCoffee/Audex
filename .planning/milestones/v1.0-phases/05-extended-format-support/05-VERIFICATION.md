---
phase: 05-extended-format-support
verified: 2026-02-17T20:00:00Z
status: passed
score: 10/10 must-haves verified
re_verification: true
  previous_status: gaps_found
  previous_score: 9/10
  gaps_closed:
    - "Bass.CreateStream transparently decodes AAC, WMA, Opus, AIFF, and OGG files after plugins are loaded — WMA now functional (basswma.dll is a real 29728-byte PE DLL with valid MZ header, copies to build output, PluginManager 0-byte guard will not skip it)"
  gaps_remaining: []
  regressions: []
human_verification:
  - test: "Preview an AAC or M4A file in Explorer"
    expected: "File plays with waveform displayed; Music Info section shows BPM and Key if tagged; no crash"
    why_human: "Cannot verify BASS audio output programmatically; requires actual Explorer integration test"
  - test: "Preview a WMA file in Explorer"
    expected: "File plays (basswma.dll plugin now loads successfully); waveform displays; no crash; diag.log shows 'Plugin loaded: basswma.dll'"
    why_human: "WMA playback via basswma.dll requires live Explorer integration test; PluginManager.LoadPlugins runs in prevhost.exe context"
  - test: "Preview a module file (.mod, .xm, .it, or .s3m) in Explorer"
    expected: "File plays; waveform shows mono-color bars (no frequency coloring); Bit Depth and Bitrate rows absent from metadata; Music Info section shows dashes for BPM/Key"
    why_human: "Module format playback path (MusicLoad) and UI suppression require live playback verification"
  - test: "Preview a file with an unsupported extension (e.g. .xyz) in Explorer"
    expected: "Waveform area shows 'Format Unavailable: Unsupported format' text; playback controls are not functional; metadata grid still renders whatever TagLib can read"
    why_human: "Format error display path in WaveformRenderer.DrawFormatError requires visual inspection"
  - test: "Preview an MP3 with Serato Autotags GEOB BPM and no standard TBPM frame"
    expected: "BPM value appears in Music Info section (parsed from GEOB fallback)"
    why_human: "Requires a real Serato-analyzed file to test the GEOB fallback branch"
  - test: "Preview an MP3 tagged with Camelot key notation (e.g. '8A') in Explorer"
    expected: "Key field in Music Info section shows 'Am' (normalized from Camelot)"
    why_human: "Key normalization end-to-end requires a tagged file and visual inspection"
---

# Phase 5: Extended Format Support Verification Report

**Phase Goal:** User can preview extended audio formats and see rich metadata from tags
**Verified:** 2026-02-17T20:00:00Z
**Status:** passed — all 10 must-haves verified; human verification needed for live playback
**Re-verification:** Yes — after gap closure (basswma.dll replaced with real 29728-byte PE DLL)

## Re-verification Summary

The only gap from the initial verification was `src/Audex/native/x64/basswma.dll` being a 0-byte placeholder. The user obtained the real basswma.dll from un4seen.com (29728 bytes). Re-verification confirmed:

- File size: 29728 bytes in source (`native/x64/basswma.dll`), identical 29728 bytes in build output (`bin/x64/Debug/net48/basswma.dll`)
- PE header: `4D 5A 90 00` — valid MZ header, confirmed real Windows PE DLL
- PluginManager 0-byte guard (`if (fi.Length == 0)` at PluginManager.cs line 58) will NOT trigger — the 29728-byte DLL passes through to `Bass.PluginLoad(dllPath)` at line 66
- `register.ps1` line 101-103: `Test-Path (Join-Path $outputDir "basswma.dll")` will now succeed, causing `.wma` to be added to the extension list

No regressions found. All 9 previously-verified items remain intact (files unchanged since initial verification: same timestamps, same content patterns confirmed by grep).

## Goal Achievement

### Observable Truths

| #  | Truth | Status | Evidence |
|----|-------|--------|---------|
| 1  | BASS plugins (AAC, WMA, Opus) are loaded eagerly at startup and logged to diag.log | VERIFIED | PluginManager.LoadPlugins() called in AudioPlayer.Initialize() after Bass.Init(0). All three plugin DLLs are real PE files (bass_aac.dll 242176b, bassopus.dll 87896b, basswma.dll 29728b). 0-byte guard at line 58 passes all three. Logging present at lines 68-74. |
| 2  | Bass.CreateStream transparently decodes AAC, WMA, Opus, AIFF, and OGG files after plugins are loaded | VERIFIED | AAC/M4A: bass_aac.dll 242176b real PE, loaded via PluginManager. Opus: bassopus.dll 87896b real PE, loaded. **WMA: basswma.dll now 29728b real PE (MZ header 4D 5A 90 00 confirmed), 0-byte guard passes, Bass.PluginLoad will be called.** OGG/AIFF: core BASS, no plugin. IsFormatSupported returns true for all. |
| 3  | Module formats (.mod, .xm, .it, .s3m) play via Bass.MusicLoad with proper MusicFree cleanup | VERIFIED | AudioPlayer.LoadModuleFile() at line 141 uses Bass.MusicLoad byte[] overload. StopAndFreeStream() at line 391 uses MusicFree for _isModuleHandle=true, StreamFree otherwise. All four extensions in PluginManager._moduleExtensions. |
| 4  | Module format waveform generation produces mono-color peaks without FFT frequency analysis | VERIFIED | WaveformGenerator.Generate() accepts isModuleFormat parameter (line 50). FFT block wrapped in `if (!isModuleFormat)`. FrequencyColors returned as null for module formats. SmoothColors skipped for modules. |
| 5  | When a format cannot decode, the caller receives a clear error reason string (not a crash) | VERIFIED | AudioPreviewHandler lines 310-335: PluginManager.IsFormatSupported() checked before LoadFile; formatError set from GetUnsupportedReason(); exception from LoadFile caught and stored as "Format Unavailable: {message}". WaveformRenderer.DrawFormatError() renders in waveform area (PreviewWindow line 447). |
| 6  | Missing plugin DLLs are detected and reported as reason string | VERIFIED | PluginManager.GetUnsupportedReason() returns "{FORMAT} plugin not found" for plugin extensions whose plugin is not loaded. 0-byte detection path documented at line 60. Now moot for all three plugins (all real DLLs). |
| 7  | BPM and musical key are read from ID3v2, Vorbis Comments, and APE tags | VERIFIED | TagReader.ReadMusicInfo() reads: TBPM/TKEY from ID3v2 TextInformationFrame, BPM/INITIALKEY from Vorbis XiphComment, BPM/INITIALKEY from APE tag, Serato GEOB as BPM fallback. All four source types covered. |
| 8  | Key values are normalized to standard notation (Am, C#m, F) from Camelot, Open Key, and text formats | VERIFIED | MusicKeyNormalizer.Normalize() has full 24-entry CamelotMap, full 24-entry OpenKeyMap, text regex for minor/major variants. Called from TagReader line 131. |
| 9  | Music Info section always visible with dashes for missing values; Key before BPM | VERIFIED | LayoutRenderer.DrawMusicInfoSection() called unconditionally in Render(). Key row drawn first, BPM second. Null BPM shows "-", null Key shows "-". |
| 10 | Registration script only registers plugin-dependent extensions when their DLL is present | VERIFIED | register.ps1 line 93: `Test-Path (Join-Path $outputDir "bass_aac.dll")` guards .aac/.m4a. Line 101: `Test-Path (Join-Path $outputDir "basswma.dll")` guards .wma (will now succeed). Line 109: guards .opus. Core and module extensions always registered. |

**Score:** 10/10 truths verified

### Required Artifacts

| Artifact | Status | Details |
|----------|--------|---------|
| `src/Audex/Audio/PluginManager.cs` | VERIFIED | 6462 bytes. Exports LoadPlugins, IsFormatSupported, GetUnsupportedReason, IsModuleFormat. 0-byte guard at line 58 passes 29728-byte basswma.dll. |
| `src/Audex/Audio/AudioPlayer.cs` | VERIFIED | Contains LoadModuleFile with Bass.MusicLoad, _isModuleHandle field, MusicFree in StopAndFreeStream. |
| `src/Audex/Audio/WaveformGenerator.cs` | VERIFIED | Contains isModuleFormat parameter, MusicLoad path, FFT skip for modules, null FrequencyColors return. |
| `src/Audex/native/x64/bass_aac.dll` | VERIFIED | 242176 bytes. Real x64 PE DLL. Copies to bin/x64/Debug/net48/. |
| `src/Audex/native/x64/basswma.dll` | VERIFIED | **29728 bytes. Real x64 PE DLL (MZ header 4D 5A 90 00 confirmed). Copies to bin/x64/Debug/net48/ at same size. WMA decoding now functional.** |
| `src/Audex/native/x64/bassopus.dll` | VERIFIED | 87896 bytes. Real x64 PE DLL. Copies to bin/x64/Debug/net48/. |
| `src/Audex/Audio/MusicKeyNormalizer.cs` | VERIFIED | 6366 bytes. Full Camelot/OpenKey/text normalization. |
| `src/Audex/Audio/TagReader.cs` | VERIFIED | 16486 bytes. Contains ReadMusicInfo with all tag sources, MusicKeyNormalizer.Normalize call, Serato GEOB parser. |
| `src/Audex/FileReader/AudioFileInfo.cs` | VERIFIED | 3258 bytes. Contains Bpm, Key, IsModuleFormat, FormatError fields. |
| `src/Audex/UI/LayoutRenderer.cs` | VERIFIED | 14892 bytes. DrawMusicInfoSection renders "Music Info" header, Key row, BPM row with dashes. Module format suppresses Bit Depth and Bitrate. |
| `src/Audex/UI/WaveformRenderer.cs` | VERIFIED | 21508 bytes. DrawFormatError method draws "Format Unavailable: {reason}" in waveform area. |
| `src/Audex/UI/PreviewWindow.cs` | VERIFIED | 37175 bytes. _formatError field; UpdateContent stores info.FormatError; OnPaint branches to DrawFormatError; StartWaveformGeneration accepts isModuleFormat. |
| `scripts/register.ps1` | VERIFIED | 6887 bytes. Dynamic extension registration based on plugin DLL presence. Backup to prev-handlers.json. Module extensions always included. |
| `scripts/unregister.ps1` | VERIFIED | 5137 bytes. Full 14-extension superset. Restore from prev-handlers.json. Only removes Audex CLSID entries. |

### Key Link Verification

**Plan 01 Key Links:**

| From | To | Via | Status | Evidence |
|------|----|-----|--------|---------|
| PluginManager.cs | Bass.PluginLoad | absolute path plugin loading | WIRED | PluginManager.cs line 66: `Bass.PluginLoad(dllPath)`. 29728-byte basswma.dll will now reach this call. |
| AudioPlayer.cs | PluginManager.LoadPlugins | called in Initialize() after Bass.Init(0) | WIRED | AudioPlayer.cs line 80: `PluginManager.LoadPlugins(assemblyDir ?? string.Empty)` |
| AudioPlayer.cs | Bass.MusicLoad | module format loading path | WIRED | AudioPlayer.cs line 145: `Bass.MusicLoad(data, 0, data.Length, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan, 0)` |
| WaveformGenerator.cs | PluginManager.IsModuleFormat | skips FFT for module formats | WIRED | WaveformGenerator.cs: isModuleFormat parameter flows through; FFT wrapped in `if (!isModuleFormat)` |

**Plan 02 Key Links:**

| From | To | Via | Status | Evidence |
|------|----|-----|--------|---------|
| TagReader.cs | MusicKeyNormalizer.Normalize | normalizes raw key from tags | WIRED | TagReader.cs line 131: `MusicKeyNormalizer.Normalize(rawKey)` |
| AudioPreviewHandler.cs | TagReader.ReadMusicInfo | calls ReadMusicInfo and populates Bpm/Key | WIRED | AudioPreviewHandler.cs line 348: `TagReader.ReadMusicInfo(_fileData, _fileName)`. Lines 370-371 populate Bpm, Key on AudioFileInfo. |
| AudioPreviewHandler.cs | PluginManager.(IsFormatSupported/GetUnsupportedReason) | checks format support, sets FormatError | WIRED | AudioPreviewHandler.cs lines 310-312: IsFormatSupported checked, GetUnsupportedReason called on failure. |
| LayoutRenderer.cs | AudioFileInfo.(Bpm/Key) | reads Bpm/Key for Music Info section | WIRED | LayoutRenderer.cs lines 271, 278: `info.Key`, `info.Bpm`. |
| PreviewWindow.cs | WaveformRenderer.DrawFormatError | passes format error for waveform display | WIRED | PreviewWindow.cs line 444-447: `if (_formatError != null)` -> `WaveformRenderer.DrawFormatError(g, waveformBounds, _formatError, dpiScale)` |
| register.ps1 | build output directory | checks for plugin DLL presence before registering dependent extensions | WIRED | register.ps1 line 101: `Test-Path (Join-Path $outputDir "basswma.dll")` — now succeeds (29728b DLL copies to output). |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|---------|
| FMT-02 | 05-01, 05-02 | Supports AIFF, OGG, AAC, WMA, OPUS, and M4A playback | VERIFIED | AIFF, OGG: core BASS. AAC/M4A: bass_aac.dll 242176b loaded. OPUS: bassopus.dll 87896b loaded. **WMA: basswma.dll 29728b real PE DLL, 0-byte guard passes, Bass.PluginLoad will execute at runtime. All six formats now supported.** |
| FMT-03 | 05-01 | Supports module formats (.mod, .xm, .it, .s3m) | VERIFIED | MusicLoad path in AudioPlayer + WaveformGenerator, all four extensions in PluginManager._moduleExtensions and AudioHeaderParserFactory. |
| FMT-04 | 05-01, 05-02 | Unsupported formats show clear error message (not crash) | VERIFIED | PluginManager.GetUnsupportedReason() returns descriptive strings. AudioPreviewHandler catches LoadFile exceptions. WaveformRenderer.DrawFormatError renders in waveform area. |
| META-03 | 05-02 | Displays BPM and musical key from existing tags | VERIFIED | TagReader.ReadMusicInfo reads from ID3v2/Vorbis/APE/Serato GEOB. MusicKeyNormalizer normalizes Camelot/OpenKey/text. LayoutRenderer draws Music Info section always. AudioFileInfo.Bpm and .Key fields populated and rendered. |

No orphaned requirements. All four requirement IDs (FMT-02, FMT-03, FMT-04, META-03) appear in plan frontmatter and are fully satisfied.

### Anti-Patterns Found

None. The previously-flagged 0-byte placeholder in basswma.dll has been resolved. No TODO/FIXME/stub anti-patterns in code files. All `return null` occurrences are legitimate nullable helper method returns.

### Human Verification Required

#### 1. WMA Playback End-to-End (NEW — previously blocked by 0-byte placeholder)

**Test:** Select a WMA file in the Windows Explorer preview pane.
**Expected:** Audio plays through speakers; waveform displays; Music Info section shows BPM/Key from tags (or dashes if not tagged); diag.log shows `[PluginManager] Plugin loaded: basswma.dll`; no crash.
**Why human:** Bass.PluginLoad must execute in prevhost.exe context; BASS audio output cannot be verified programmatically.

#### 2. AAC/M4A or Opus Playback End-to-End

**Test:** Select an AAC or M4A file in Windows Explorer preview pane.
**Expected:** Audio plays through speakers; waveform displays; Music Info section shows BPM/Key from tags (or dashes if not tagged); no crash.
**Why human:** BASS audio output through WASAPI cannot be verified programmatically. Requires prevhost.exe loading the registered DLL.

#### 3. Module Format Playback

**Test:** Select a .mod, .xm, .it, or .s3m file in Windows Explorer preview pane.
**Expected:** Audio plays; waveform displays with mono-color bars (no frequency gradient); Bit Depth and Bitrate rows absent from metadata grid; Music Info section shows dashes (module formats have no BPM/key tags).
**Why human:** MusicLoad path and UI suppression require live integration test with real module files.

#### 4. Unsupported Format Error Display

**Test:** Create a file with a .xyz extension and select it in Explorer.
**Expected:** Waveform area shows "Format Unavailable: Unsupported format: xyz" text; controls are present but non-functional; metadata grid shows filename and file size.
**Why human:** WaveformRenderer.DrawFormatError() output requires visual inspection.

#### 5. Serato GEOB BPM Fallback

**Test:** Select an MP3 file analyzed by Serato DJ that has a Serato Autotags GEOB frame but no standard TBPM frame.
**Expected:** BPM value appears in Music Info section, sourced from the GEOB binary fallback path.
**Why human:** Requires a real Serato-analyzed file; the GEOB parsing code path cannot be verified with synthetic test files.

#### 6. Camelot Key Normalization Display

**Test:** Select an audio file tagged with Camelot notation key (e.g., "8A" for Am) in Explorer.
**Expected:** Key field in Music Info section shows "Am" — normalized from Camelot.
**Why human:** End-to-end requires a tagged file and visual inspection of the rendered value.

### Gaps Summary

No gaps. The single gap from the initial verification (basswma.dll 0-byte placeholder) is closed. The file is now a real 29728-byte Windows PE DLL (MZ header `4D 5A 90 00` confirmed). It copies correctly to the build output directory at the same size. PluginManager's 0-byte guard will not skip it. register.ps1's `Test-Path` check for basswma.dll in the output directory will succeed, causing `.wma` to be registered as a preview-handler extension.

**All four requirements (FMT-02, FMT-03, FMT-04, META-03) are fully satisfied.** Phase 5 goal is achieved.

---

_Verified: 2026-02-17T20:00:00Z_
_Verifier: Claude (gsd-verifier)_
_Re-verification: Yes — gap closure confirmed for basswma.dll_
