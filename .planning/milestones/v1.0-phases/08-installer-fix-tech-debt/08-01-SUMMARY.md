---
phase: 08-installer-fix-tech-debt
plan: 01
subsystem: installer
tags: [inno-setup, com-registration, threading-model, error-handling, documentation]

# Dependency graph
requires:
  - phase: 07-configuration-polish
    provides: ConfigManager with Newtonsoft.Json dependency, installer Audex.iss, PreviewHandlerRegistration.cs
  - phase: 05-extended-format-support
    provides: basswma.dll native plugin, 05-01-SUMMARY.md documentation
provides:
  - Installer packages Newtonsoft.Json.dll for config persistence on clean install
  - ComRegisterFunction ThreadingModel aligned to "Apartment" matching register.ps1 and installer
  - Error banner renders correctly in DoPreviewInternal catch block
  - Accurate basswma.dll documentation in 05-01-SUMMARY.md
affects: [installer, com-registration, error-display]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Error banner ordering pattern: call UpdateContent first (resets _showError=false), then ShowError (sets _showError=true)

key-files:
  created: []
  modified:
    - installer/Audex.iss
    - src/Audex/PreviewHandler/PreviewHandlerRegistration.cs
    - src/Audex/PreviewHandler/AudioPreviewHandler.cs
    - .planning/phases/05-extended-format-support/05-01-SUMMARY.md

key-decisions:
  - "Newtonsoft.Json.dll placed in managed assembly group in [Files] section, after TagLibSharp.dll and before native DLLs"
  - "ThreadingModel changed from Both to Apartment in ComRegisterFunction to match register.ps1 and installer registry entries"
  - "Error banner catch block reordered: UpdateContent then ShowError (UpdateContent resets _showError flag)"

patterns-established:
  - "Installer [Files] grouping: managed assemblies first, then core native DLLs, then optional plugin DLLs"

requirements-completed: [CONF-01, CONF-02, CONF-03, PLAY-04]

# Metrics
duration: 3min
completed: 2026-02-19
---

# Phase 8 Plan 01: Installer Fix and Tech Debt Summary

**Newtonsoft.Json.dll added to installer for config persistence, ThreadingModel aligned to Apartment, error banner ordering fixed, basswma.dll docs corrected**

## Performance

- **Duration:** 3 min
- **Started:** 2026-02-19T00:36:53Z
- **Completed:** 2026-02-19T00:40:08Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added Newtonsoft.Json.dll to installer [Files] section so ConfigManager.Save/Load works on clean install (critical gap closure)
- Aligned ComRegisterFunction ThreadingModel from "Both" to "Apartment" matching register.ps1 and installer -- ensures regasm /codebase users get correct STA threading
- Fixed error banner ordering in DoPreviewInternal catch block (UpdateContent before ShowError) so error flag is not reset
- Updated 05-01-SUMMARY.md to accurately describe basswma.dll as real 29KB x64 WMA plugin DLL across all 7 references

## Task Commits

Each task was committed atomically:

1. **Task 1: Add Newtonsoft.Json.dll to installer and fix ThreadingModel** - `6e33e79` (fix)
2. **Task 2: Fix error banner ordering and update basswma.dll documentation** - `4e8928d` (fix)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `installer/Audex.iss` - Added Newtonsoft.Json.dll to [Files] section (managed assembly group)
- `src/Audex/PreviewHandler/PreviewHandlerRegistration.cs` - Changed ThreadingModel from "Both" to "Apartment" in ComRegisterFunction
- `src/Audex/PreviewHandler/AudioPreviewHandler.cs` - Swapped ShowError/UpdateContent order in catch block lambda
- `.planning/phases/05-extended-format-support/05-01-SUMMARY.md` - Updated 7 basswma.dll references from 0-byte placeholder to real 29KB DLL

## Decisions Made

None - followed plan as specified. All four fixes were surgical single-line or single-phrase changes exactly as described in the plan.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] prevhost.exe holding DLL lock during build**
- **Found during:** Task 1 (build verification)
- **Issue:** prevhost.exe (Preview Handler Surrogate Host) locked Audex.dll in bin/x64/Release, preventing build output copy
- **Fix:** Killed prevhost.exe via `taskkill /f /im prevhost.exe` before rebuild
- **Files modified:** None (runtime process management)
- **Verification:** Build succeeds with 0 errors after process termination
- **Committed in:** N/A (not a code change)

---

**Total deviations:** 1 auto-fixed (Rule 3 blocking -- process lock)
**Impact on plan:** Build environment issue only. No code changes beyond plan. No scope creep.

## Issues Encountered

- prevhost.exe held DLL lock on build output directory, requiring process termination before successful build. This is expected when the preview handler is actively loaded in Windows Explorer.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All four v1.0 milestone audit gaps are now closed
- Installer packages all required managed DLLs including Newtonsoft.Json.dll
- COM registration is consistent across all three paths (installer, register.ps1, regasm)
- Error banner rendering is reliable in all code paths
- Historical documentation accurately reflects current state of basswma.dll

---
*Phase: 08-installer-fix-tech-debt*
*Completed: 2026-02-19*

## Self-Check: PASSED

- FOUND: installer/Audex.iss
- FOUND: src/Audex/PreviewHandler/PreviewHandlerRegistration.cs
- FOUND: src/Audex/PreviewHandler/AudioPreviewHandler.cs
- FOUND: .planning/phases/05-extended-format-support/05-01-SUMMARY.md
- FOUND: .planning/phases/08-installer-fix-tech-debt/08-01-SUMMARY.md
- FOUND commit: 6e33e79 (Task 1)
- FOUND commit: 4e8928d (Task 2)
