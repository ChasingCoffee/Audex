---
status: resolved
trigger: "Investigate why tooltips are not appearing when hovering over control bar buttons in the preview pane."
created: 2026-02-17T00:00:00Z
updated: 2026-02-17T00:00:00Z
---

## Current Focus

hypothesis: Multiple code-level issues prevent ToolTip from appearing — primarily the prevhost.exe hosting environment suppresses TOOLTIPS_CLASS popup windows
test: Read tooltip initialization, usage in OnMouseMove, and research prevhost.exe tooltip constraints
expecting: Find specific code gaps and environmental constraints
next_action: Return diagnosis

## Symptoms

expected: Hovering over play, stop, volume, and other control bar buttons shows tooltip text
actual: No tooltip appears at all on any button
errors: None reported
reproduction: Hover over any control bar button in the preview pane
started: Possibly never worked

## Eliminated

## Evidence

- timestamp: 2026-02-17T00:01:00Z
  checked: PreviewWindow constructor (line 148-149)
  found: ToolTip is created with correct timing settings (AutoPopDelay=5000, InitialDelay=400, ReshowDelay=200). Active property defaults to true. Not added as a child component via Controls.Add — but ToolTip is a Component, not a Control, so this is normal.
  implication: ToolTip initialization looks correct on the surface.

- timestamp: 2026-02-17T00:02:00Z
  checked: OnMouseMove tooltip update logic (lines 1236-1257)
  found: |
    The tooltip is only updated when `newHovered != _hoveredZone` (line 1238). This means:
    1. On first hover over a button, _hoveredZone transitions from None to the zone — tooltip IS set.
    2. While staying on the SAME zone, tooltip is NOT re-set (correct, should not be needed).
    3. When leaving all zones (HitZone.None), tooltip is set to empty string (correct clear).
    But there are multiple early-return paths ABOVE the control bar tooltip code that could prevent it from executing:
    - Line 1148: settings overlay return
    - Line 1168: waveform drag return
    - Line 1199: waveform area return (entire waveform area returns before reaching tooltip code)
    - Line 1225: seeking return
    - Line 1232: volume slider drag return
    These are all expected — tooltips should not show during drags/overlay.
  implication: The code logic for setting tooltip text appears correct for the control bar zone.

- timestamp: 2026-02-17T00:03:00Z
  checked: prevhost.exe tooltip window constraints
  found: |
    WinForms ToolTip creates a native TOOLTIPS_CLASS popup window (top-level, WS_POPUP style).
    In the prevhost.exe hosting environment, the PreviewWindow UserControl is reparented into
    Explorer's preview pane via SetParent(). The ToolTip's popup window is owned by the control's
    top-level parent. In a normal WinForms app, that's the Form. In prevhost.exe, there is NO Form —
    the control is reparented under an Explorer HWND. The ToolTip popup may:
    (a) Be created as a child of a non-existent or wrong parent window
    (b) Be clipped or hidden behind the Explorer window
    (c) Never receive WM_NOTIFY messages because the message pump is Explorer's, not WinForms'

    Additionally, prevhost.exe runs in a low-integrity-level (Low IL) process, and UIPI
    (User Interface Privilege Isolation) can block popup windows from appearing above
    higher-integrity windows. The DisableLowILProcessIsolation=1 registry flag helps with
    COM loading but does not necessarily grant permission for popup window creation.
  implication: The fundamental issue is environmental — ToolTip popup windows likely cannot display in the prevhost.exe hosting context.

- timestamp: 2026-02-17T00:04:00Z
  checked: Gear icon tooltip behavior (lines 1253-1257)
  found: |
    The gear icon tooltip has a subtle bug: it is set OUTSIDE the `if (newHovered != _hoveredZone)`
    block. When hovering over the gear icon, overGear is true but the gear is NOT part of the
    ControlBarRenderer.HitTest — it is handled separately above. So newHovered would be HitZone.None,
    and the tooltip could get set to empty string first (line 1249), then immediately overwritten
    with the gear tooltip (line 1256). This is a minor ordering issue but wouldn't prevent display
    if the ToolTip popup worked at all.
  implication: Even if the environmental issue were solved, there is a minor race between clearing and setting the gear tooltip.

- timestamp: 2026-02-17T00:05:00Z
  checked: ToolTip.SetToolTip with empty string behavior
  found: |
    When newHovered is HitZone.None, GetTooltipText returns null, and line 1249 calls
    _tooltip.SetToolTip(this, ""). In WinForms, setting tooltip text to empty string
    effectively hides the tooltip for that control. This is correct behavior.
  implication: The clear-on-leave logic is correct.

## Resolution

root_cause: WinForms ToolTip relies on creating a native TOOLTIPS_CLASS popup window, which cannot reliably display in the prevhost.exe hosting environment because (1) the UserControl has no WinForms Form as a top-level parent, causing the tooltip's owner window chain to be broken, and (2) prevhost.exe's process isolation may block popup window creation above Explorer windows.
fix: Replace the WinForms ToolTip with a custom owner-drawn tooltip rendered directly onto the UserControl surface in OnPaint, similar to how the waveform hover guide/timestamp is already rendered.
verification:
files_changed: []
