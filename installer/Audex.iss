; Audex Inno Setup Installer Script
; Requires Inno Setup 6.x (https://jrsoftware.org/isinfo.php)
;
; CLSID:       {F2A5B8C3-4D7E-4A9B-8C1F-3E6D5A7B9C2E}
; AppID:       {6d2b5079-2f0b-48dd-ab7f-97cec514d30b}
; IPreviewHandler IID: {8895b1c6-b41f-4c1c-a562-0d564250836f}
;
; Registry mirrors scripts/register.ps1:
;   - DisableLowILProcessIsolation=1 on CLSID (required for .NET CLR in prevhost.exe)
;   - ThreadingModel=Apartment on InprocServer32
;   - SystemFileAssociations + ProgID shellex registration
;   - PreviewHandlers list registration
;   - Does NOT touch UserChoice/AppX ProgIds (Explorer freeze prevention)

#define AppName      "Audex"
#define AppVersion   "1.0.0"
#define AppPublisher "Audex"
#define AppCLSID     "{F2A5B8C3-4D7E-4A9B-8C1F-3E6D5A7B9C2E}"
#define AppID        "{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}"
#define PreviewIID   "{8895b1c6-b41f-4c1c-a562-0d564250836f}"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppId={{#AppID}
DefaultDirName={autopf}\{#AppName}
PrivilegesRequired=admin
OutputBaseFilename=Audex-Setup
Compression=lzma2/ultra64
SolidCompression=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Audex.dll
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=
; No LicenseFile -- license agreement page omitted per user decision
; No Start Menu entries -- DisableProgramGroupPage=yes

[Files]
; Main assembly and managed dependencies
Source: "..\src\Audex\bin\x64\Release\net48\Audex.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\Serilog.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\Serilog.Sinks.File.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\INIFileParser.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\ManagedBass.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\ManagedBass.Wasapi.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\ManagedBass.Flac.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\ManagedBass.Mix.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\ManagedBass.Fx.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\TagLibSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
; Core BASS native DLLs
Source: "..\src\Audex\bin\x64\Release\net48\bass.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\basswasapi.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\bassflac.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\bassmix.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Audex\bin\x64\Release\net48\bass_fx.dll"; DestDir: "{app}"; Flags: ignoreversion
; Optional plugin DLLs
Source: "..\src\Audex\bin\x64\Release\net48\bass_aac.dll"; DestDir: "{app}"; Flags: ignoreversion; Components: aac
Source: "..\src\Audex\bin\x64\Release\net48\basswma.dll"; DestDir: "{app}"; Flags: ignoreversion; Components: wma
Source: "..\src\Audex\bin\x64\Release\net48\bassopus.dll"; DestDir: "{app}"; Flags: ignoreversion; Components: opus

[Types]
Name: "full";   Description: "All supported formats"
Name: "custom"; Description: "Custom selection"; Flags: iscustom

[Components]
Name: "core";   Description: "Core audio formats (.wav, .mp3, .flac, .aiff, .aif, .ogg)"; Types: full custom; Flags: fixed
Name: "module"; Description: "Module formats (.mod, .xm, .it, .s3m)";                     Types: full custom
Name: "aac";    Description: "AAC/M4A formats (.aac, .m4a)";                              Types: full custom
Name: "wma";    Description: "WMA format (.wma)";                                          Types: full custom
Name: "opus";   Description: "Opus format (.opus)";                                        Types: full custom

[Code]

const
  AppCLSID   = '{F2A5B8C3-4D7E-4A9B-8C1F-3E6D5A7B9C2E}';
  AppID_GUID = '{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}';
  PreviewHandlerIID = '{8895b1c6-b41f-4c1c-a562-0d564250836f}';
  // .NET 4.8 minimum release value
  DotNet48Release = 528040;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function RegAsmPath(): String;
var
  InstallRoot: String;
begin
  // Try to get the install root from registry first
  if RegQueryStringValue(HKLM,
      'SOFTWARE\Microsoft\.NETFramework',
      'InstallRoot', InstallRoot) then
  begin
    Result := InstallRoot + 'v4.0.30319\RegAsm.exe';
    if FileExists(Result) then
      Exit;
  end;
  // Fallback to well-known x64 location
  Result := ExpandConstant('{win}') + '\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe';
end;

procedure RegisterExtension(const Ext: String);
var
  SysAssocPath: String;
  ProgId: String;
  ProgIdShellexPath: String;
begin
  // SystemFileAssociations -- most reliable path on modern Windows
  SysAssocPath := 'SystemFileAssociations\' + Ext + '\shellex\' + PreviewHandlerIID;
  RegWriteStringValue(HKCR, SysAssocPath, '', AppCLSID);

  // Also register under the extension ProgID shellex (if a ProgID exists)
  // NOTE: Do NOT touch UserChoice/AppX ProgIds -- they cause Explorer freezes
  if RegQueryStringValue(HKCR, Ext, '', ProgId) then
  begin
    if (ProgId <> '') and
       (Pos('AppX', ProgId) = 0) and          // skip AppX/UWP ProgIds
       (Pos('UserChoice', ProgId) = 0) then    // skip UserChoice ProgIds
    begin
      ProgIdShellexPath := ProgId + '\shellex\' + PreviewHandlerIID;
      RegWriteStringValue(HKCR, ProgIdShellexPath, '', AppCLSID);
    end;
  end;
end;

procedure UnregisterExtension(const Ext: String);
var
  SysAssocPath: String;
  ProgId: String;
  ProgIdShellexPath: String;
begin
  SysAssocPath := 'SystemFileAssociations\' + Ext + '\shellex\' + PreviewHandlerIID;
  RegDeleteKeyIfEmpty(HKCR, SysAssocPath);
  RegDeleteValue(HKCR, 'SystemFileAssociations\' + Ext + '\shellex', PreviewHandlerIID);

  if RegQueryStringValue(HKCR, Ext, '', ProgId) then
  begin
    if (ProgId <> '') and
       (Pos('AppX', ProgId) = 0) and
       (Pos('UserChoice', ProgId) = 0) then
    begin
      ProgIdShellexPath := ProgId + '\shellex\' + PreviewHandlerIID;
      RegDeleteValue(HKCR, ProgId + '\shellex', PreviewHandlerIID);
    end;
  end;
end;

procedure RegisterAllSelectedExtensions();
var
  I: Integer;
  CoreExts, ModExts, AacExts, WmaExts, OpusExts: TStringList;
begin
  // Core extensions -- always registered (flags: fixed in [Components])
  CoreExts := TStringList.Create;
  try
    CoreExts.Add('.wav');
    CoreExts.Add('.mp3');
    CoreExts.Add('.flac');
    CoreExts.Add('.aiff');
    CoreExts.Add('.aif');
    CoreExts.Add('.ogg');
    for I := 0 to CoreExts.Count - 1 do
      RegisterExtension(CoreExts[I]);
  finally
    CoreExts.Free;
  end;

  // Module formats
  if WizardIsComponentSelected('module') then
  begin
    ModExts := TStringList.Create;
    try
      ModExts.Add('.mod');
      ModExts.Add('.xm');
      ModExts.Add('.it');
      ModExts.Add('.s3m');
      for I := 0 to ModExts.Count - 1 do
        RegisterExtension(ModExts[I]);
    finally
      ModExts.Free;
    end;
  end;

  // AAC/M4A
  if WizardIsComponentSelected('aac') then
  begin
    AacExts := TStringList.Create;
    try
      AacExts.Add('.aac');
      AacExts.Add('.m4a');
      for I := 0 to AacExts.Count - 1 do
        RegisterExtension(AacExts[I]);
    finally
      AacExts.Free;
    end;
  end;

  // WMA
  if WizardIsComponentSelected('wma') then
  begin
    WmaExts := TStringList.Create;
    try
      WmaExts.Add('.wma');
      for I := 0 to WmaExts.Count - 1 do
        RegisterExtension(WmaExts[I]);
    finally
      WmaExts.Free;
    end;
  end;

  // Opus
  if WizardIsComponentSelected('opus') then
  begin
    OpusExts := TStringList.Create;
    try
      OpusExts.Add('.opus');
      for I := 0 to OpusExts.Count - 1 do
        RegisterExtension(OpusExts[I]);
    finally
      OpusExts.Free;
    end;
  end;
end;

procedure UnregisterAllExtensions();
var
  AllExts: TStringList;
  I: Integer;
begin
  AllExts := TStringList.Create;
  try
    AllExts.Add('.wav');
    AllExts.Add('.mp3');
    AllExts.Add('.flac');
    AllExts.Add('.aiff');
    AllExts.Add('.aif');
    AllExts.Add('.ogg');
    AllExts.Add('.mod');
    AllExts.Add('.xm');
    AllExts.Add('.it');
    AllExts.Add('.s3m');
    AllExts.Add('.aac');
    AllExts.Add('.m4a');
    AllExts.Add('.wma');
    AllExts.Add('.opus');
    for I := 0 to AllExts.Count - 1 do
      UnregisterExtension(AllExts[I]);
  finally
    AllExts.Free;
  end;
end;

// ---------------------------------------------------------------------------
// Step 1: .NET 4.8 detection before setup begins
// ---------------------------------------------------------------------------

function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := True;
  if not RegQueryDWordValue(HKLM,
      'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
      'Release', Release) then
  begin
    MsgBox(
      '.NET Framework 4.8 or later is required but was not found on this system.' + #13#10 + #13#10 +
      'Please download and install it from:' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet-framework/net48' + #13#10 + #13#10 +
      'After installation, run this setup again.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if Release < DotNet48Release then
  begin
    MsgBox(
      '.NET Framework 4.8 or later is required.' + #13#10 +
      'Your installed version release key is ' + IntToStr(Release) +
      ' (need ' + IntToStr(DotNet48Release) + ' or higher).' + #13#10 + #13#10 +
      'Please download and install .NET Framework 4.8 from:' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet-framework/net48' + #13#10 + #13#10 +
      'After installation, run this setup again.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;
end;

// ---------------------------------------------------------------------------
// Step 2: Terminate prevhost.exe before file copy to release the DLL lock
// ---------------------------------------------------------------------------

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Response: Integer;
begin
  Result := '';
  NeedsRestart := False;

  // Check if prevhost.exe is currently running
  if not FileExists(ExpandConstant('{sys}\prevhost.exe')) then
    // prevhost.exe is not a standalone file we can check this way;
    // instead we attempt to find its window or just attempt kill
    ;

  // Use tasklist to detect prevhost.exe
  Exec(ExpandConstant('{sys}\tasklist.exe'),
       '/fi "IMAGENAME eq prevhost.exe" /nh',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // ResultCode 0 means tasklist ran (not necessarily that process exists)
  // We optimistically offer to kill prevhost.exe if it may be running
  Response := MsgBox(
    'Windows Preview Host (prevhost.exe) may be running and could hold the preview handler DLL.' + #13#10 + #13#10 +
    'It is strongly recommended to stop it before installing to prevent file-in-use errors.' + #13#10 + #13#10 +
    'Stop prevhost.exe now?',
    mbConfirmation, MB_YESNO);

  if Response = IDYES then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'),
         '/f /im prevhost.exe',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
    // ResultCode non-zero is fine -- just means prevhost wasn't running
  end
  else
  begin
    Result := 'Installation cancelled. Please close any open preview panes and try again.';
  end;
end;

// ---------------------------------------------------------------------------
// Step 3: Post-install COM registration and registry setup
// ---------------------------------------------------------------------------

procedure CurStepChanged(CurStep: TSetupStep);
var
  RegAsm: String;
  ResultCode: Integer;
  ClsidBase: String;
  InprocPath: String;
  PhPath: String;
  Response: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // --- Run regasm to register the COM class ---
    RegAsm := RegAsmPath();
    if not FileExists(RegAsm) then
    begin
      MsgBox(
        'RegAsm.exe was not found at:' + #13#10 + RegAsm + #13#10 + #13#10 +
        'COM registration could not be completed.' + #13#10 +
        'Please register manually using:' + #13#10 +
        '"' + RegAsm + '" "' + ExpandConstant('{app}\Audex.dll') + '" /codebase',
        mbError, MB_OK);
      Exit;
    end;

    Exec(RegAsm,
         '"' + ExpandConstant('{app}\Audex.dll') + '" /codebase',
         ExpandConstant('{app}'),
         SW_HIDE, ewWaitUntilTerminated, ResultCode);

    if ResultCode <> 0 then
    begin
      MsgBox(
        'RegAsm.exe returned error code ' + IntToStr(ResultCode) + '.' + #13#10 +
        'COM registration may have failed. The preview handler may not work correctly.',
        mbError, MB_OK);
      // Continue anyway -- partial registration is better than nothing
    end;

    // --- Set CLSID registry entries (mirrors register.ps1) ---
    ClsidBase := 'CLSID\' + AppCLSID;

    // DisplayName and AppID on the CLSID key
    RegWriteStringValue(HKCR, ClsidBase, 'DisplayName', 'Audex');
    RegWriteStringValue(HKCR, ClsidBase, 'AppID', AppID_GUID);

    // DisableLowILProcessIsolation=1 -- required for .NET CLR in prevhost.exe low-integrity
    RegWriteDWordValue(HKCR, ClsidBase, 'DisableLowILProcessIsolation', 1);

    // ThreadingModel=Apartment on InprocServer32 -- required for WinForms STA
    InprocPath := ClsidBase + '\InprocServer32';
    RegWriteStringValue(HKCR, InprocPath, 'ThreadingModel', 'Apartment');

    // --- Register in global PreviewHandlers list (HKLM) ---
    PhPath := 'SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers';
    RegWriteStringValue(HKLM, PhPath, AppCLSID, 'Audex');

    // --- Register file type associations based on selected components ---
    RegisterAllSelectedExtensions();

    // --- Prompt to restart Windows Explorer ---
    Response := MsgBox(
      'Installation complete!' + #13#10 + #13#10 +
      'Would you like to restart Windows Explorer now to activate the preview handler?' + #13#10 + #13#10 +
      '(Recommended -- the preview handler will not work until Explorer is restarted.)',
      mbConfirmation, MB_YESNO);

    if Response = IDYES then
    begin
      // Kill explorer then restart it
      Exec(ExpandConstant('{sys}\taskkill.exe'),
           '/f /im explorer.exe',
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(1000);
      // Restart explorer as the current user (shell restart)
      Exec(ExpandConstant('{win}\explorer.exe'),
           '',
           ExpandConstant('{win}'),
           SW_SHOW, ewNoWait, ResultCode);
    end;
  end;
end;

// ---------------------------------------------------------------------------
// Step 4: Uninstall -- regasm /unregister, registry cleanup, optional data
// ---------------------------------------------------------------------------

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RegAsm: String;
  ResultCode: Integer;
  ClsidBase: String;
  PhPath: String;
  LocalAppData: String;
  TempDir: String;
  Response: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // --- Run regasm /unregister ---
    RegAsm := RegAsmPath();
    if FileExists(RegAsm) then
    begin
      Exec(RegAsm,
           '"' + ExpandConstant('{app}\Audex.dll') + '" /unregister',
           ExpandConstant('{app}'),
           SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;

    // --- Remove CLSID registry key ---
    ClsidBase := 'CLSID\' + AppCLSID;
    RegDeleteValue(HKCR, ClsidBase, 'DisplayName');
    RegDeleteValue(HKCR, ClsidBase, 'AppID');
    RegDeleteValue(HKCR, ClsidBase, 'DisableLowILProcessIsolation');
    RegDeleteValue(HKCR, ClsidBase + '\InprocServer32', 'ThreadingModel');

    // --- Remove from PreviewHandlers list ---
    PhPath := 'SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers';
    RegDeleteValue(HKLM, PhPath, AppCLSID);

    // --- Remove all file association registry entries ---
    UnregisterAllExtensions();

    // --- Optional: remove settings and cache data ---
    Response := MsgBox(
      'Would you like to remove settings and cache data?' + #13#10 + #13#10 +
      'This will delete:' + #13#10 +
      '  %LOCALAPPDATA%\Audex\' + #13#10 +
      '  %TEMP%\Audex\' + #13#10 + #13#10 +
      'Select No to keep your settings (waveform cache, preferences, etc.).',
      mbConfirmation, MB_YESNO);

    if Response = IDYES then
    begin
      // Remove settings directory (%LOCALAPPDATA%\Audex\)
      LocalAppData := GetEnv('LOCALAPPDATA');
      if LocalAppData <> '' then
        DelTree(LocalAppData + '\Audex', True, True, True);

      // Remove cache directory (%TEMP%\Audex\)
      TempDir := GetEnv('TEMP');
      if TempDir <> '' then
        DelTree(TempDir + '\Audex', True, True, True);
    end;
  end;
end;
