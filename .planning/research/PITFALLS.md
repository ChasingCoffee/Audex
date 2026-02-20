# Domain Pitfalls: Windows Audio Preview Handler

**Domain:** Windows Shell Extension (IPreviewHandler COM object for audio files)
**Researched:** 2026-02-16
**Confidence:** MEDIUM-HIGH

> **Note on confidence:** Based on official Microsoft documentation for IPreviewHandler (HIGH confidence) combined with established patterns for BASS, WPF, and COM interop from training data (MEDIUM confidence due to lack of web search verification). All critical pitfalls are derived from official docs or well-documented failure modes.

---

## Critical Pitfalls

These mistakes cause Explorer crashes, system instability, or require major rewrites.

### Pitfall 1: Improper COM Lifetime Management Crashes Explorer

**What goes wrong:**
Shell extension holds COM references incorrectly or fails to release resources during `IPreviewHandler::Unload`, causing Explorer to crash when switching files or closing the preview pane.

**Why it happens:**
Developers treat COM objects like normal C# objects, relying on garbage collection instead of explicit cleanup. Explorer controls the preview handler lifecycle aggressively - it expects immediate resource release on `Unload()`.

**Consequences:**
- Explorer crashes take down all open Explorer windows
- Users lose unsaved work in other applications
- Windows marks the extension as unstable and may disable it
- Extension gets reputation damage, users uninstall

**How to avoid:**
1. Implement `IPreviewHandler::Unload()` to **immediately** release all resources:
   - Stop BASS playback and free streams
   - Dispose WPF controls and clear visual tree
   - Release all COM references
   - Clear cached data
2. Never rely on finalizers or GC for COM cleanup
3. Use explicit `Dispose` pattern for all IDisposable objects
4. Test rapid preview switching (select 50+ files quickly)
5. Use a cleanup timeout watchdog (max 200ms for Unload)

**Warning signs:**
- Explorer becomes unresponsive when switching previews
- Event Viewer shows shell extension crashes
- `Prevhost.exe` consumes increasing memory
- Preview pane becomes blank after several file selections

**Phase to address:**
Phase 1 (Basic IPreviewHandler implementation) - Build cleanup architecture from the start. Testing in Phase 2.

**Recovery cost:** HIGH - Requires architectural changes to resource management if built incorrectly

---

### Pitfall 2: Threading Model Violations (STA/MTA Mismatch)

**What goes wrong:**
Preview handler creates threads or uses async/await incorrectly, violating COM apartment threading requirements. Causes random crashes, deadlocks, or "RPC server unavailable" errors.

**Why it happens:**
- WPF requires STA thread
- BASS callbacks may execute on different threads
- async/await can switch thread context
- COM requires specific threading models (usually STA for shell extensions)
- Developers use `Task.Run()` without understanding marshalling

**Consequences:**
- Intermittent crashes (hard to reproduce)
- Explorer hangs requiring Task Manager kill
- Preview fails to display with cryptic COM errors
- ASIO callbacks crash on wrong thread

**How to avoid:**
1. Mark preview handler class with `[ComVisible(true)]` and verify STA threading
2. All WPF operations MUST occur on the UI thread created in `SetWindow()`
3. BASS callbacks: Use `BASS_SYNC_MIXTIME` to avoid thread issues, or marshal to UI thread
4. For audio decoding: Load waveform data on background thread, render on UI thread
5. Never use `Task.Run()` for UI updates - use `Dispatcher.BeginInvoke()`
6. Test on multiple CPU configs (single-core, multi-core, hyperthreading)

**Warning signs:**
- Works on dev machine, crashes on some user machines
- Crashes only occur under load or with large files
- Event Viewer shows RPC errors or apartment state violations
- Preview works first time, fails on subsequent loads

**Phase to address:**
Phase 1 - Establish threading architecture. Phase 3 (ASIO) requires extra care for callbacks.

**Recovery cost:** HIGH - Threading bugs are hard to diagnose and may require redesign

---

### Pitfall 3: Not Running as Low Integrity Level Process

**What goes wrong:**
Preview handler opts out of low IL process isolation (sets `DisableLowILProcessIsolation=1`) for "easier development", exposing system to security vulnerabilities.

**Why it happens:**
Developers encounter file access issues during testing and take the shortcut of disabling low IL instead of fixing the root cause.

**Consequences:**
- Preview handler can write to protected system locations (security risk)
- Buffer overrun exploits can elevate privileges
- Microsoft may eventually reject handlers that aren't low IL
- Corporate IT departments block installation
- Cannot access files from low IL process (catch-22)

**How to avoid:**
1. **NEVER** set `DisableLowILProcessIsolation=1` in production
2. Initialize with `IInitializeWithStream` (recommended by Microsoft), NOT file path
3. Stream access works correctly in low IL processes
4. For config files: Store in `%LOCALAPPDATA%\Low` not `%LOCALAPPDATA%`
5. Test in low IL environment from day one
6. Read Microsoft docs: streams provide "file integrity and stability benefits"

**Warning signs:**
- Config file reads work in debug, fail in release
- Temp file creation fails with access denied
- Need administrator rights to use preview handler

**Phase to address:**
Phase 1 - Architecture decision (stream-based initialization). Phase 4 (Config) must use correct AppData paths.

**Recovery cost:** MEDIUM-HIGH - Changing from file-based to stream-based initialization requires refactoring

---

### Pitfall 4: BASS License Violation and Distribution

**What goes wrong:**
Ship BASS.DLL with commercial product without purchasing BASS license, violating Un4seen licensing terms. Legal liability and forced removal.

**Why it happens:**
Developers assume BASS is "open source" or "free for .NET" because BASS.NET wrapper is free. BASS.DLL itself requires commercial license for non-freeware distribution.

**Consequences:**
- Legal action from Un4seen
- Forced to pull product from distribution
- Rewrite audio engine with different library (weeks of work)
- Reputation damage

**How to avoid:**
1. Read BASS license: FREE for freeware, PAID for commercial/shareware
2. If distributing for free (true freeware): OK to use BASS free
3. If selling or including ads: Purchase BASS license (~$100 EUR)
4. Document license status in project README
5. Consider alternatives if budget constrained:
   - NAudio (LGPL, free but fewer features)
   - CSCore (MS-PL, free)
   - Windows Media Foundation (built-in, limited format support)

**Warning signs:**
- Planning to sell extension or bundle with paid software
- Adding "donate" button or monetization
- Corporate deployment (not freeware usage)

**Phase to address:**
Phase 0 (Planning) - Decide on licensing model and budget for BASS license if needed

**Recovery cost:** MEDIUM (if caught early) to HIGH (if post-release legal issues)

---

### Pitfall 5: BASS Resource Leaks and Handle Exhaustion

**What goes wrong:**
BASS streams, devices, and plugins are not freed correctly, causing handle leaks. Over time, system runs out of handles or memory, requiring reboot.

**Why it happens:**
- BASS uses unmanaged resources with manual lifecycle
- Forgetting to call `Bass.StreamFree()`, `Bass.Free()`, `Bass.PluginFree()`
- Exception thrown before cleanup code executes
- Assuming C# GC will clean up native handles (it won't in time)

**Consequences:**
- Preview handler stops working after previewing ~100-1000 files
- System audio stops working (BASS holds device handles)
- `Prevhost.exe` memory grows to GB-scale
- System becomes unstable, requires reboot

**How to avoid:**
1. Wrap all BASS handles in `IDisposable` wrappers
2. Use `try/finally` or `using` statements for stream creation
3. Call `Bass.Free()` in `IPreviewHandler::Unload()`, not finalizer
4. Implement defensive cleanup - check if handle valid before freeing
5. Test with 1000+ file preview sequence
6. Monitor handle count in Process Explorer during testing

**Warning signs:**
- Process Explorer shows increasing handle count in Prevhost.exe
- Audio preview works initially, then stops
- Error messages about "too many files open" or "cannot create stream"
- Memory usage climbs but GC doesn't reclaim

**Phase to address:**
Phase 1 (Basic playback) - Build BASS resource wrapper from start. Phase 2 (Waveform) - Critical for decode streams.

**Recovery cost:** MEDIUM - Retrofitting disposable wrappers is tedious but straightforward

---

### Pitfall 6: WPF HwndSource Lifetime Mismatch

**What goes wrong:**
WPF `HwndSource` created in `SetWindow()` is not properly disposed or is disposed too early, causing rendering failures or Explorer crashes.

**Why it happens:**
Confusion about when Explorer destroys parent HWND. Developers either:
- Never dispose HwndSource (leak)
- Dispose in wrong method (`SetRect` instead of `Unload`)
- Fail to null-check before rendering after disposal

**Consequences:**
- InvalidOperationException during rendering
- Explorer crash on window resize
- Preview pane shows empty/black rectangle
- GDI handle leaks

**How to avoid:**
1. Create `HwndSource` in `SetWindow()` only
2. Store as instance field, check for null before use
3. Dispose in `Unload()` with null guard:
   ```csharp
   if (_hwndSource != null)
   {
       _hwndSource.Dispose();
       _hwndSource = null;
   }
   ```
4. Handle `SetWindow(null)` gracefully (indicates cleanup)
5. Never recreate HwndSource in `SetRect()` - reuse existing

**Warning signs:**
- Crashes during window resize
- Process Explorer shows increasing USER object count
- Preview works once, fails on second file
- Exception: "Cannot access disposed object"

**Phase to address:**
Phase 1 - Core WPF hosting architecture

**Recovery cost:** LOW-MEDIUM - Fixing requires careful review of lifecycle

---

### Pitfall 7: Synchronous Large File Processing Blocks Explorer

**What goes wrong:**
`DoPreview()` synchronously decodes entire multi-GB audio file before returning, freezing Explorer for 10+ seconds.

**Why it happens:**
- Decoding full waveform data in `DoPreview()` instead of async/lazy loading
- No progress indication or chunking
- Reading entire file into memory at once
- Misunderstanding that DoPreview should return quickly

**Consequences:**
- "Not Responding" white overlay on Explorer
- Users think Explorer crashed, kill process
- Poor user experience, negative reviews
- Windows may mark extension as "slow" and deprioritize

**How to avoid:**
1. `DoPreview()` should return within 200ms max
2. Decode waveform asynchronously after window displays
3. Show loading indicator during decode
4. For huge files: Decode first 30-60 seconds only, offer "Load Full" button
5. Use BASS_DECODE_PRESCAN for faster seeking, or skip if >100MB
6. Consider waveform caching in `%TEMP%` with file hash

**Warning signs:**
- Explorer freezes when selecting large files
- DoPreview execution time >500ms in profiler
- High CPU spike lasting multiple seconds
- No responsive UI during load

**Phase to address:**
Phase 2 (Waveform) - Build async decode from start. Phase 5 (Performance) - Add caching.

**Recovery cost:** MEDIUM - Retrofitting async can be complex

---

### Pitfall 8: 32-bit vs 64-bit Binary Mismatch

**What goes wrong:**
Build handler as 64-bit but ship 32-bit BASS.DLL (or vice versa), causing DllNotFoundException or BadImageFormatException.

**Why it happens:**
- Platform target set to "Any CPU" instead of explicit x64
- Copy wrong BASS.DLL variant from SDK
- Test on dev machine with different bitness than users
- Forget that shell extensions must match Explorer bitness

**Consequences:**
- Preview handler silently fails to load
- No error message to user, just blank preview
- Works on some machines, not others
- Event Viewer shows image load errors

**How to avoid:**
1. Set platform target to x64 explicitly (not AnyCPU)
2. Use 64-bit bass.dll from BASS SDK x64 folder
3. Installer must detect OS bitness and install correct version
4. On 64-bit Windows with 32-bit Explorer: Would need 32-bit build (rare, legacy)
5. Add build validation: Check BASS.DLL bitness matches output assembly
6. Test on clean VM without dev environment

**Warning signs:**
- "Could not load file or assembly" errors
- DllNotFoundException for bass.dll
- Works in Visual Studio, fails after install
- Event ID 1000/1001 application errors

**Phase to address:**
Phase 1 - Build configuration. Phase 6 (Distribution) - Installer validation.

**Recovery cost:** LOW - Fix build config and include correct DLL

---

### Pitfall 9: Registry Registration Errors Prevent Loading

**What goes wrong:**
COM registration incorrect or incomplete - missing registry keys, wrong GUIDs, improper file associations. Preview handler never loads.

**Why it happens:**
- Manual registry editing with typos
- Copy-paste GUID without generating new one
- Missing AppID for separate process
- Wrong PreviewHandler association for file types
- Not registering for correct bitness (HKLM vs HKLM\Wow6432Node)

**Consequences:**
- Preview handler installed but never invoked
- Users see default/no preview
- Difficult to debug - no error messages
- Appears to work in regsvr32 but doesn't actually load

**How to avoid:**
1. Use WiX or InstallShield for professional installers
2. For dev: Use regasm or register via code in installer
3. Required registry keys (see Microsoft docs):
   - HKCR\CLSID\{GUID} for handler
   - HKCR\{FileExtension}\ShellEx\{PreviewHandler GUID}
   - AppID settings if using separate process
4. Generate unique GUID - never reuse examples
5. Test registration with `regsvr32` and verify keys created
6. Check `HKLM\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers`

**Warning signs:**
- Preview pane shows default handler after install
- Registry keys missing or malformed
- Shell extension doesn't appear in Process Explorer
- Changes to code don't affect preview

**Phase to address:**
Phase 1 - Dev testing registration. Phase 6 (Distribution) - Installer automation.

**Recovery cost:** LOW-MEDIUM - Tedious to fix but well-documented

---

### Pitfall 10: Missing Error Handling Makes Debugging Impossible

**What goes wrong:**
Exceptions in preview handler go uncaught, causing silent failures. No logging, no error indication to user or developer. Handler appears broken with no diagnostic info.

**Why it happens:**
- Assumption that exceptions will be visible in debugger
- Not realizing Explorer swallows handler exceptions
- No logging infrastructure in place
- Try-catch without logging the exception

**Consequences:**
- Cannot diagnose user-reported issues
- "Works on my machine" syndrome
- Users blame files instead of handler
- Waste hours debugging without trace data

**How to avoid:**
1. Wrap all IPreviewHandler methods in try-catch with logging
2. Log to `%LOCALAPPDATA%\Low\Audex\logs\` (low IL accessible)
3. Use structured logging (Serilog or NLog)
4. Log critical operations:
   - Stream initialization
   - BASS device/stream creation
   - WPF HwndSource creation
   - Exceptions with full stack trace
5. Add debug mode with verbose logging (config option)
6. Include log collection in bug reports

**Warning signs:**
- User reports "it doesn't work" with no details
- Cannot reproduce issues locally
- Preview fails silently with no indication
- Need to ask for Windows Event Viewer logs

**Phase to address:**
Phase 1 - Infrastructure (logging framework). All phases - Log critical paths.

**Recovery cost:** LOW (early) to MEDIUM (if retrofitting into large codebase)

---

## Moderate Pitfalls

Issues that cause bugs or poor UX but not system crashes.

### Pitfall 11: Metadata Reading Blocks UI Thread

**What goes wrong:**
Reading BPM/key tags from audio file synchronously on UI thread causes preview to freeze briefly.

**How to avoid:**
Read metadata asynchronously during decode phase, display when available. Use TagLib# async methods if possible.

**Phase to address:** Phase 2 (Metadata display)

---

### Pitfall 12: Waveform Rendering Performance with High DPI

**What goes wrong:**
On 4K displays, waveform bitmap size explodes, causing slow rendering and high memory usage.

**How to avoid:**
1. Query DPI via `VisualTreeHelper.GetDpi()`
2. Limit waveform bitmap to logical pixels, not physical
3. Use WriteableBitmap with appropriate DPI metadata
4. Cap maximum rendered samples (e.g., 1 pixel = 1000 samples)

**Phase to address:** Phase 2 (Waveform rendering), Phase 5 (Performance optimization)

---

### Pitfall 13: ASIO Device Enumeration Crashes Without Drivers

**What goes wrong:**
Calling ASIO device enumeration on systems without ASIO drivers throws exceptions or returns garbage data.

**How to avoid:**
1. Wrap ASIO calls in try-catch
2. Validate ASIO device list before using
3. Fall back to WASAPI if no ASIO devices
4. Check `Bass.LastError` after ASIO operations

**Phase to address:** Phase 3 (ASIO support)

---

### Pitfall 14: Config File JSON Parse Errors

**What goes wrong:**
User manually edits JSON config, introduces syntax error, preview handler fails to load config and crashes.

**How to avoid:**
1. Use robust JSON parsing with error handling
2. Validate config schema on load
3. Fall back to defaults on parse error
4. Log config errors to help users fix
5. Consider config UI instead of manual JSON editing

**Phase to address:** Phase 4 (Configuration)

---

### Pitfall 15: No Testing on Different Audio Formats

**What goes wrong:**
Handler works with WAV/MP3 but crashes on exotic formats (FLAC, APE, DSD) due to missing BASS plugins or unsupported features.

**How to avoid:**
1. Load BASS plugins on startup (bassflac.dll, etc.)
2. Handle `Bass.BASS_ERROR_FILEFORM` gracefully
3. Show "Unsupported Format" message instead of crashing
4. Test with full format matrix (see test plan)

**Phase to address:** Phase 2 (Waveform), Phase 5 (Testing across formats)

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Skip low IL testing | Faster dev iteration | Security issues, deployment blocks | NEVER - test from day 1 |
| Synchronous waveform decode | Simpler code | Explorer freezes, poor UX | Only for MVP prototype testing |
| Hard-coded config | No config file needed | Cannot customize without rebuild | Early Phase 1 only |
| Single format support | Less plugin management | Limited utility | Phase 1 MVP, but expand Phase 2 |
| No logging | No log file management | Impossible to debug user issues | NEVER - add logging immediately |
| Global exception handler only | Minimal try-catch code | Cannot pinpoint failure location | NEVER - granular error handling required |
| Skip ASIO support | Simpler audio path | Miss audiophile user segment | Acceptable for MVP, add Phase 3 |
| Fixed DPI rendering | Works on 1080p | Blurry or huge on 4K | Phase 1 only, fix Phase 2/5 |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| BASS Initialization | Call `Bass.Init()` multiple times | Init once globally, check `Bass.Initialized` first |
| BASS Plugins | Load plugins on every preview | Load once in preview handler constructor/static init |
| WPF Dispatcher | Assume Dispatcher.CurrentDispatcher is correct | Store dispatcher from SetWindow thread, use stored reference |
| COM Registration | Use regasm /codebase | Use GAC or side-by-side, /codebase breaks on path changes |
| File Streams | Open file directly by path | Use IInitializeWithStream, more secure and works with low IL |
| ASIO Exclusive Mode | Leave ASIO device locked | Release device immediately after use, WASAPI for preview playback |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Full file decode | DoPreview takes >5 seconds | Decode first 60s or downsample, show loading UI | Files >100MB (20min FLAC) |
| No waveform caching | Re-decode every preview | Cache waveform data in %TEMP% by file hash | Same file previewed 2+ times |
| High-res waveform bitmap | Memory spike, slow render | Limit to screen resolution, 1-2 samples/pixel | 4K displays, long files |
| BASS_DECODE_PRESCAN on huge files | 10+ second scan time | Skip prescan for >100MB, or do async | Files >100MB |
| Metadata read via full decode | Unnecessary CPU usage | Use TagLib# or BASS tags API, not full decode | Every preview load |
| WPF layout recalculation | SetRect causes lag | Freeze layout during resize, batch updates | Rapid window resizing |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Disabling low IL | Privilege escalation exploits | Always run as low IL, use stream initialization |
| Executing code from file content | Arbitrary code execution | Never eval/compile from audio metadata |
| Writing to user-provided paths | File system corruption | Only write to %LOCALAPPDATA%\Low\AppName |
| Loading external plugins without validation | DLL hijacking | Validate plugin signatures, load from known paths only |
| Trusting file extensions | Malicious files disguised | Validate actual file format, not just extension |

---

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| No loading indicator | Appears frozen on large files | Show spinner/progress during decode |
| Silent failure | Users think file is corrupt | Show error message "Cannot preview [reason]" |
| No format indication | Confusion about why preview fails | Display "Unsupported format" with format name |
| Audio auto-plays | Annoying in batch file browsing | Audio controls with manual play/pause |
| No volume control | Users' ears hurt on loud files | Volume slider, remember last volume |
| No seek bar | Cannot preview specific part | Visual waveform with click-to-seek |

---

## "Looks Done But Isn't" Checklist

- [ ] **COM Registration:** Verified on clean machine, not just dev environment with lingering registry
- [ ] **Resource Cleanup:** Tested rapid preview switching (50+ files), no handle/memory leaks
- [ ] **Error Handling:** All IPreviewHandler methods wrapped in try-catch with logging
- [ ] **Threading:** Verified all WPF operations on correct thread, BASS callbacks marshalled
- [ ] **Low IL:** Tested in actual low IL process, not with DisableLowILProcessIsolation=1
- [ ] **BASS Licensing:** License purchased if commercial, or documented as freeware-only
- [ ] **64-bit Consistency:** Verified BASS.DLL matches assembly bitness
- [ ] **Format Support:** Tested all claimed formats (WAV, MP3, FLAC, etc.), plugins loaded
- [ ] **Performance:** DoPreview returns <200ms on typical files, async for large files
- [ ] **Multiple Instances:** Tested multiple Prevhost.exe instances don't conflict

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Wrong threading model | HIGH | 1. Audit all cross-thread calls 2. Add Dispatcher marshalling 3. Retest thoroughly |
| File-based instead of stream init | MEDIUM-HIGH | 1. Refactor to IInitializeWithStream 2. Test low IL access 3. Update docs |
| BASS resource leaks | MEDIUM | 1. Create disposable wrappers 2. Add using statements 3. Load test |
| Missing error handling | MEDIUM | 1. Add logging framework 2. Wrap all external calls 3. Add debug mode |
| Synchronous large file processing | MEDIUM | 1. Extract decode to async method 2. Add progress UI 3. Add timeout/cancel |
| WPF HwndSource lifecycle | LOW-MEDIUM | 1. Review SetWindow/Unload 2. Add null guards 3. Test rapid switching |
| Wrong BASS.DLL bitness | LOW | 1. Fix project platform 2. Include correct DLL 3. Add build validation |
| Registry errors | LOW | 1. Use installer tool 2. Validate registry keys 3. Test clean install |
| High DPI rendering | LOW | 1. Query DPI 2. Adjust bitmap size 3. Test on 4K monitor |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| COM lifetime management | Phase 1 | Rapid switching test (50+ files), Process Explorer handle count stable |
| Threading model violations | Phase 1 | Multi-core stress test, no RPC errors in Event Viewer |
| Low IL process isolation | Phase 1 | Test with default Prevhost.exe, verify stream initialization works |
| BASS license compliance | Phase 0 (Planning) | License purchased or project marked freeware |
| BASS resource leaks | Phase 1, Phase 2 | 1000-file preview sequence, memory usage stable |
| WPF HwndSource lifecycle | Phase 1 | Window resize test, recreation test, no GDI leaks |
| Synchronous file processing | Phase 2 | DoPreview execution time <200ms for 99% of files |
| 32/64-bit mismatch | Phase 1, Phase 6 | Clean VM install test, no BadImageFormat errors |
| Registry registration | Phase 1, Phase 6 | Clean machine test, preview handler appears |
| Missing error handling | Phase 1 | Inject errors, verify logs created and useful |
| Metadata blocking | Phase 2 | UI remains responsive during metadata load |
| High DPI rendering | Phase 2, Phase 5 | Test on 4K display, waveform crisp and fast |
| ASIO crashes | Phase 3 | Test on machine without ASIO, graceful fallback |
| Config parse errors | Phase 4 | Inject malformed JSON, handler loads with defaults |
| Format support gaps | Phase 2, Phase 5 | Test matrix of all supported formats |

---

## Sources

**HIGH Confidence:**
- Microsoft Learn: Preview Handlers and Shell Preview Host (https://learn.microsoft.com/en-us/windows/win32/shell/preview-handlers)
  - Official documentation on IPreviewHandler architecture, threading, low IL, initialization
  - Explicitly states: "streams provide file integrity and stability benefits"
  - Server model options and debugging guidance

**MEDIUM Confidence (established patterns, not independently verified for 2026):**
- BASS audio library patterns: Based on Un4seen documentation and common C# interop patterns
- WPF HwndSource hosting: Based on Microsoft WPF documentation and established WinForms/WPF interop patterns
- COM lifetime management: Based on Microsoft COM documentation and .NET interop guidelines
- Shell extension stability patterns: Based on historical Windows shell extension development practices

**LOW Confidence (requires verification):**
- Specific BASS.NET API details - should verify with current Un4seen docs
- ASIO-specific failure modes - based on general ASIO patterns
- TagLib# async patterns - API may have changed

**Recommended additional research for specific phases:**
- Phase 3 (ASIO): Verify current BASS ASIO plugin capabilities and failure modes
- Phase 4 (Config): Research current best practices for .NET JSON config validation
- Phase 5 (Performance): Benchmark actual waveform decode performance on target hardware

---

*Pitfalls research for: Audex - Windows Audio Preview Handler*
*Researched: 2026-02-16*
*Primary source: Microsoft official documentation + established COM/BASS/WPF patterns*
