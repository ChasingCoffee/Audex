---
phase: 07-configuration-polish
plan: 04
subsystem: infra
tags: [inno-setup, installer, com-registration, windows-shell]

# Dependency graph
requires:
  - phase: 07-configuration-polish
    provides: register.ps1 with registry logic for SystemFileAssociations, CLSID, and PreviewHandlers
provides:
  - Inno Setup 6.x installer script that packages, installs, registers, and uninstalls the preview handler
affects: []

# Tech tracking
tech-stack:
  added: [Inno Setup 6.x (Pascal scripting for Windows installer)]
  patterns:
    - Installer Pascal code mirrors PowerShell registration logic exactly
    - Component-based file type selection (fixed core + optional plugin groups)
    - RegAsm path resolved dynamically via registry with fallback to well-known path

key-files:
  created:
    - installer/Audex.iss
  modified: []

key-decisions:
  - "Installer always offers to kill prevhost.exe even if we cannot reliably detect it is running (tasklist approach is optimistic)"
  - "ProgID shellex registration skips AppX and UserChoice ProgIds by string-prefix check to prevent Explorer freeze"
  - "Plugin DLLs (bass_aac, basswma, bassopus) conditionally included in [Files] via Components= flag so they are not installed if component not selected"
  - "RegAsmPath() queries HKLM SOFTWARE\Microsoft\.NETFramework\InstallRoot for dynamic resolution, falls back to Framework64 hardcoded path"

patterns-established:
  - "Inno Setup [Code] registration mirrors register.ps1 logic: same CLSID, AppID, IID constants; same registry paths"

requirements-completed:
  - CONF-01
  - CONF-02
  - CONF-03
  - CONF-04
  - PLAY-04
  - PLAY-05

# Metrics
duration: 3min
completed: 2026-02-17
---

# Phase 7 Plan 04: Inno Setup Installer Script Summary

**Complete Inno Setup 6.x installer script: .NET 4.8 detection, prevhost kill, regasm COM registration, file type component checkboxes, SystemFileAssociations + ProgID shellex registry setup, and clean uninstall with optional data removal**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-02-17T22:57:29Z
- **Completed:** 2026-02-17T22:59:52Z
- **Tasks:** 1 of 2 (Task 2 is human-verify checkpoint)
- **Files created:** 1

## Accomplishments

- Created `installer/Audex.iss` (495 lines) -- complete Inno Setup 6.x script
- Mirrors all registry logic from `scripts/register.ps1` in Pascal scripting
- Implements all user-locked decisions: admin-only, no license page, no Start Menu, .NET 4.8 gate, prevhost termination prompt, component-based file type selection, Explorer restart prompt, uninstall settings/cache removal option

## Task Commits

1. **Task 1: Create Inno Setup installer script** - `d4eb7d0` (feat)
2. **Task 2: Verify Inno Setup script correctness** - awaiting human-verify checkpoint

## Files Created/Modified

- `C:/dev/projects/Music/Audex/installer/Audex.iss` -- Complete Inno Setup 6.x installer script with all sections: [Setup], [Files], [Components], [Types], [Code]

## Decisions Made

- Installer uses an optimistic prevhost.exe kill offer: tasklist detection cannot reliably tell us if prevhost is running in all cases, so we always prompt and let the user decide. This is consistent with the plan's intent.
- AppX/UserChoice ProgID check uses `Pos('AppX', ProgId) = 0` and `Pos('UserChoice', ProgId) = 0` -- simple string guard to avoid the Explorer-freeze ProgId classes identified in the project lessons.
- Plugin DLLs are gated on the same Components that gate file type registration, so an unselected component means the DLL is not installed AND the extensions are not registered. This is the correct consistent behavior.
- `RegAsmPath()` function provides both dynamic resolution (from HKLM\.NETFramework\InstallRoot) and a hardcoded fallback. This matches the approach in `register.ps1`.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `installer/Audex.iss` is ready for human review (Task 2 checkpoint)
- If Inno Setup 6.x compiler (`iscc.exe`) is available, compile with: `iscc installer\Audex.iss`
- Verify checklist: [Setup] admin/no-license/no-StartMenu, [Components] file type groups, [Code] .NET detection + prevhost + regasm + registry, CLSID matches ComGuids.cs, no UserChoice/AppX ProgId modification, uninstall cleanup

---
*Phase: 07-configuration-polish*
*Completed: 2026-02-17*
