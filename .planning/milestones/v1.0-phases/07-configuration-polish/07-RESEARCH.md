# Phase 7: Configuration & Polish - Research

**Researched:** 2026-02-17
**Domain:** WinForms GDI+ Settings Overlay, IPreviewHandler Keyboard Integration, Inno Setup COM Installer
**Confidence:** HIGH (core patterns), MEDIUM (keyboard routing edge cases), MEDIUM (Inno Setup custom pages)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Settings UI**
- Gear icon in the top-right corner of the preview pane opens settings
- Settings appear as a floating overlay/popup on top of preview content
- Dismiss via X button, click outside overlay, or Escape key
- Settings follow the system dark/light theme (no theme override option)
- Changes take effect immediately (no save/apply button)
- Includes a "Reset to defaults" button
- Open/close with Ctrl+, keyboard shortcut

Settings panel contents:
- Output device selector (WASAPI devices only, no ASIO)
- Waveform options: frequency coloring on/off, height preset (Small/Medium/Large)
- Analysis options: BPM/key auto-analysis on/off, clear analysis cache button
- Check for updates button (checks GitHub releases, notifies user)
- Reset to defaults button

NOT in settings overlay:
- Autoplay toggle (checkbox in control bar instead)
- Loop toggle (checkbox in control bar instead)

**Control Bar Layout**
- Autoplay checkbox on far left of control bar
- Loop checkbox next to autoplay (left side, side by side)
- Layout: [Autoplay] [Loop] | Play/Pause | ... | Volume
- Both checkboxes persist state to config

**Keyboard Shortcuts**
- Ctrl+Space: Play/Pause
- Ctrl+Left/Right: Seek backward/forward (5 second jumps)
- Ctrl+Up/Down: Volume up/down
- Ctrl+L: Toggle loop
- Ctrl+M: Mute/unmute
- Ctrl+,: Open/close settings overlay
- Escape: Close settings overlay (when open)
- Shortcuts work Explorer-wide (not just when preview pane has focus)
- Tooltips on control bar buttons show keyboard shortcut on hover
- Implementation mechanism: Claude's discretion

**Autoplay Behavior**
- Off by default for first-time users
- 500ms delay before playback starts on file selection
- Debounce during rapid file navigation (only last file in sequence plays)
- Autoplay is absolute: when off, never auto-plays
- When on, auto-plays every file selection
- Autoplay checkbox in left side of control bar (not in settings overlay)

**Installer (Inno Setup)**
- Requires admin rights (installs to Program Files)
- Full auto-registration: runs regasm and sets up all COM/file association registry entries
- Bundles everything: DLL, BASS DLLs, bass_fx.dll, all plugins
- Detects .NET Framework 4.8: if missing, shows download link and aborts install
- Warns and prompts before killing prevhost.exe
- File type registration: checkboxes for user to select which audio extensions
- Uninstall asks user: "Remove settings and cache data?" checkbox
- Prompts to restart Explorer after install
- No Start Menu entry
- No license agreement page (license file included in install directory)
- No silent/unattended install support
- Interactive only
- Manual reinstall for updates

### Claude's Discretion
- Debounce timing (same as autoplay delay or shorter)
- Settings overlay exact layout and spacing
- Keyboard shortcut implementation mechanism (TranslateAccelerator vs hook)
- GDI+ rendering for gear icon, checkbox controls
- Inno Setup script structure and organization
- Exact waveform height values for Small/Medium/Large presets

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| CONF-01 | Settings stored in JSON config file in AppData | Existing code uses INI; migration to JSON or hybrid strategy documented below |
| CONF-02 | User can select audio output device (WASAPI default, optional ASIO) | BassWasapi.DeviceCount + GetDeviceInfo enumeration pattern documented |
| CONF-03 | User can toggle autoplay on/off | AppConfig extension + control bar checkbox rendering pattern documented |
| CONF-04 | Settings UI accessible from the preview pane | GDI+ overlay on UserControl pattern documented; no child Form needed |
| PLAY-04 | User can toggle autoplay (auto-play on file select) | Autoplay state management + 500ms debounce timer pattern documented |
| PLAY-05 | User can control playback via keyboard (space=play/pause, arrows=seek) | IPreviewHandler::TranslateAccelerator routing documented; must intercept in AudioPreviewHandler |
</phase_requirements>

---

## Summary

Phase 7 adds three major features to the existing WinForms-based preview handler: a settings overlay UI, keyboard shortcut handling, and an Inno Setup installer. All three build directly on established patterns already used in the codebase.

The settings overlay is best implemented as an owner-drawn panel rendered directly in PreviewWindow's OnPaint — consistent with the existing GDI+ approach for the control bar and waveform. Using a separate WinForms Form or Panel child control is not recommended in the prevhost.exe COM context because child windows can produce z-order and painting artifacts when reparented under Explorer's HWND.

Keyboard shortcuts must go through `IPreviewHandler::TranslateAccelerator`, which is called by prevhost.exe's message pump for every keystroke. The handler must intercept Ctrl+modifier keys before passing unknowns to `IPreviewHandlerFrame::TranslateAccelerator`. This is the only mechanism available for Explorer-wide shortcuts — keyboard hooks (SetWindowsHookEx) would require a separate process and are inappropriate for a low-integrity COM server.

The config format presents a migration issue: CONF-01 requires JSON, but the existing code uses INI (ini-parser-netstandard). The recommended approach is to migrate to System.Text.Json (available in .NET Framework 4.8 via NuGet) with a migration path that reads the old INI file once and converts it. This avoids breaking existing user configurations.

**Primary recommendation:** Implement the settings overlay as GDI+ owner-drawn in PreviewWindow. Handle keyboard shortcuts in AudioPreviewHandler.TranslateAccelerator. Migrate config to JSON using System.Text.Json. Build the Inno Setup script using the [Code] section for prevhost.exe termination and .NET detection.

---

## Standard Stack

### Core (already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Windows.Forms | .NET 4.8 | UserControl + Timer | Already used; owner-draw pattern established |
| System.Drawing (GDI+) | .NET 4.8 | Overlay painting | All existing UI uses this; no third-party UI needed |
| ManagedBass.Wasapi | 4.0.1 | WASAPI device enumeration | Already in project; BassWasapi.GetDeviceInfo works |
| ini-parser-netstandard | 2.5.2 | Current config read/write | Already used; migration shim needed |

### New Additions
| Library | Version | Purpose | Why |
|---------|---------|---------|-----|
| System.Text.Json | via NuGet or built-in | JSON config serialization | CONF-01 requires JSON; available for .NET Framework 4.8 via NuGet package `System.Text.Json` |
| Inno Setup | 6.x | Installer builder | Industry standard free installer; supports Pascal scripting for COM registration |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| System.Text.Json | Newtonsoft.Json | Newtonsoft is more mature and .NET 4.8 native, but adds a large dependency; System.Text.Json is now standard |
| Owner-drawn overlay in PreviewWindow | Separate Panel or Form child | Separate windows are problematic in the reparented COM host context; owner-drawn is consistent with existing approach |
| IPreviewHandler::TranslateAccelerator | Low-level keyboard hook | Hooks require separate elevated process; TranslateAccelerator is the Shell-sanctioned mechanism |

**Installation:**
```bash
# If using System.Text.Json NuGet package for .NET 4.8:
dotnet add package System.Text.Json
# Alternatively use Newtonsoft.Json which is natively compatible:
dotnet add package Newtonsoft.Json
```

Note: System.Text.Json is built into .NET 5+ but requires NuGet for .NET Framework 4.8. Newtonsoft.Json may be simpler given the existing .NET 4.8 target.

---

## Architecture Patterns

### Recommended Project Structure Changes
```
src/Audex/
├── Config/
│   ├── AppConfig.cs              # extend with new settings fields
│   ├── ConfigManager.cs          # migrate from INI to JSON
│   └── ConfigMigration.cs        # one-time INI -> JSON migration (NEW)
├── UI/
│   ├── PreviewWindow.cs          # add overlay state, gear icon, checkbox rendering
│   ├── SettingsOverlayRenderer.cs # NEW: owner-drawn settings panel
│   ├── ControlBarRenderer.cs     # extend: add Autoplay/Loop checkbox zones
│   └── ThemeHelper.cs            # extend: add overlay colors
├── PreviewHandler/
│   └── AudioPreviewHandler.cs    # extend TranslateAccelerator for keyboard shortcuts
installer/
└── Audex.iss           # NEW: Inno Setup script
```

### Pattern 1: Settings Overlay as Owner-Drawn Panel in PreviewWindow

**What:** The overlay is rendered as a semi-transparent rectangle in PreviewWindow.OnPaint when `_settingsOpen` is true. It is NOT a separate WinForms Control or Form.

**When to use:** Always — this is the only safe approach in the COM-hosted reparented window context.

**Why this works:** The existing codebase already uses this exact pattern for the waveform area, analysis progress indicator, and error banner. Owner-draw is consistent and avoids z-order issues with SetParent-reparented windows.

```csharp
// Source: existing PreviewWindow.cs OnPaint pattern + extension
private bool _settingsOpen;
private Rectangle _settingsOverlayBounds;
private Rectangle _gearIconRect;

protected override void OnPaint(PaintEventArgs e)
{
    // ... existing painting ...
    DrawGearIcon(e.Graphics, _gearIconRect);
    if (_settingsOpen)
    {
        DrawSettingsOverlay(e.Graphics, _settingsOverlayBounds);
    }
}

// Hit testing follows the same HitZone enum pattern already used in ControlBarRenderer
// Add: HitZone.GearIcon, HitZone.SettingsOverlay, HitZone.SettingsClose, etc.
```

### Pattern 2: Keyboard Shortcut Routing via TranslateAccelerator

**What:** AudioPreviewHandler.TranslateAccelerator intercepts Ctrl+modifier keys, dispatches actions to PreviewWindow/AudioPlayer via InvokeOnUI, and returns S_OK for handled keys. Unrecognized keys are forwarded to IPreviewHandlerFrame.

**When to use:** All keyboard shortcuts in the preview handler context.

**Key insight:** `IPreviewHandler::TranslateAccelerator` is called by prevhost.exe's message pump for EVERY keystroke when the preview pane has focus, and also when Explorer routes keystrokes to the handler. This is the correct, Shell-sanctioned mechanism.

```csharp
// Source: Microsoft Docs - IPreviewHandler::TranslateAccelerator
// Source: existing AudioPreviewHandler.cs TranslateAccelerator stub
public uint TranslateAccelerator(ref MSG pmsg)
{
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_SYSKEYDOWN = 0x0104;

    if (pmsg.message == WM_KEYDOWN || pmsg.message == WM_SYSKEYDOWN)
    {
        bool ctrl = (NativeMethods.GetKeyState(Keys.ControlKey) & 0x8000) != 0;
        var key = (Keys)(int)pmsg.wParam;

        if (ctrl)
        {
            switch (key)
            {
                case Keys.Space:
                    InvokeOnUI(() => _previewWindow.TogglePlayPause());
                    return S_OK;
                case Keys.Left:
                    InvokeOnUI(() => _previewWindow.Seek(-5.0));
                    return S_OK;
                // ... etc.
                case Keys.Escape when _previewWindow.IsSettingsOpen:
                    InvokeOnUI(() => _previewWindow.CloseSettings());
                    return S_OK;
            }
        }

        if (key == Keys.Escape && _previewWindow.IsSettingsOpen)
        {
            InvokeOnUI(() => _previewWindow.CloseSettings());
            return S_OK;
        }
    }

    // Not handled — forward to host frame
    if (_frame != null)
        return _frame.TranslateAccelerator(ref pmsg);
    return S_FALSE;
}
```

### Pattern 3: WASAPI Device Enumeration

**What:** Enumerate output devices using BassWasapi.DeviceCount and GetDeviceInfo, filter to non-input/non-loopback devices.

**When to use:** Populating the settings overlay device selector.

```csharp
// Source: ManagedBass official documentation https://managedbass.github.io/api/ManagedBass.Wasapi.BassWasapi.html
var outputDevices = new List<(int index, string name)>();
for (int i = 0; i < BassWasapi.DeviceCount; i++)
{
    var info = BassWasapi.GetDeviceInfo(i);
    if (!info.IsInput && !info.IsLoopback)
    {
        outputDevices.Add((i, info.Name));
    }
}
// Index -1 = default WASAPI device (already used in AudioPlayer.Initialize)
// Selected device index persisted to config
```

### Pattern 4: Config Migration from INI to JSON

**What:** On first run with new version, detect old config.ini, parse it with existing ConfigManager.Load(), save as config.json, delete config.ini.

**Why:** CONF-01 requires JSON. The existing INI approach must be migrated gracefully.

```csharp
// Source: pattern based on existing ConfigManager.cs
public static class ConfigMigration
{
    public static void MigrateIfNeeded()
    {
        string iniPath = PathHelper.GetConfigPath(); // still returns old path temporarily
        string jsonPath = PathHelper.GetJsonConfigPath(); // new method needed

        if (File.Exists(iniPath) && !File.Exists(jsonPath))
        {
            var config = ConfigManager.LoadFromIni(iniPath);
            ConfigManager.SaveToJson(jsonPath, config);
            // Optionally delete old file, or leave as backup
        }
    }
}
```

### Pattern 5: Inno Setup Script Structure

**What:** Inno Setup .iss script with [Code] section for prevhost.exe termination, .NET detection, and regasm invocation. File type registration checkboxes via [Components] section.

```pascal
; Source: Inno Setup official docs + community patterns
[Setup]
AppName=Audex
PrivilegesRequired=admin
DefaultDirName={autopf}\Audex

[Components]
Name: "ext_wav"; Description: ".wav, .mp3, .flac, .ogg (core formats)"; Types: full custom; Flags: fixed
Name: "ext_aac"; Description: ".aac, .m4a (AAC/M4A)"; Types: full custom
Name: "ext_mod"; Description: ".mod, .xm, .it, .s3m (module formats)"; Types: full custom
; etc.

[Code]
function InitializeSetup(): Boolean;
begin
  // Check .NET 4.8: HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full, Release >= 528040
  if not RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
                            'Release', DotNetRelease) or (DotNetRelease < 528040) then
  begin
    MsgBox('This application requires .NET Framework 4.8. Please download it from Microsoft.', mbError, MB_OK);
    Result := False;
    exit;
  end;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Check if prevhost.exe is running and prompt user to kill it
  if ProcessExists('prevhost.exe') then
  begin
    if MsgBox('Windows Preview Host (prevhost.exe) is running and must be stopped...',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im prevhost.exe', '',
           SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(500);
    end else
      Result := 'Installation cancelled by user';
  end;
end;

[Run]
; Run regasm for COM registration
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe";
  Parameters: """{app}\Audex.dll"" /codebase";
  WorkingDir: "{app}"; StatusMsg: "Registering COM component..."; Flags: runhidden

[UninstallRun]
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe";
  Parameters: """{app}\Audex.dll"" /unregister";
  WorkingDir: "{app}"; Flags: runhidden
```

### Anti-Patterns to Avoid

- **Separate WinForms Form for settings overlay:** Creates a second top-level window in the COM host context. This can cause z-order problems with Explorer's preview pane HWND hierarchy and painting artifacts. Use owner-drawn rendering in PreviewWindow instead.
- **SetWindowsHookEx for keyboard shortcuts:** Keyboard hooks require a separate process for low-integrity contexts; this is incompatible with prevhost.exe's security model. Always use IPreviewHandler::TranslateAccelerator.
- **Calling BASS API from the overlay painting thread:** BassWasapi.DeviceCount enumerates WASAPI devices via COM; call this only when opening settings, not in OnPaint.
- **Background update checks:** The user explicitly decided manual-only. Do not add timers or startup checks for updates.
- **Registering under UserChoice/AppX ProgIds:** Already documented as causing Explorer freezes (existing register.ps1 avoids this; installer must do the same).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON serialization | Custom text serializer | System.Text.Json or Newtonsoft.Json | Edge cases in escaping, number formatting, Unicode |
| WASAPI device list | Raw MMDevice COM calls | BassWasapi.GetDeviceInfo | Already integrated; handles device state changes |
| .NET version detection | Manual registry parsing | Standard Inno Setup registry query pattern | Registry path well-known: `SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full`, Release >= 528040 |
| Process termination before install | Custom tool | Inno Setup [Code] + taskkill | Established pattern; handles elevation correctly |
| GitHub API version parsing | Custom HTTP client | System.Net.WebClient + JsonDocument | Simple version comparison; no library needed |

**Key insight:** All of the "hard" problems in this phase have established patterns. The overlay UI, keyboard routing, device enumeration, and installer each have well-understood solutions in the existing codebase or in the Shell extension ecosystem.

---

## Common Pitfalls

### Pitfall 1: Keyboard Shortcuts Not Firing When File List Has Focus
**What goes wrong:** Ctrl+Space does nothing. User clicks in file list, then presses Ctrl+Space expecting playback to toggle.
**Why it happens:** When the file list has keyboard focus, Explorer handles keystrokes itself and does NOT call IPreviewHandler::TranslateAccelerator. The "Explorer-wide" behavior in the decisions is aspirational — TranslateAccelerator only fires when focus is within the preview pane or its host. There is no Shell-sanctioned mechanism to intercept global keystrokes from prevhost.exe.
**How to avoid:** Document this limitation in tooltips (e.g., "Click preview pane first, then use keyboard shortcuts"). The Ctrl+modifier pattern was chosen to avoid conflicting with Explorer's own shortcuts (F5 refresh, Delete, etc.), but coverage is limited to when the preview pane has focus.
**Warning signs:** Users report shortcuts work sometimes but not always; they work reliably after clicking in the preview pane.

### Pitfall 2: Config JSON Migration Breaks Existing Users
**What goes wrong:** After upgrade, user's saved settings (volume, color mode, etc.) revert to defaults.
**Why it happens:** ConfigManager switches from reading config.ini to config.json, but config.json doesn't exist yet for existing users.
**How to avoid:** Implement ConfigMigration.MigrateIfNeeded() that runs before ConfigManager.Load() on first startup. Read existing INI, write JSON, then proceed. Call this at the start of AudioPreviewHandler constructor.
**Warning signs:** Unit test for migration: write INI, run migration, verify JSON content.

### Pitfall 3: Settings Overlay Not Dismissed on File Switch
**What goes wrong:** User opens settings, selects a different file in Explorer — settings overlay stays open on the new file's preview.
**Why it happens:** DoPreview is called on file switch but overlay state is not reset.
**How to avoid:** In AudioPreviewHandler.DoPreview (or DoPreviewInternal), call `_previewWindow.CloseSettings()` via InvokeOnUI before loading the new file.

### Pitfall 4: WASAPI Device Selection Breaks Playback After Config Reload
**What goes wrong:** User selects a non-default WASAPI device, closes and reopens preview — audio plays on wrong device or fails.
**Why it happens:** AudioPlayer.Initialize() uses the default WASAPI device (device -1 or 0). Switching devices requires reinitializing WASAPI with a specific device index.
**How to avoid:** Store the WASAPI device index in config. On startup, if a non-default device is configured, call `BassWasapi.Free()` then reinitialize with the stored device index. If the stored device is unavailable (index out of range or removed), fall back to default with a warning.
**Warning signs:** Test: configure device, restart Explorer, verify device selection persists.

### Pitfall 5: regasm.exe Path in Installer Fails on Non-Standard Windows
**What goes wrong:** Installer fails with "RegAsm.exe not found" on some machines.
**Why it happens:** The path `{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe` is canonical for x64 .NET 4.x but may vary on ARM64 Windows or non-standard .NET installations.
**How to avoid:** Use the registry to find the .NET install root: `HKLM\SOFTWARE\Microsoft\.NETFramework\InstallRoot`, then construct the regasm path dynamically. This is more robust than hardcoding the path.
**Warning signs:** Installer fails on machines with non-default Windows installation paths.

### Pitfall 6: Inno Setup [Components] Section vs Manual Registry Writes for File Types
**What goes wrong:** Attempting to use [Files] + [Registry] sections for per-extension registration conflicts with the existing Inno Setup component model, making it complex to conditionally register only selected extensions.
**Why it happens:** [Registry] entries don't have built-in conditional skip based on checkbox state without Pascal scripting.
**How to avoid:** Use the [Code] section's CurStepChanged(ssPostInstall) event to run selective registry writes based on which components the user selected. Mirrors what the existing register.ps1 script does, but from Pascal code within the installer.

### Pitfall 7: Settings Overlay Click-Outside Dismiss with Wrong Bounds
**What goes wrong:** Clicking outside the overlay on the waveform area doesn't dismiss it; or clicking inside the overlay dismisses it prematurely.
**Why it happens:** Overlay bounds detection relies on the cached `_settingsOverlayBounds` rectangle. If OnPaint hasn't run with current dimensions before the click, the cached bounds are stale.
**How to avoid:** Update `_settingsOverlayBounds` in both OnPaint AND in the resize handler (OnSizeChanged). In OnMouseDown, check the current bounds before deciding to dismiss.

---

## Code Examples

### WASAPI Device Enumeration
```csharp
// Source: https://managedbass.github.io/api/ManagedBass.Wasapi.BassWasapi.html
private List<(int DeviceIndex, string Name)> GetWasapiOutputDevices()
{
    var devices = new List<(int, string)>();
    devices.Add((-1, "Default Output Device")); // Always include default

    for (int i = 0; i < BassWasapi.DeviceCount; i++)
    {
        var info = BassWasapi.GetDeviceInfo(i);
        if (!info.IsInput && !info.IsLoopback && info.IsEnabled)
        {
            devices.Add((i, info.Name));
        }
    }
    return devices;
}
```

### GitHub Releases API Version Check
```csharp
// Source: https://docs.github.com/en/rest/releases
// Endpoint: GET https://api.github.com/repos/OWNER/REPO/releases/latest
// Returns JSON with "tag_name" field like "v1.2.0"
private async Task<string?> CheckForUpdatesAsync(string currentVersion)
{
    const string apiUrl = "https://api.github.com/repos/OWNER/REPO/releases/latest";
    using var client = new System.Net.Http.HttpClient();
    client.DefaultRequestHeaders.Add("User-Agent", "Audex/" + currentVersion);
    var json = await client.GetStringAsync(apiUrl);
    using var doc = System.Text.Json.JsonDocument.Parse(json);
    return doc.RootElement.GetProperty("tag_name").GetString();
}
// Note: If using .NET 4.8 without HttpClient, use System.Net.WebClient instead
```

### Gear Icon GDI+ Drawing
```csharp
// Source: GDI+ GraphicsPath pattern — standard approach for icon rendering
private static void DrawGearIcon(Graphics g, Rectangle bounds, Color color)
{
    // Simple approach: draw filled circle + 4 rectangles for teeth
    // Or use a Unicode gear glyph rendered via DrawString with Segoe UI Symbol
    float cx = bounds.X + bounds.Width / 2f;
    float cy = bounds.Y + bounds.Height / 2f;
    float r = bounds.Width / 2f - 2;

    using var pen = new Pen(color, 1.5f);
    using var brush = new SolidBrush(color);

    // Option A: Use Segoe UI Symbol glyph (simplest for Windows 10+)
    // Glyph: U+2699 GEAR (⚙) in Segoe UI Symbol
    using var font = new Font("Segoe UI Symbol", bounds.Height * 0.55f, GraphicsUnit.Pixel);
    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    g.DrawString("\u2699", font, brush, bounds, sf);
}
```

### .NET Framework 4.8 Detection in Inno Setup Pascal
```pascal
// Source: https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed
// Release value >= 528040 = .NET Framework 4.8
function IsDotNet48OrLater(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
    'Release', Release) and (Release >= 528040);
end;
```

### Config JSON Schema (AppConfig)
```json
{
  "Volume": 0.5,
  "IsMuted": false,
  "WaveformColorMode": true,
  "WaveformHeightPreset": "Medium",
  "EnableBpmKeyDetection": true,
  "Autoplay": false,
  "AutoplayDelayMs": 500,
  "Loop": false,
  "WasapiDeviceIndex": -1,
  "SupportedExtensions": [".wav", ".mp3", ".flac", ".ogg"]
}
```

New fields vs existing INI config:
- `WaveformHeightPreset`: "Small" | "Medium" | "Large" (new)
- `Autoplay`: bool (new — CONF-03, PLAY-04)
- `AutoplayDelayMs`: int (new, default 500)
- `Loop`: bool (new)
- `WasapiDeviceIndex`: int (new, -1 = default)

---

## Critical Implementation Note: Config Format

**The requirement CONF-01 says "JSON config file" but the existing code uses INI (ini-parser-netstandard).**

Decision for planner: The plan should include a task to migrate ConfigManager from INI to JSON. This is a self-contained refactor:
1. Add System.Text.Json (or Newtonsoft.Json) NuGet reference
2. Write new ConfigManager.LoadFromJson / SaveToJson methods
3. Write ConfigMigration.MigrateIfNeeded() to detect and convert existing config.ini
4. Update PathHelper to add GetJsonConfigPath() method
5. Remove ini-parser-netstandard dependency after migration

The migration must be backward-compatible: users with existing config.ini should have their settings preserved.

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| Separate Form for settings popup | Owner-drawn panel in existing UserControl | Required in COM host context; avoids z-order issues |
| INI config files | JSON with System.Text.Json | Easier to extend; human-readable; required by CONF-01 |
| Manual COM registration scripts | Inno Setup + regasm | Single offline installer; handles upgrade/uninstall cleanly |
| No keyboard shortcuts | IPreviewHandler::TranslateAccelerator | Shell-sanctioned mechanism; available since Vista |

---

## Open Questions

1. **"Explorer-wide" keyboard shortcuts scope**
   - What we know: IPreviewHandler::TranslateAccelerator fires when focus is in the preview pane
   - What's unclear: Whether Ctrl+Space reliably fires when the user has clicked in the file list and then presses the shortcut
   - Recommendation: Implement via TranslateAccelerator; document focus requirement in tooltips; do not pursue keyboard hooks

2. **WASAPI device switching at runtime**
   - What we know: BassWasapi.Init/Free can switch devices; AudioPlayer.Initialize is called once in constructor
   - What's unclear: Whether BASS/WASAPI supports hot-switching devices without reinitializing the entire engine
   - Recommendation: On device change in settings, store index in config; reinitialize BASS only on next preview handler instantiation (prevhost.exe restart). For immediate effect, call AudioPlayer.Shutdown() then AudioPlayer.Initialize() with new device — test for stability.

3. **Waveform height preset pixel values for Small/Medium/Large**
   - What we know: Current waveform area height is computed from control height dynamically
   - What's unclear: Whether "Small/Medium/Large" refers to a fraction of available height or an absolute pixel height
   - Recommendation: Use fractions of available height (e.g., Small=40%, Medium=60%, Large=80%) — responsive to panel resize

4. **Inno Setup version requirement**
   - What we know: Inno Setup 6.x is current; supports CreateInputOptionPage for custom checkboxes
   - What's unclear: Whether the file type checkbox page should use [Components] or custom Pascal-drawn checkboxes
   - Recommendation: Use [Components] with a custom component page; this is the standard Inno Setup pattern for optional feature selection

---

## Sources

### Primary (HIGH confidence)
- Microsoft Docs: [IPreviewHandler::TranslateAccelerator](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ipreviewhandler-translateaccelerator) — keyboard routing mechanism
- Microsoft Docs: [IPreviewHandlerFrame](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ipreviewhandlerframe) — frame interface for forwarding keys
- Microsoft Docs: [Building Preview Handlers](https://learn.microsoft.com/en-us/windows/win32/shell/building-preview-handlers) — complete TranslateAccelerator algorithm
- ManagedBass Docs: [BassWasapi class](https://managedbass.github.io/api/ManagedBass.Wasapi.BassWasapi.html) — DeviceCount + GetDeviceInfo
- Microsoft Docs: [RegAsm.exe](https://learn.microsoft.com/en-us/dotnet/framework/tools/regasm-exe-assembly-registration-tool) — /codebase and /unregister options
- Microsoft Docs: [Determine .NET Framework versions](https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed) — Release key 528040 for 4.8
- Codebase: existing `AudioPreviewHandler.cs`, `PreviewWindow.cs`, `ConfigManager.cs`, `ControlBarRenderer.cs`, `ThemeHelper.cs`, `register.ps1`

### Secondary (MEDIUM confidence)
- Inno Setup Docs: [[Run] section parameters](https://jrsoftware.org/ishelp/topic_runsection.htm) — Filename, Parameters, Flags
- Inno Setup Docs: [Pascal Scripting Event Functions](https://jrsoftware.org/ishelp/topic_scriptevents.htm) — PrepareToInstall pattern
- GitHub REST API: [releases/latest endpoint](https://docs.github.com/en/rest/releases) — tag_name field for version checking
- AdvancedInstaller: [.NET Framework prerequisite detection](https://www.advancedinstaller.com/check-if-net-framework-version-is-installed-with-inno-setup.html) — Inno Setup detection pattern

### Tertiary (LOW confidence — needs validation)
- Community: Process termination pattern in Inno Setup [Code] section using taskkill — widely used but not official docs
- Community: WasapiDeviceInfo.IsEnabled field — referenced in enum pattern but not explicitly verified in official docs

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries already in use; JSON migration is standard .NET
- Architecture (overlay pattern): HIGH — directly follows existing owner-drawn GDI+ pattern in codebase
- Keyboard shortcut routing: HIGH — documented in official Microsoft Shell docs; TranslateAccelerator algorithm is canonical
- Keyboard shortcut scope ("Explorer-wide"): MEDIUM — real-world behavior may differ from specification; TranslateAccelerator scope is focus-dependent
- WASAPI device switching: MEDIUM — enumeration is documented; hot-switch stability needs testing
- Inno Setup patterns: MEDIUM — standard patterns verified; custom file type checkboxes need script testing
- Config migration: HIGH — straightforward INI-to-JSON migration with known data schema

**Research date:** 2026-02-17
**Valid until:** 2026-05-17 (stable domain, 90 days)
