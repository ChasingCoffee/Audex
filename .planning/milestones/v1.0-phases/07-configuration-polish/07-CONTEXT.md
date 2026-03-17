# Phase 7: Configuration & Polish - Context

**Gathered:** 2026-02-17
**Status:** Ready for planning

<domain>
## Phase Boundary

User can configure settings from the preview pane, control playback via keyboard shortcuts, and install/uninstall the preview handler via an Inno Setup installer with full COM registration. This phase wraps the preview handler into a configurable, installable product.

</domain>

<decisions>
## Implementation Decisions

### Settings UI
- Gear icon in the top-right corner of the preview pane opens settings
- Settings appear as a floating overlay/popup on top of preview content
- Dismiss via X button, click outside overlay, or Escape key
- Settings follow the system dark/light theme (no theme override option)
- Changes take effect immediately (no save/apply button)
- Includes a "Reset to defaults" button
- Open/close with Ctrl+, keyboard shortcut

**Settings panel contents:**
- Output device selector (WASAPI devices only, no ASIO)
- Waveform options: frequency coloring on/off, height preset (Small/Medium/Large)
- Analysis options: BPM/key auto-analysis on/off, clear analysis cache button
- Check for updates button (checks GitHub releases, notifies user)
- Reset to defaults button

**NOT in settings overlay (moved to control bar):**
- Autoplay toggle (checkbox in control bar instead)
- Loop toggle (checkbox in control bar instead)

### Control Bar Layout
- Autoplay checkbox on far left of control bar
- Loop checkbox next to autoplay (left side, side by side)
- Layout: [Autoplay] [Loop] | Play/Pause | ... | Volume
- Both checkboxes persist state to config

### Keyboard Shortcuts
- Ctrl+Space: Play/Pause
- Ctrl+Left/Right: Seek backward/forward (5 second jumps)
- Ctrl+Up/Down: Volume up/down
- Ctrl+L: Toggle loop
- Ctrl+M: Mute/unmute
- Ctrl+,: Open/close settings overlay
- Escape: Close settings overlay (when open)
- Shortcuts work Explorer-wide (not just when preview pane has focus)
- Tooltips on control bar buttons show keyboard shortcut on hover

**Implementation mechanism:** Claude's discretion (IPreviewHandlerFrame TranslateAccelerator or alternative — research will determine best approach for Explorer-wide shortcuts)

### Autoplay Behavior
- Off by default for first-time users
- 500ms delay before playback starts on file selection
- Debounce during rapid file navigation (only last file in sequence plays)
- Autoplay is absolute: when off, never auto-plays regardless of previous playback state
- When on, auto-plays every file selection (even if previous track finished)
- Autoplay checkbox in left side of control bar (not in settings overlay)

### Installer (Inno Setup)
- Requires admin rights (installs to Program Files)
- Full auto-registration: runs regasm and sets up all COM/file association registry entries
- Bundles everything: DLL, BASS DLLs, bass_fx.dll, all plugins — single offline package
- Detects .NET Framework 4.8: if missing, shows download link and aborts install
- Warns and prompts before killing prevhost.exe (during install/upgrade)
- File type registration: checkboxes for user to select which audio extensions to register
- Uninstall asks user: "Remove settings and cache data?" checkbox
- Prompts to restart Explorer after install to activate handler immediately
- No Start Menu entry (shell extension, not standalone app)
- No license agreement page (license file included in install directory)
- No silent/unattended install support
- Interactive only
- Manual reinstall for updates (check-for-updates button in settings just notifies)

### Claude's Discretion
- Debounce timing (same as autoplay delay or shorter)
- Settings overlay exact layout and spacing
- Keyboard shortcut implementation mechanism (TranslateAccelerator vs hook)
- GDI+ rendering for gear icon, checkbox controls
- Inno Setup script structure and organization
- Exact waveform height values for Small/Medium/Large presets

</decisions>

<specifics>
## Specific Ideas

- Autoplay and loop checkboxes should be in the control bar, not hidden in settings — they're primary workflow controls
- Gear icon in top-right corner, away from playback controls
- Update check is manual only (button in settings), no background polling — appropriate for a shell extension
- Installer should feel lightweight and fast — no unnecessary pages (no license, no Start Menu)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 07-configuration-polish*
*Context gathered: 2026-02-17*
