---
status: resolved
phase: 07-configuration-polish
source: 07-01-SUMMARY.md, 07-02-SUMMARY.md, 07-03-SUMMARY.md, 07-04-SUMMARY.md
started: 2026-02-17T23:30:00Z
updated: 2026-02-18T01:00:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Autoplay Checkbox Toggle
expected: In the control bar, you should see an "Auto" checkbox on the far left. Clicking it toggles the checkmark on/off. When checked, selecting a new audio file should automatically start playback after ~500ms. When unchecked, selecting a file should NOT auto-play.
result: pass

### 2. Loop Checkbox Toggle
expected: Next to the Auto checkbox, you should see a "Loop" checkbox. Clicking it toggles on/off. When checked, playback should automatically restart from the beginning when the track ends. When unchecked, playback stops at the end.
result: pass

### 3. Config Persistence
expected: Toggle Autoplay and Loop checkboxes, then close and reopen Explorer (or switch to a different file and back). The checkbox states should persist — whatever you set should still be set after reopening.
result: pass

### 4. Settings Gear Icon
expected: In the top-right corner of the preview pane, you should see a gear icon (⚙). Hovering highlights it. Clicking opens a settings overlay panel.
result: pass

### 5. WASAPI Device Selection
expected: In the settings overlay, there should be an "Output Device" section showing a dropdown of available audio output devices. You should be able to select a different device. A note should indicate the change takes effect on the next file.
result: issue
reported: "the ux works but it doesn't actually switch the audio device"
severity: major

### 6. Waveform Height Presets
expected: In the settings overlay, there should be a "Waveform" section with Small/Medium/Large height options. Selecting a different preset should change the waveform height. Medium is the default (120px scaled).
result: issue
reported: "the waveform switches but the radio button doesn't change selected state when you pick an option"
severity: minor

### 7. Analysis Toggle and Cache Clear
expected: In the settings overlay, there should be an "Analysis" section with a toggle for BPM/key detection and a "Clear cache" button. Toggling analysis off should skip BPM/key detection on new files. Clear cache should delete cached analysis results.
result: issue
reported: "i toggled the BPM / key detection and it turned it off. turning it on again shows the display but i don't think detection is running. also when it is off if there is a detection file it should still show the key and bpm"
severity: major

### 8. Settings Overlay Dismiss
expected: The settings overlay should close when you: click the X button, click outside the overlay, press Escape, or select a different file in Explorer.
result: pass

### 9. Keyboard Shortcut - Play/Pause
expected: Click the preview pane to give it focus, then press Ctrl+Space. Playback should toggle between play and pause. (You must click the preview pane first for keyboard shortcuts to work.)
result: pass

### 10. Keyboard Shortcut - Seek
expected: With the preview pane focused, Ctrl+Left should seek backward ~5 seconds and Ctrl+Right should seek forward ~5 seconds.
result: issue
reported: "this works but should be adaptive. short files like samples need an much shorter seek time"
severity: minor

### 11. Keyboard Shortcut - Volume
expected: With the preview pane focused, Ctrl+Up should increase volume and Ctrl+Down should decrease volume. If muted, adjusting volume should unmute first.
result: pass

### 12. Keyboard Shortcut - Mute and Settings
expected: Ctrl+M should toggle mute on/off. Ctrl+, (comma) should toggle the settings overlay. Ctrl+L should toggle loop. Escape should close the settings overlay if open.
result: pass

### 13. Tooltip Hints on Controls
expected: Hovering over control bar buttons (play, stop, volume, etc.) should show tooltip hints that include the keyboard shortcut (e.g., "Play/Pause (Ctrl+Space)"). Tooltips appear after a brief hover delay (~400ms).
result: issue
reported: "there are no tooltips"
severity: major

### 14. Installer Script Exists
expected: The file `installer/Audex.iss` should exist and be a valid Inno Setup script. If you have Inno Setup installed, it should compile without errors. (Skip if you don't have Inno Setup installed.)
result: pass

## Summary

total: 14
passed: 9
issues: 5
pending: 0
skipped: 0

## Gaps

- truth: "Selecting a different WASAPI output device in settings should route audio to that device on next file"
  status: resolved
  reason: "User reported: the ux works but it doesn't actually switch the audio device"
  severity: major
  test: 5
  root_cause: "AudioPlayer.Initialize() hardcodes BassWasapi.Init(-1, ...) and never reads config.WasapiDeviceIndex. No reinit/switch path exists."
  artifacts:
    - path: "src/Audex/Audio/AudioPlayer.cs"
      issue: "Line 84: BassWasapi.Init(-1, ...) hardcoded, no device index parameter"
    - path: "src/Audex/PreviewHandler/AudioPreviewHandler.cs"
      issue: "Lines 104-105: Initialize() called once in constructor, never re-invoked on file switch"
  missing:
    - "AudioPlayer.Initialize() must accept device index parameter or read from config"
    - "AudioPlayer needs SwitchDevice(int) method: stop WASAPI, free, reinit with new device, recreate mixer"
    - "DoPreviewInternal must check config.WasapiDeviceIndex and call SwitchDevice if different from current"
  debug_session: ".planning/debug/wasapi-device-switch.md"

- truth: "Waveform height radio buttons should visually reflect the selected preset"
  status: resolved
  reason: "User reported: the waveform switches but the radio button doesn't change selected state when you pick an option"
  severity: minor
  test: 6
  root_cause: "SettingsOverlayRenderer.Draw() reads from ConfigManager.Load() (disk) while waveform uses in-memory _waveformHeightPreset. Empty catch{} in SetWaveformHeightPreset may silently swallow save failures."
  artifacts:
    - path: "src/Audex/UI/PreviewWindow.cs"
      issue: "OnPaint line 967 passes ConfigManager.Load() to overlay renderer instead of in-memory state"
    - path: "src/Audex/UI/SettingsOverlayRenderer.cs"
      issue: "Draw() reads config.WaveformHeightPreset from disk-loaded config for radio selection"
  missing:
    - "Pass _waveformHeightPreset as separate parameter to SettingsOverlayRenderer.Draw() so radio state matches in-memory value"
    - "Add error logging to empty catch{} blocks in settings handlers"
  debug_session: ""

- truth: "Analysis toggle should re-enable detection when turned back on, and cached results should display even when detection is off"
  status: resolved
  reason: "User reported: i toggled the BPM / key detection and it turned it off. turning it on again shows the display but i don't think detection is running. also when it is off if there is a detection file it should still show the key and bpm"
  severity: major
  test: 7
  root_cause: "Two issues: (1) AnalysisToggle handler only calls Invalidate(), never re-triggers StartBpmKeyAnalysis for current file. (2) StartBpmKeyAnalysis early-returns before cache lookup when detection is off, so cached results are never shown."
  artifacts:
    - path: "src/Audex/UI/PreviewWindow.cs"
      issue: "Lines 374-385: AnalysisToggle handler only persists config and Invalidate(), no re-trigger"
    - path: "src/Audex/UI/PreviewWindow.cs"
      issue: "Lines 779-781: Config check early-returns before cache lookup at line 788"
  missing:
    - "AnalysisToggle handler must call StartBpmKeyAnalysis() when toggled ON for current file"
    - "Move cache lookup above the config toggle check so cached results display regardless of toggle"
    - "Gate only live analysis (background thread) behind EnableBpmKeyDetection, not cache reads"
  debug_session: ".planning/debug/analysis-toggle-issues.md"

- truth: "Keyboard seek should adapt to file duration (shorter seek for short files like samples)"
  status: resolved
  reason: "User reported: this works but should be adaptive. short files like samples need an much shorter seek time"
  severity: minor
  test: 10
  root_cause: "TranslateAccelerator hardcodes SeekRelative(-5.0) and SeekRelative(5.0) as literal constants. SeekRelative already has duration available but only uses it for clamping."
  artifacts:
    - path: "src/Audex/PreviewHandler/AudioPreviewHandler.cs"
      issue: "Lines 622-627: Hardcoded 5.0/-5.0 second seek values"
    - path: "src/Audex/UI/PreviewWindow.cs"
      issue: "Lines 1534-1543: SeekRelative takes raw seconds, has duration but doesn't use it for adaptive calculation"
  missing:
    - "Change SeekRelative to compute adaptive seek: Max(0.5, Min(15, duration * 0.05))"
    - "TranslateAccelerator should pass direction (+1/-1) instead of fixed seconds"
  debug_session: ""

- truth: "Hovering over control bar buttons should show tooltip hints with keyboard shortcut names"
  status: resolved
  reason: "User reported: there are no tooltips"
  severity: major
  test: 13
  root_cause: "WinForms ToolTip creates a native TOOLTIPS_CLASS popup window which cannot display in prevhost.exe — UserControl is reparented via SetParent() with no WinForms Form in ancestry, breaking the tooltip's owner window chain."
  artifacts:
    - path: "src/Audex/UI/PreviewWindow.cs"
      issue: "ToolTip component created correctly but native popup window cannot display in prevhost.exe hosting environment"
    - path: "src/Audex/UI/ControlBarRenderer.cs"
      issue: "GetTooltipText() returns correct strings but they never display"
  missing:
    - "Replace WinForms ToolTip with custom owner-drawn tooltip rendered in OnPaint"
    - "Add _tooltipText/_tooltipPosition fields and delay timer (400ms)"
    - "Update OnMouseMove to set tooltip state instead of calling SetToolTip()"
    - "Remove ToolTip component entirely"
  debug_session: ".planning/debug/tooltip-not-appearing.md"
