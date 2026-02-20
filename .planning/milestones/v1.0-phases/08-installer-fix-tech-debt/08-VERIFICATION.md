---
phase: 08-installer-fix-tech-debt
verified: 2026-02-18T22:00:00Z
status: passed
score: 4/4 must-haves verified
re_verification: false
---

# Phase 8: Installer Fix and Tech Debt Verification Report

**Phase Goal:** Installer includes all runtime dependencies; accumulated tech debt from earlier phases cleaned up
**Verified:** 2026-02-18
**Status:** PASSED
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Installer [Files] section includes Newtonsoft.Json.dll so ConfigManager.Save/Load work on clean install | VERIFIED | Line 53 of `installer/Audex.iss`: `Source: "..\src\Audex\bin\x64\Release\net48\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion` -- placed after TagLibSharp.dll, before native DLL group, exactly as specified |
| 2 | ComRegisterFunction writes ThreadingModel=Apartment matching register.ps1 and installer | VERIFIED | `PreviewHandlerRegistration.cs` line 42: `inprocKey.SetValue("ThreadingModel", "Apartment")`. "Both" absent from file. All three registration paths consistent: PreviewHandlerRegistration.cs (line 42), scripts/register.ps1 (line 63), Audex.iss (line 394) all write "Apartment" |
| 3 | Error banner renders when DoPreviewInternal catch block fires (ShowError after UpdateContent) | VERIFIED | `AudioPreviewHandler.cs` catch block lines 433-438: `StopLoading()` then `UpdateContent(_audioFileInfo)` then `ShowError(ex.Message)`. Correct ordering confirmed -- UpdateContent resets `_showError=false` (PreviewWindow.cs line 197), ShowError sets `_showError=true` (PreviewWindow.cs line 228), rendering picks up `_showError=true` (line 914) |
| 4 | 05-01-SUMMARY.md accurately describes basswma.dll as real 29KB DLL, not 0-byte placeholder | VERIFIED | All primary claims updated: `provides` (line 20), `tech-stack` (line 25), `key-decisions` (line 49) all describe "real 29KB x64 WMA plugin". Remaining "0-byte placeholder" occurrences (lines 49, 83, 105, 111, 149) are exclusively in historical context paired with "subsequently replaced with real 29KB DLL" -- accurate and appropriate |

**Score:** 4/4 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `installer/Audex.iss` | Newtonsoft.Json.dll in [Files] section | VERIFIED | Line 53 contains exact entry; positioned in managed assembly group per plan requirement |
| `src/Audex/PreviewHandler/PreviewHandlerRegistration.cs` | ThreadingModel="Apartment" in ComRegisterFunction | VERIFIED | Line 42 confirmed; "Both" not found anywhere in file |
| `src/Audex/PreviewHandler/AudioPreviewHandler.cs` | UpdateContent before ShowError in catch block | VERIFIED | Lines 433-438 confirmed correct ordering; success path (lines 365-373) unchanged |
| `.planning/phases/05-extended-format-support/05-01-SUMMARY.md` | Accurate basswma.dll documentation (29KB DLL) | VERIFIED | All 7 targeted locations updated; no standalone false claim of 0-byte status remains |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `installer/Audex.iss` | `src/Audex/Config/ConfigManager.cs` | Newtonsoft.Json.dll runtime dependency | WIRED | Audex.iss line 53 packages the DLL; ConfigManager.cs uses `using Newtonsoft.Json` -- runtime link is now complete for clean installs |
| `PreviewHandlerRegistration.cs` | `installer/Audex.iss` | ThreadingModel registry value must match | WIRED | Both write "Apartment": PreviewHandlerRegistration.cs line 42 and Audex.iss line 394. scripts/register.ps1 line 63 also matches -- all three registration paths consistent |
| `AudioPreviewHandler.cs catch block` | `PreviewWindow.UpdateContent then ShowError` | Flag ordering: _showError=false then _showError=true | WIRED | Lines 436-437 call UpdateContent then ShowError. PreviewWindow.cs confirms UpdateContent sets _showError=false (line 197) and ShowError sets _showError=true (line 228). LayoutRenderer uses _showError at line 914. End-to-end flag chain is correct |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| CONF-01 | 08-01-PLAN.md | Settings stored in JSON config file in AppData | SATISFIED | Newtonsoft.Json.dll now packaged by installer; ConfigManager.Save/Load (which use JsonConvert) will work on clean install |
| CONF-02 | 08-01-PLAN.md | User can select audio output device (WASAPI default, optional ASIO) | SATISFIED | Installer fix enables device selection preference to persist; ConfigManager serializes DeviceIndex to JSON config |
| CONF-03 | 08-01-PLAN.md | User can toggle autoplay on/off | SATISFIED | Installer fix enables autoplay preference to survive reboots; ConfigManager serializes AutoPlay boolean to JSON config |
| PLAY-04 | 08-01-PLAN.md | User can toggle autoplay (auto-play on file select) | SATISFIED | Autoplay logic confirmed correctly implemented in prior phases; Newtonsoft.Json.dll packaging (the only blocker) is now resolved |

All four requirements mapped to Phase 8 in REQUIREMENTS.md Traceability table are now satisfied. No orphaned requirements detected.

### Anti-Patterns Found

No anti-patterns found in any modified file:

- `installer/Audex.iss`: No TODO/FIXME/placeholder comments
- `src/Audex/PreviewHandler/PreviewHandlerRegistration.cs`: No TODO/FIXME/placeholder comments
- `src/Audex/PreviewHandler/AudioPreviewHandler.cs`: No TODO/FIXME/placeholder comments; no empty implementations
- `.planning/phases/05-extended-format-support/05-01-SUMMARY.md`: Documentation-only file; updated text is accurate

### Human Verification Required

#### 1. Config persistence on clean install

**Test:** Install Audex on a machine that has never had it before (or a clean Windows VM). Select an audio output device in the settings overlay. Close Explorer. Re-open Explorer, select an audio file, and confirm the previously selected device is still selected.
**Expected:** Device selection persists because ConfigManager.Save writes config.json via Newtonsoft.Json.dll, which is now present in the installation directory.
**Why human:** Requires a real install environment. Grep confirms the DLL is packaged; runtime behavior requires a live test.

#### 2. Error banner renders on parse failure

**Test:** Copy a corrupt or intentionally truncated audio file into a monitored folder. Select it in Windows Explorer. Confirm the preview pane shows an error banner (red bar) rather than loading indefinitely or showing a blank panel.
**Expected:** Error banner renders correctly because UpdateContent runs before ShowError in the catch block, leaving _showError=true when LayoutRenderer paints.
**Why human:** Catch block ordering is verified by code inspection; whether the error path is actually reachable with specific corrupt files and whether the banner pixel-renders correctly requires a live test.

#### 3. regasm direct registration produces correct ThreadingModel

**Test:** On a test machine, run `regasm /codebase Audex.dll` directly (without the installer). Open regedit and check `HKCR\CLSID\{guid}\InprocServer32\ThreadingModel`. Confirm the value is "Apartment".
**Expected:** ComRegisterFunction now writes "Apartment" directly, so regasm-only users get the correct STA threading model without needing the installer to overwrite it.
**Why human:** Registry inspection after regasm requires a live Windows environment with the .NET runtime.

### Commit Verification

Both task commits confirmed in git history:

- `6e33e79` -- `fix(08-01): add Newtonsoft.Json.dll to installer and align ThreadingModel to Apartment`
  - Files: `installer/Audex.iss` (+1 line), `src/Audex/PreviewHandler/PreviewHandlerRegistration.cs` (2 changed)
- `4e8928d` -- `fix(08-01): fix error banner ordering and correct basswma.dll documentation`
  - Files: `.planning/phases/05-extended-format-support/05-01-SUMMARY.md` (14 changed), `src/Audex/PreviewHandler/AudioPreviewHandler.cs` (2 changed)

### Gaps Summary

No gaps. All four must-have truths are verified against the actual codebase. All four requirements (CONF-01, CONF-02, CONF-03, PLAY-04) are satisfied by the installer fix. The three automated checks that cannot be replaced by code inspection (clean install behavior, error banner live rendering, regasm registration) are flagged for human verification as expected -- they do not block phase goal determination.

The phase goal is achieved: installer now includes Newtonsoft.Json.dll (critical runtime dependency), and all three tech debt items (ThreadingModel alignment, error banner ordering, documentation accuracy) are corrected.

---

_Verified: 2026-02-18_
_Verifier: Claude (gsd-verifier)_
