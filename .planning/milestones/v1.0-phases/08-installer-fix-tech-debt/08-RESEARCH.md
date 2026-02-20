# Phase 8: Installer Fix & Tech Debt Cleanup - Research

**Researched:** 2026-02-18
**Domain:** Inno Setup installer packaging, COM registration, WinForms error rendering, documentation accuracy
**Confidence:** HIGH

## Summary

Phase 8 closes the four gaps identified in the v1.0 Milestone Audit. Three of the four success criteria are surgical one-location fixes with well-understood root causes confirmed by reading source code. The fourth is a documentation update. No new dependencies are needed, no architectural changes are required.

The most significant finding from source investigation: the v1.0 audit and the Phase 7-01 SUMMARY contain a factual error. `ConfigManager.cs` uses `Newtonsoft.Json` (NuGet package `Newtonsoft.Json 13.0.3`), NOT `System.Text.Json`. The Phase 7-01 SUMMARY claims "System.Text.Json 8.0.5 chosen over Newtonsoft.Json" but the actual codebase uses `Newtonsoft.Json`. This makes the installer fix even simpler — `Newtonsoft.Json.dll` is the correct DLL to add and it is already confirmed present in the Release build output. Several tech debt items listed in the audit (DiagLogPath hardcoded path, StartLoading never called, stale TODO comment at APH line 435) were already resolved in prior phases and are no longer present in the codebase.

**Primary recommendation:** Execute four targeted fixes in a single plan: (1) add one line to Audex.iss, (2) change one string in PreviewHandlerRegistration.cs, (3) swap two lines in the catch block of AudioPreviewHandler.cs, (4) update one paragraph in 05-01-SUMMARY.md.

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| CONF-01 | Settings stored in JSON config file in AppData | Fix 1 (installer) makes Newtonsoft.Json.dll available on clean install so ConfigManager.Save/Load work at runtime |
| CONF-02 | User can select audio output device (WASAPI default, optional ASIO) | Fix 1 (installer) enables device selection to persist across Explorer sessions |
| CONF-03 | User can toggle autoplay on/off | Fix 1 (installer) enables autoplay preference to survive reboots on clean install |
| PLAY-04 | User can toggle autoplay (auto-play on file select) | Fix 1 (installer) is the only blocker; autoplay logic itself is correctly implemented in source |
</phase_requirements>

---

## Standard Stack

### Core (already in project — no additions needed)

| Library | Version | Purpose | Notes |
|---------|---------|---------|-------|
| Newtonsoft.Json | 13.0.3 | JSON config serialization | Already in Audex.csproj; DLL is in Release build output |
| Inno Setup | 6.x | Windows installer | Audex.iss uses IS 6 syntax; no changes to IS version needed |

**Key clarification:** `ConfigManager.cs` imports `using Newtonsoft.Json;` and `using IniParser;`. `PreviewWindow.cs` imports `using Newtonsoft.Json.Linq;` for the GitHub API update check. The build output at `src/Audex/bin/x64/Release/net48/` contains `Newtonsoft.Json.dll` (711,952 bytes, dated 2023-03-07). The installer's `[Files]` section currently lists 13 DLLs but omits `Newtonsoft.Json.dll`.

### No New Dependencies

This phase adds zero new NuGet packages, zero new files, and zero new DLLs. All materials exist.

---

## Architecture Patterns

### Pattern 1: Inno Setup [Files] Section

**What:** Each file to install is a single `Source:` line in the `[Files]` section.
**When to use:** Any DLL in the Release build output that is a runtime dependency.

```pascal
; Correct pattern (matches existing entries in Audex.iss):
Source: "..\src\Audex\bin\x64\Release\net48\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
```

**Placement:** Add immediately after the last managed assembly entry (`TagLibSharp.dll`, currently line 52 in Audex.iss). Before the "Core BASS native DLLs" comment block. This keeps managed dependencies grouped together.

### Pattern 2: COM Registration ThreadingModel

**What:** `[ComRegisterFunction]` in `PreviewHandlerRegistration.cs` writes registry values that persist after `regasm.exe` runs. The `InprocServer32\ThreadingModel` value must be `"Apartment"` for WinForms STA compatibility.

**Current state (confirmed by code reading):**
- `PreviewHandlerRegistration.cs` line 42: `inprocKey.SetValue("ThreadingModel", "Both");`  — WRONG
- `register.ps1` line 63: `Set-ItemProperty $inprocPath -Name "ThreadingModel" -Value "Apartment"` — correct
- `Audex.iss` line 393: `RegWriteStringValue(HKCR, InprocPath, 'ThreadingModel', 'Apartment');` — correct

**The fix:** Change `"Both"` to `"Apartment"` in `PreviewHandlerRegistration.cs`.

**Why it matters:** When the installer runs `regasm /codebase`, `[ComRegisterFunction]` fires first and writes `"Both"`. The installer's post-install code then immediately overwrites it with `"Apartment"`. However, any user running `regasm` directly (without the installer) gets `"Both"`, which can cause STA marshaling failures in `prevhost.exe`. Aligning the code to match what the installer/script already do is correct.

### Pattern 3: Error Banner Ordering Fix

**What:** In `AudioPreviewHandler.DoPreviewInternal()`, the catch block currently calls methods in the wrong order, preventing the error banner from rendering.

**Root cause (confirmed):**
- `UpdateContent(info)` sets `_showError = false` (line 197 of PreviewWindow.cs)
- `ShowError(msg)` sets `_showError = true` (line 228)
- The **catch block** (lines 433-438 of AudioPreviewHandler.cs) calls `ShowError` THEN `UpdateContent` — so `_showError` is reset to `false` immediately after being set to `true`, and the error banner never renders

**The success path** (lines 365-373) correctly calls `UpdateContent` THEN `ShowError` and works properly. The catch block has the two calls transposed.

**The fix:** Swap the two lines in the catch block's `InvokeOnUI` lambda:
```csharp
// BEFORE (wrong order — error never renders):
InvokeOnUI(() =>
{
    _previewWindow.StopLoading();
    _previewWindow.ShowError(ex.Message);       // sets _showError = true
    _previewWindow.UpdateContent(_audioFileInfo); // sets _showError = false  ← DESTROYS error flag
});

// AFTER (correct order — matches success path pattern):
InvokeOnUI(() =>
{
    _previewWindow.StopLoading();
    _previewWindow.UpdateContent(_audioFileInfo); // sets _showError = false, resets state
    _previewWindow.ShowError(ex.Message);          // sets _showError = true  ← sticks
});
```

### Pattern 4: Documentation Correction

**What:** `05-01-SUMMARY.md` contains a historical inaccuracy in its Decisions section and body text, describing `basswma.dll` as a "0-byte placeholder".

**Current state (confirmed):** `src/Audex/native/x64/basswma.dll` is 29,728 bytes (29KB), dated 2026-02-17. It is a real WMA plugin DLL, not a placeholder. The file was obtained and placed at some point after the initial 05-01 plan execution.

**The fix:** Update the decision entry and body text in `05-01-SUMMARY.md` to reflect the actual DLL status. The `key-decisions` YAML entry still says "basswma.dll not available at plan URL (404); created 0-byte placeholder". The body text at the bottom of the file says "basswma.dll is a 0-byte placeholder (download 404)". Both need correction.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Detecting DLL in installer | Custom check script | Add file entry to [Files] section | IS copies files during install automatically |
| Threading model registry write | Any custom registration | Single string change to existing ComRegisterFunction | Code path already correct, just wrong value |

---

## Common Pitfalls

### Pitfall 1: Confusing System.Text.Json with Newtonsoft.Json

**What goes wrong:** The Phase 7-01 SUMMARY claims "System.Text.Json 8.0.5 chosen over Newtonsoft.Json" but the actual code uses Newtonsoft.Json 13.0.3. A planner reading only SUMMARYs (not source) might add `System.Text.Json.dll` to the installer instead.

**Why it happens:** The decision in SUMMARY was recorded at plan time as an intent; the actual implementation used Newtonsoft.Json. The SUMMARY was not corrected after implementation.

**How to avoid:** Read the `.csproj` to see actual NuGet references. Read `ConfigManager.cs` to confirm the `using` statement. The Release build output confirms: `Newtonsoft.Json.dll` (711KB) is present, `System.Text.Json.dll` is NOT present (it's inbox in .NET 5+, not in net48 NuGet).

**Confidence:** HIGH — verified directly from `.csproj` (line 20), `ConfigManager.cs` (line 5), `PreviewWindow.cs` (line 5), and filesystem.

### Pitfall 2: Stale Tech Debt Items in Audit

**What goes wrong:** The v1.0 audit lists 6 tech debt items across 2 phases. Investigation reveals that 3 of those are already fixed:

| Audit Item | Status | Evidence |
|-----------|--------|---------|
| DiagLogPath hardcoded to dev machine | ALREADY FIXED | Logger.cs uses PathHelper.GetLogFilePath() → %LOCALAPPDATA%\Audex\logs\Audex.log |
| StartLoading() never called | ALREADY FIXED | AudioPreviewHandler.cs line 277: InvokeOnUI(() => _previewWindow?.StartLoading()) |
| Stale TODO comment at APH line 435 | ALREADY FIXED | No TODO/FIXME found in AudioPreviewHandler.cs |

**Remaining actual tech debt:**
- ThreadingModel="Both" in ComRegisterFunction — CONFIRMED (line 42 of PreviewHandlerRegistration.cs)
- Error banner ordering in catch block — CONFIRMED (lines 433-438 of AudioPreviewHandler.cs)
- 05-01-SUMMARY.md says basswma.dll is 0-byte — CONFIRMED inaccurate (actual file is 29KB)

**Confidence:** HIGH — verified by direct code reading.

### Pitfall 3: ThreadingModel "Both" vs "Apartment" Scope

**What goes wrong:** Someone might wonder if changing ComRegisterFunction breaks the `register.ps1` / installer flow since the installer ALSO sets ThreadingModel.

**Why it's safe:** The installer and `register.ps1` both set `ThreadingModel=Apartment` AFTER `regasm` runs. If `ComRegisterFunction` sets `"Both"` and the installer overwrites it with `"Apartment"`, the final state is correct. But if a user runs `regasm` directly without the installer, they get `"Both"` permanently. Changing `ComRegisterFunction` to write `"Apartment"` makes all registration paths consistent and never requires the overwrite step.

### Pitfall 4: Error Banner — Correct Path Is NOT Broken

**What goes wrong:** The success path (lines 365-373) already calls `UpdateContent` then `ShowError` in the correct order. Only the catch block (lines 433-438) has them reversed. Do NOT change the success path.

---

## Code Examples

### Fix 1: Audex.iss [Files] Addition

```pascal
; Current last managed DLL entry:
Source: "..\src\Audex\bin\x64\Release\net48\TagLibSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
; ADD THIS LINE:
Source: "..\src\Audex\bin\x64\Release\net48\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
; Core BASS native DLLs
Source: "..\src\Audex\bin\x64\Release\net48\bass.dll"; DestDir: "{app}"; Flags: ignoreversion
```

### Fix 2: PreviewHandlerRegistration.cs ThreadingModel

```csharp
// BEFORE (line 42):
inprocKey.SetValue("ThreadingModel", "Both");

// AFTER:
inprocKey.SetValue("ThreadingModel", "Apartment");
```

### Fix 3: AudioPreviewHandler.cs Error Banner Order (catch block only)

```csharp
// BEFORE — catch block lines 433-438:
InvokeOnUI(() =>
{
    _previewWindow.StopLoading();
    _previewWindow.ShowError(ex.Message);
    _previewWindow.UpdateContent(_audioFileInfo);
});

// AFTER — swap ShowError and UpdateContent:
InvokeOnUI(() =>
{
    _previewWindow.StopLoading();
    _previewWindow.UpdateContent(_audioFileInfo);
    _previewWindow.ShowError(ex.Message);
});
```

### Fix 4: 05-01-SUMMARY.md basswma.dll documentation

In `key-decisions` YAML (frontmatter):
```yaml
# BEFORE:
- "basswma.dll not available at plan URL (404); created 0-byte placeholder per plan fallback policy"

# AFTER:
- "basswma.dll initially created as 0-byte placeholder (plan URL 404); subsequently replaced with real 29KB DLL"
```

In body text:
```markdown
<!-- BEFORE: -->
basswma.dll is a 0-byte placeholder (download 404)

<!-- AFTER: -->
basswma.dll: initially a 0-byte placeholder; subsequently replaced with the real 29KB x64 WMA plugin DLL
```

---

## State of the Art

| Item | Prior Understanding | Actual Current State | Action |
|------|--------------------|--------------------|--------|
| JSON serializer | "System.Text.Json 8.0.5" per 07-01 SUMMARY | Newtonsoft.Json 13.0.3 per .csproj and source code | Add Newtonsoft.Json.dll to installer |
| basswma.dll | "0-byte placeholder" per 05-01 SUMMARY | Real 29KB DLL (29,728 bytes, 2026-02-17) | Update SUMMARY documentation |
| DiagLogPath | "Hardcoded dev path" per audit | Already fixed — uses PathHelper.GetLogFilePath() | No action needed |
| StartLoading() | "Never called" per audit | Already fixed — called in DoPreviewInternal line 277 | No action needed |
| Stale TODO at APH:435 | "Exists" per audit | Already fixed — no TODO/FIXME in file | No action needed |

---

## Open Questions

1. **Should the installer also include `INIFileParser.dll`?**
   - What we know: `INIFileParser.dll` is used by `ConfigManager.cs` for migration from old config.ini. It IS listed in the installer `[Files]` section at line 46: `Source: "..\src\Audex\bin\x64\Release\net48\INIFileParser.dll"`.
   - What's unclear: Nothing — it's already included.
   - Recommendation: No action needed.

2. **Does `MigrateIfNeeded()` in ConfigManager still reference INI migration or should it be removed?**
   - What we know: The migration path (`LoadFromIni`) is still present and functional. It only runs if `config.ini` exists and `config.json` does not — this is a one-time upgrade path for users who had the pre-Phase-7 install.
   - What's unclear: Whether users who installed before Phase 7 will ever encounter this.
   - Recommendation: Leave migration code in place. It harms nothing and protects upgrading users.

3. **Is `System.Text.Json.dll` in the build output at all?**
   - What we know: .NET 4.8 does not include System.Text.Json in the BCL. If a NuGet package referenced it, it would appear in the output. No `System.Text.Json.dll` exists in the Release output (confirmed by filesystem check).
   - Recommendation: No action — confirms Newtonsoft.Json is the correct DLL to add.

---

## Sources

### Primary (HIGH confidence)

- Direct source reading: `C:/dev/projects/Music/Audex/installer/Audex.iss` — [Files] section, ThreadingModel registry writes
- Direct source reading: `C:/dev/projects/Music/Audex/src/Audex/PreviewHandler/PreviewHandlerRegistration.cs` — ComRegisterFunction, line 42
- Direct source reading: `C:/dev/projects/Music/Audex/src/Audex/PreviewHandler/AudioPreviewHandler.cs` — error banner catch block, lines 433-438
- Direct source reading: `C:/dev/projects/Music/Audex/src/Audex/UI/PreviewWindow.cs` — UpdateContent resets `_showError=false` (line 197), ShowError sets it (line 228)
- Direct source reading: `C:/dev/projects/Music/Audex/src/Audex/Config/ConfigManager.cs` — `using Newtonsoft.Json;` (line 5), `JsonConvert` usage (lines 50, 86)
- Direct source reading: `C:/dev/projects/Music/Audex/src/Audex/Audex.csproj` — `<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />`
- Filesystem check: `src/Audex/bin/x64/Release/net48/Newtonsoft.Json.dll` — 711,952 bytes, confirmed present
- Filesystem check: `src/Audex/native/x64/basswma.dll` — 29,728 bytes (29KB), dated 2026-02-17
- Prior planning document: `.planning/v1.0-MILESTONE-AUDIT.md` — gap definitions, tech debt inventory
- Prior planning document: `.planning/phases/05-extended-format-support/05-01-SUMMARY.md` — basswma.dll description to be corrected

---

## Metadata

**Confidence breakdown:**
- Installer fix (Newtonsoft.Json.dll): HIGH — DLL confirmed in build output; missing from .iss confirmed by reading all 13 listed files
- ThreadingModel fix: HIGH — "Both" on line 42 confirmed by reading; "Apartment" in register.ps1 and .iss confirmed; Phase 01-03 decision log confirms requirement
- Error banner fix: HIGH — UpdateContent/ShowError flag interaction confirmed by reading both methods in PreviewWindow.cs; catch block ordering confirmed by reading AudioPreviewHandler.cs lines 433-438
- Documentation fix: HIGH — basswma.dll file size confirmed by filesystem; 05-01-SUMMARY.md text confirmed by reading
- Stale tech debt items (DiagLogPath, StartLoading, TODO): HIGH — all confirmed fixed by reading source

**Research date:** 2026-02-18
**Valid until:** Stable — no external APIs or fast-moving ecosystem involved; valid indefinitely until code changes
