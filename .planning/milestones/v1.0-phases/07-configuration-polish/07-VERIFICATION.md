---
phase: 07-configuration-polish
verified: 2026-02-18T00:00:00Z
status: passed
score: 29/29 must-haves verified
re_verification:
  previous_status: passed
  previous_score: 24/24
  note: "Previous verification was automated-only and predated UAT. UAT discovered 5 issues (plans 05-07 closed them). This re-verification covers all 24 original truths plus 5 new gap-closure truths."
  gaps_closed:
    - "WASAPI device selection routes audio to chosen device on next file preview"
    - "Waveform height radio buttons reflect in-memory preset (not disk state)"
    - "Analysis toggle ON re-triggers detection for current file"
    - "Cached BPM/key results display even when detection toggle is OFF"
    - "Keyboard seek adapts to file duration (0.5s min, 15s max, 5% of duration)"
    - "Hovering control bar buttons shows owner-drawn tooltip with keyboard shortcut names"
  gaps_remaining: []
  regressions: []
gaps: []
human_verification:
  - test: "Autoplay fires after 500ms"
    expected: "Selecting an audio file with Autoplay enabled triggers playback after a ~500ms delay"
    why_human: "Timer behavior cannot be verified statically; requires live Explorer interaction"
  - test: "Settings overlay dark/light theme"
    expected: "Overlay background and text colors match system dark/light theme on each OS mode"
    why_human: "Theme detection depends on DWM API; cannot verify GDI+ color rendering statically"
  - test: "Keyboard shortcuts fire via TranslateAccelerator"
    expected: "Pressing Ctrl+Space plays/pauses when preview pane has focus in Explorer"
    why_human: "Shell keyboard routing requires live prevhost.exe context to verify"
  - test: "Installer compiles to EXE with iscc.exe"
    expected: "iscc.exe Audex.iss completes without errors and produces Audex-Setup.exe"
    why_human: "Requires Inno Setup 6.x compiler; not available in static verification"
  - test: "WASAPI device switch routes audio to correct device"
    expected: "Selecting a non-default WASAPI device in settings then previewing a file plays audio through that device"
    why_human: "WASAPI device enumeration and routing can only be verified in live prevhost.exe context with audio hardware"
  - test: "Owner-drawn tooltip appears after 400ms hover"
    expected: "Hovering over Play button for 400ms shows tooltip text rendered directly on the control surface"
    why_human: "GDI+ tooltip rendering in prevhost.exe can only be verified visually in live Explorer context"
---

# Phase 7: Configuration and Polish Verification Report

**Phase Goal:** User can configure settings, use keyboard shortcuts, and install via Inno Setup installer
**Verified:** 2026-02-18
**Status:** passed
**Re-verification:** Yes — after UAT gap closure (plans 07-05, 07-06, 07-07)

## Re-verification Context

The initial VERIFICATION.md (2026-02-17) passed with 24/24 automated checks but predated user acceptance testing. UAT (07-UAT.md) discovered 5 real-world issues requiring 3 additional gap-closure plans:

| UAT Issue | Severity | Plan | Status |
|-----------|----------|------|--------|
| #5 WASAPI device selection doesn't actually switch audio device | Major | 07-06 | Closed |
| #6 Waveform height radio button shows wrong selected state | Minor | 07-05 | Closed |
| #7 Analysis toggle re-enable doesn't restart detection; cached results hidden when off | Major | 07-05 | Closed |
| #10 Keyboard seek hardcoded at 5s (too long for short samples) | Minor | 07-05 | Closed |
| #13 Tooltips never appear (WinForms ToolTip broken in prevhost.exe) | Major | 07-07 | Closed |

This re-verification checks all 24 original truths plus the 5 gap-closure truths.

---

## Goal Achievement

### Observable Truths — Original 24

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 1 | Settings stored in JSON at %LOCALAPPDATA%\Audex\config.json | VERIFIED | PathHelper.GetJsonConfigPath() returns that path; ConfigManager.Load/Save use it |
| 2 | INI config migrated to JSON on first load with no data loss | VERIFIED | MigrateIfNeeded() in ConfigManager checks ini exists && json not, maps all fields |
| 3 | Autoplay checkbox appears on far left of control bar | VERIFIED | ControlBarRenderer.Draw() places _autoplayCheckboxRect at controlBarBounds.Left+pad |
| 4 | Loop checkbox appears next to autoplay checkbox | VERIFIED | _loopCheckboxRect positioned immediately right of Auto label |
| 5 | When autoplay on, file selection auto-plays after 500ms delay | VERIFIED | AudioPreviewHandler uses _autoplayTimer with config.AutoplayDelayMs (default 500) |
| 6 | Rapid file navigation debounces so only last file plays | VERIFIED | _autoplayTimer canceled at start of DoPreviewInternal; DoPreview debounce also applies |
| 7 | Autoplay and loop preferences persist across sessions | VERIFIED | ToggleAutoplay/ToggleLoop call SavePlaybackConfig() -> ConfigManager.Save() |
| 8 | Gear icon appears in top-right corner | VERIFIED | PreviewWindow._gearIconRect set in OnPaint top-right; U+2699 drawn there |
| 9 | Clicking gear icon opens settings overlay | VERIFIED | OnMouseDown checks _gearIconRect.Contains -> ToggleSettings() -> _settingsOpen=true |
| 10 | Settings overlay shows WASAPI device selector | VERIFIED | SettingsOverlayRenderer.Draw() renders DeviceSelector section with dropdown |
| 11 | Settings overlay shows freq coloring, height preset, BPM, cache, updates, reset | VERIFIED | All 7 controls drawn in SettingsOverlayRenderer with HitZone enum entries |
| 12 | Overlay dismisses via X, click outside, or Escape | VERIFIED | CloseButton HitZone, click outside check in OnMouseDown, OnKeyDown Escape handler |
| 13 | Changes take effect immediately without Save button | VERIFIED | All settings hit zones call ConfigManager.Save() immediately on interaction |
| 14 | Selecting a different file closes overlay | VERIFIED | UpdateContent() calls CloseSettings() at PreviewWindow.cs line 199 |
| 15 | Ctrl+Space toggles play/pause | VERIFIED | TranslateAccelerator: Keys.Space -> InvokeOnUI(() => _previewWindow.TogglePlayPause()) |
| 16 | Ctrl+Left/Right seeks (adaptive) | VERIFIED | Keys.Left/Right -> SeekRelative(-1.0)/SeekRelative(1.0); SeekRelative computes adaptive amount |
| 17 | Ctrl+Up/Down adjusts volume | VERIFIED | Keys.Up/Down -> AdjustVolume(0.05f)/AdjustVolume(-0.05f) |
| 18 | Ctrl+L toggles loop, Ctrl+M toggles mute, Ctrl+, opens settings | VERIFIED | All 3 in TranslateAccelerator switch; Keys.L/M/OemComma mapped |
| 19 | Escape closes settings overlay | VERIFIED | IsSettingsOpen check -> CloseSettings() in both OnKeyDown and ProcessCmdKey |
| 20 | Tooltips on control bar show keyboard shortcut hints | VERIFIED | Owner-drawn tooltip: DrawOwnerTooltip renders in OnPaint; UpdateTooltipForPosition wired from OnMouseMove |
| 21 | Installer compiles to single-file EXE, requires admin | VERIFIED | PrivilegesRequired=admin, Compression=lzma2/ultra64, SolidCompression=yes |
| 22 | Installer detects .NET 4.8 and aborts with download link if missing | VERIFIED | InitializeSetup() checks NDP\v4\Full Release >= 528040 |
| 23 | Installer warns/prompts before killing prevhost.exe and runs regasm | VERIFIED | PrepareToInstall() prompts for prevhost kill; CurStepChanged runs regasm /codebase |
| 24 | Installer sets up all registry entries; uninstall cleans them | VERIFIED | RegisterExtension/UnregisterExtension + CLSID/PreviewHandlers/DisableLowILProcessIsolation |

### Observable Truths — Gap Closure (Plans 05-07)

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 25 | Selecting a different WASAPI output device routes audio to that device on next file preview | VERIFIED | AudioPlayer.SwitchDevice(int) exists; DoPreviewInternal compares config.WasapiDeviceIndex to _player.CurrentDeviceIndex and calls SwitchDevice if changed (AudioPreviewHandler.cs lines 328-341) |
| 26 | Waveform height radio buttons visually reflect the currently selected preset (in-memory, not disk) | VERIFIED | SettingsOverlayRenderer.Draw() accepts waveformHeightPreset parameter; PreviewWindow.OnPaint passes _waveformHeightPreset (line 990); radio comparisons use parameter not disk config |
| 27 | Toggling analysis OFF then ON re-triggers detection for the current file | VERIFIED | AnalysisToggle handler (PreviewWindow.cs line 400): if newEnabled && _currentAudioData != null, calls StartBpmKeyAnalysis(_currentAudioData, ...) |
| 28 | Cached BPM/key results display even when detection toggle is OFF | VERIFIED | StartBpmKeyAnalysis cache lookup at line 805 precedes EnableBpmKeyDetection check at line 815; cache hit returns before reaching the config guard |
| 29 | Keyboard seek adapts to file duration (0.5s for short files, up to 15s for long files) | VERIFIED | SeekRelative(double direction): Math.Max(0.5, Math.Min(15.0, duration * 0.05)) * direction; TranslateAccelerator passes ±1.0 not fixed seconds |

**Score: 29/29 truths verified**

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/Audex/Config/AppConfig.cs` | Autoplay, AutoplayDelayMs, Loop, WasapiDeviceIndex, WaveformHeightPreset fields | VERIFIED | All fields present with correct defaults |
| `src/Audex/Config/ConfigManager.cs` | JSON load/save via JsonSerializer, MigrateIfNeeded | VERIFIED | System.Text.Json, MigrateIfNeeded(), clamped Volume |
| `src/Audex/Utils/PathHelper.cs` | GetJsonConfigPath returning config.json path | VERIFIED | Returns %LOCALAPPDATA%\Audex\config.json |
| `src/Audex/UI/ControlBarRenderer.cs` | AutoplayCheckbox and LoopCheckbox HitZones, GetTooltipText (no "5s" reference) | VERIFIED | HitZone enum has both; SeekBar tooltip reads "Seek (Ctrl+Left/Right -- click preview pane first)" |
| `src/Audex/PreviewHandler/AudioPreviewHandler.cs` | _autoplayTimer, TranslateAccelerator passing ±1.0, SwitchDevice call in DoPreviewInternal | VERIFIED | Timer present; ±1.0 passed; device change detection at lines 328-341 |
| `src/Audex/UI/SettingsOverlayRenderer.cs` | Draw() accepts waveformHeightPreset string parameter; radio buttons use parameter not disk config | VERIFIED | Parameter at line 108; DrawRadioButton calls compare waveformHeightPreset == "Small/Medium/Large" |
| `src/Audex/UI/PreviewWindow.cs` | _tooltipText, _tooltipTimer, _tooltipVisible fields; DrawOwnerTooltip and UpdateTooltipForPosition methods; no WinForms ToolTip references; SeekRelative(double direction) with adaptive formula | VERIFIED | All fields at lines 112-115; DrawOwnerTooltip at line 1001; UpdateTooltipForPosition at line 1321; SeekRelative at line 1641 with formula at line 1646; no "new ToolTip" or "_tooltip." references found |
| `src/Audex/UI/ThemeHelper.cs` | 7 SettingsOverlay* color methods | VERIFIED | SettingsOverlayBackground/Text/SectionHeader/Control/ControlActive/Divider/ButtonHover |
| `src/Audex/Audio/AudioPlayer.cs` | _currentDeviceIndex field, CurrentDeviceIndex property, Initialize(int deviceIndex=-1), SwitchDevice(int) with WASAPI teardown/reinit | VERIFIED | _currentDeviceIndex at line 34; CurrentDeviceIndex property at line 56; Initialize at line 60; SwitchDevice at line 370 |
| `installer/Audex.iss` | Complete Inno Setup script, 150+ lines, contains regasm | VERIFIED | 487 lines; regasm, SystemFileAssociations, DisableLowILProcessIsolation all present |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| ConfigManager.cs | PathHelper.cs | GetJsonConfigPath() | WIRED | ConfigManager.Load/Save both call PathHelper.GetJsonConfigPath() |
| AudioPreviewHandler.cs | AppConfig.cs | config.Autoplay / config.AutoplayDelayMs | WIRED | autoplayCfg.Autoplay and autoplayCfg.AutoplayDelayMs in autoplay timer logic |
| ControlBarRenderer.cs | PreviewWindow.cs | HitZone.AutoplayCheckbox / HitZone.LoopCheckbox | WIRED | PreviewWindow.OnMouseDown handles both zones -> ToggleAutoplay/ToggleLoop |
| SettingsOverlayRenderer.cs | ThemeHelper.cs | ThemeHelper.SettingsOverlay* color methods | WIRED | 6 ThemeHelper calls in Draw() |
| PreviewWindow.cs | SettingsOverlayRenderer.cs | Draw call with _waveformHeightPreset in OnPaint | WIRED | SettingsOverlayRenderer.Draw(..., _waveformHeightPreset) at line 990 |
| SettingsOverlayRenderer.cs | ConfigManager.cs | Config changes saved immediately | WIRED | Multiple ConfigManager.Save(cfg) calls in PreviewWindow settings click handlers |
| AudioPreviewHandler.cs | PreviewWindow.cs | TranslateAccelerator via InvokeOnUI | WIRED | All shortcut cases call InvokeOnUI(() => _previewWindow.Method()) |
| AudioPreviewHandler.cs | AudioPlayer.cs | SwitchDevice on device change in DoPreviewInternal | WIRED | Lines 328-341: configDeviceIndex != _player.CurrentDeviceIndex -> _player.SwitchDevice(configDeviceIndex) |
| AudioPlayer.cs | BassWasapi | BassWasapi.Init(deviceIndex) in SwitchDevice | WIRED | SwitchDevice line 392: BassWasapi.Init(deviceIndex, 0, 0, WasapiInitFlags.Shared, ...) |
| PreviewWindow.OnMouseMove | ControlBarRenderer.GetTooltipText | UpdateTooltipForPosition called at end of OnMouseMove | WIRED | Line 1318: UpdateTooltipForPosition(e.Location, overGear); calls ControlBarRenderer.GetTooltipText(_hoveredZone) |
| PreviewWindow.OnPaint | DrawOwnerTooltip | _tooltipVisible && !_settingsOpen guard | WIRED | Line 994: if (_tooltipVisible && !string.IsNullOrEmpty(_tooltipText) && !_settingsOpen) DrawOwnerTooltip(g, dpiScale) |
| PreviewWindow.StartBpmKeyAnalysis | AnalysisCache.Read | Cache lookup before EnableBpmKeyDetection check | WIRED | Line 805: AnalysisCache.Read(cacheKey) executes before line 815 EnableBpmKeyDetection guard |
| installer/Audex.iss | scripts/register.ps1 logic | Registry entries mirror register.ps1 | WIRED | Same CLSID, AppID, IID, SystemFileAssociations, DisableLowILProcessIsolation pattern |

---

### Requirements Coverage

| Requirement | Source Plan(s) | Description | Status | Evidence |
|-------------|----------------|-------------|--------|---------|
| CONF-01 | 07-01, 07-04, 07-05 | Settings stored in JSON config file in AppData | SATISFIED | ConfigManager writes JSON to %LOCALAPPDATA%\Audex\config.json; SetWaveformHeightPreset now logs errors instead of silently swallowing them |
| CONF-02 | 07-02, 07-04, 07-06 | User can select audio output device (WASAPI) | SATISFIED | SettingsOverlayRenderer DeviceSelector UI + AudioPlayer.SwitchDevice() wired via DoPreviewInternal |
| CONF-03 | 07-01, 07-04 | User can toggle autoplay on/off | SATISFIED | Autoplay checkbox in control bar, persisted via ConfigManager.Save |
| CONF-04 | 07-02, 07-04 | Settings UI accessible from the preview pane | SATISFIED | Gear icon in PreviewWindow top-right opens SettingsOverlayRenderer |
| PLAY-04 | 07-01, 07-04 | User can toggle autoplay (auto-play on file select) | SATISFIED | _autoplayTimer in AudioPreviewHandler with 500ms delay, debounced |
| PLAY-05 | 07-03, 07-04, 07-07 | User can control playback via keyboard | SATISFIED | TranslateAccelerator handles 8 Ctrl+key combos + Escape; tooltips now visible via owner-drawn rendering; adaptive seek |

All 6 phase requirements accounted for and satisfied. No orphaned requirements detected (REQUIREMENTS.md traceability table maps exactly CONF-01 through CONF-04, PLAY-04, PLAY-05 to Phase 7).

---

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `src/Audex/PreviewHandler/AudioPreviewHandler.cs` | 435 | Stale TODO comment: `// TODO: _previewWindow.CloseSettings() — add here when plan 02 settings overlay is implemented` | Info | Behavior IS implemented via PreviewWindow.UpdateContent() -> CloseSettings(). Comment predates plan 02 completion and was never removed. No functional gap. |

---

### Human Verification Required

#### 1. Autoplay 500ms Timer Firing

**Test:** Enable autoplay checkbox, click a different audio file in Explorer, observe preview pane
**Expected:** Playback begins approximately 500ms after the file loads (waveform appears first, then audio starts)
**Why human:** System.Threading.Timer behavior in COM context (prevhost.exe MTA-to-STA marshaling) cannot be verified statically

#### 2. Settings Overlay Theme Rendering

**Test:** Toggle Windows dark/light mode, open settings overlay via gear icon in each mode
**Expected:** Overlay background is dark gray in dark mode, near-white in light mode; text colors invert accordingly
**Why human:** ThemeHelper.IsDarkMode() reads DWM registry at runtime; cannot verify color output statically

#### 3. Keyboard Shortcuts via TranslateAccelerator

**Test:** Click once in the preview pane to give it focus, then press Ctrl+Space
**Expected:** Audio playback toggles (play if stopped, pause if playing)
**Why human:** TranslateAccelerator is only invoked by Explorer's message loop when the preview handler pane has keyboard focus; prevhost.exe must be running

#### 4. Installer Compilation

**Test:** Run `iscc.exe installer\Audex.iss` with Inno Setup 6.x installed
**Expected:** Compiler exits 0, produces `Output\Audex-Setup.exe`
**Why human:** Inno Setup compiler (iscc.exe) is required; static analysis of Pascal script cannot detect syntax errors

#### 5. WASAPI Device Routing

**Test:** Open settings, select a non-default WASAPI output device from the dropdown, then preview a new audio file
**Expected:** Audio plays through the selected device (verifiable by checking active playback device in Windows Sound settings)
**Why human:** WASAPI device enumeration and routing can only be confirmed in live prevhost.exe context with real audio hardware

#### 6. Owner-Drawn Tooltip Visibility

**Test:** Hover over the Play/Pause button in the control bar for 1 second
**Expected:** Tooltip text (e.g., "Play/Pause (Ctrl+Space -- click preview pane first)") appears rendered directly on the preview pane surface below the cursor
**Why human:** GDI+ rendering in prevhost.exe UserControl can only be confirmed visually with Explorer running

---

### Gaps Summary

No gaps found. All 29 observable truths (24 original + 5 gap-closure) are verified by direct inspection of the codebase. All artifacts exist and are substantive. All key links are wired. All 6 requirements are satisfied.

Five UAT issues (plans 07-05, 07-06, 07-07) were fully implemented and verified:

1. **WASAPI device switching** — `AudioPlayer.SwitchDevice(int)` implements full WASAPI teardown/reinit. `DoPreviewInternal` reads `config.WasapiDeviceIndex`, compares to `_player.CurrentDeviceIndex`, and calls `SwitchDevice` if changed, then reapplies volume/mute. Constructor now initializes with saved device index.

2. **Waveform height radio state** — `SettingsOverlayRenderer.Draw()` accepts in-memory `waveformHeightPreset` parameter. `PreviewWindow.OnPaint` passes `_waveformHeightPreset` directly, eliminating the disk-read race condition that caused radio buttons to show stale state.

3. **Analysis toggle re-enable** — `AnalysisToggle` handler now calls `StartBpmKeyAnalysis()` when toggled ON with a loaded file. Cache lookup in `StartBpmKeyAnalysis` moved above the `EnableBpmKeyDetection` guard, so cached BPM/key values display regardless of toggle state.

4. **Adaptive keyboard seek** — `SeekRelative(double direction)` computes `Math.Max(0.5, Math.Min(15.0, duration * 0.05)) * direction`. `TranslateAccelerator` passes `±1.0` instead of fixed seconds. Seek tooltip updated to remove "5s" reference.

5. **Owner-drawn tooltips** — WinForms `ToolTip` component fully removed (no references remain). Custom `DrawOwnerTooltip` renders tooltip text via GDI+ directly in `OnPaint`. `UpdateTooltipForPosition` called from `OnMouseMove` with 400ms `_tooltipTimer` delay. Tooltip suppressed when settings overlay is open. Cleared on mouse leave and settings open.

---

_Verified: 2026-02-18_
_Verifier: Claude (gsd-verifier)_
