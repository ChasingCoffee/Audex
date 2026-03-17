---
status: resolved
trigger: "Selecting a different WASAPI output device in settings overlay doesn't actually switch audio output"
created: 2026-02-17T00:00:00Z
updated: 2026-02-17T00:00:00Z
---

## Current Focus

hypothesis: CONFIRMED - AudioPlayer.Initialize() hardcodes device index -1 and is called only once; config.WasapiDeviceIndex is never read by AudioPlayer
test: Traced full path: dropdown click -> config save -> AudioPlayer init
expecting: Break in the chain where device index is lost
next_action: Return diagnosis

## Symptoms

expected: Selecting a different WASAPI output device in settings should route audio through that device on next file
actual: Audio continues playing through original device regardless of selection
errors: None reported
reproduction: Select different device in settings dropdown, preview a new file
started: Unknown

## Eliminated

## Evidence

- timestamp: 2026-02-17T00:01:00Z
  checked: PreviewWindow.HandleSettingsOverlayClick (SettingsHitZone.DeviceDropdownItem case, line 328-345)
  found: Device selection DOES save to config correctly. cfg.WasapiDeviceIndex = deviceIdx; ConfigManager.Save(cfg);
  implication: Step 1 of the chain (UI -> config) works correctly

- timestamp: 2026-02-17T00:02:00Z
  checked: AudioPlayer.Initialize() line 84
  found: BassWasapi.Init(-1, 0, 0, ...) — hardcoded -1 (default device). Never reads config.WasapiDeviceIndex.
  implication: Step 2 of the chain is BROKEN — AudioPlayer never consumes the saved device index

- timestamp: 2026-02-17T00:03:00Z
  checked: AudioPreviewHandler constructor lines 104-105
  found: _player = new AudioPlayer(); _player.Initialize(); — called ONCE in constructor, never re-initialized on file switch
  implication: Even if Initialize() read from config, it only runs once at COM object creation time. The overlay note says "Takes effect on next file" but there is no mechanism for that.

- timestamp: 2026-02-17T00:04:00Z
  checked: AudioPlayer.LoadFile() lines 183-261
  found: LoadFile only creates a BASS decode stream and adds it to the existing mixer. It does NOT re-initialize WASAPI or check device config.
  implication: On next file, only the decode stream changes — the WASAPI output device stays the same one from Initialize()

- timestamp: 2026-02-17T00:05:00Z
  checked: AudioPreviewHandler.DoPreviewInternal()
  found: No code reads WasapiDeviceIndex from config. No call to reinitialize WASAPI on file switch.
  implication: The config value is written but never read back by the audio pipeline

## Resolution

root_cause: AudioPlayer.Initialize() hardcodes BassWasapi.Init(-1, ...) and never reads config.WasapiDeviceIndex. Initialize() runs only once at COM construction time and is never re-invoked when the user selects a new file. There is no mechanism to switch the WASAPI output device after initial setup.
fix: (not applied — diagnosis only)
verification:
files_changed: []
