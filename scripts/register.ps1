# Register Audex preview handler
# Run as Administrator
#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

$clsid = "{F2A5B8C3-4D7E-4A9B-8C1F-3E6D5A7B9C2E}"
$appId = "{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}"
$previewHandlerIID = "{8895b1c6-b41f-4c1c-a562-0d564250836f}"

$projectDir = Join-Path $PSScriptRoot "..\src\Audex"

# Step 1: Stop prevhost.exe FIRST to release loaded DLLs
Write-Host "Stopping prevhost.exe processes..."
Get-Process prevhost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Step 2: Build the project
Write-Host "Building project (Release)..." -ForegroundColor Cyan
Push-Location (Join-Path $PSScriptRoot "..")
dotnet build $projectDir -c Release
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Error "Build failed."
    exit 1
}
Pop-Location
Write-Host "Build succeeded." -ForegroundColor Green

$dllPath = Join-Path $projectDir "bin\x64\Release\net48\Audex.dll"
if (-not (Test-Path $dllPath)) {
    Write-Error "DLL not found at: $dllPath"
    exit 1
}
Write-Host "Using DLL: $dllPath"

# Step 2.5: Determine associations and back up existing handlers BEFORE RegAsm.
# RegAsm invokes PreviewHandlerRegistration.Register, which writes associations by default.
# Capture them first, then opt the callback out so this script can apply the plugin-aware list.
Write-Host "Determining supported extensions..."

$extensions = @(".wav", ".mp3", ".flac", ".aiff", ".aif", ".ogg")
$extensions += @(".mod", ".xm", ".it", ".s3m")
$outputDir = Split-Path $dllPath

if (Test-Path (Join-Path $outputDir "bass_aac.dll")) {
    $extensions += @(".aac", ".m4a")
    Write-Host "  bass_aac.dll found -- registering .aac, .m4a" -ForegroundColor Green
} else {
    Write-Host "  bass_aac.dll NOT found -- skipping .aac, .m4a registration" -ForegroundColor Yellow
}

if (Test-Path (Join-Path $outputDir "basswma.dll")) {
    $extensions += @(".wma")
    Write-Host "  basswma.dll found -- registering .wma" -ForegroundColor Green
} else {
    Write-Host "  basswma.dll NOT found -- skipping .wma registration" -ForegroundColor Yellow
}

if (Test-Path (Join-Path $outputDir "bassopus.dll")) {
    $extensions += @(".opus")
    Write-Host "  bassopus.dll found -- registering .opus" -ForegroundColor Green
} else {
    Write-Host "  bassopus.dll NOT found -- skipping .opus registration" -ForegroundColor Yellow
}

Write-Host "  Registering $($extensions.Count) extensions: $($extensions -join ', ')" -ForegroundColor Cyan
Write-Host "Backing up existing preview handler registrations..."

$backupPath = Join-Path $env:LOCALAPPDATA "Audex\prev-handlers.json"
$systemAssociationBackup = @{}
$progIdBackup = @{}

function Copy-JsonPropertiesToDictionary {
    param($Source, [System.Collections.IDictionary]$Destination)
    if ($null -eq $Source) { return }
    foreach ($property in $Source.PSObject.Properties) {
        $Destination[$property.Name] = [string]$property.Value
    }
}

# Preserve the original backup across upgrades/re-registration. Older Audex backups used
# a flat extension -> CLSID object, so accept that shape as SystemFileAssociations data.
if (Test-Path $backupPath) {
    try {
        $existingBackup = Get-Content $backupPath -Encoding UTF8 | ConvertFrom-Json
        if ($existingBackup.PSObject.Properties.Name -contains "SystemFileAssociations") {
            Copy-JsonPropertiesToDictionary $existingBackup.SystemFileAssociations $systemAssociationBackup
            Copy-JsonPropertiesToDictionary $existingBackup.ProgIdAssociations $progIdBackup
        } else {
            Copy-JsonPropertiesToDictionary $existingBackup $systemAssociationBackup
        }
    }
    catch {
        Write-Host "  Warning: existing handler backup could not be read: $_" -ForegroundColor Yellow
    }
}

foreach ($ext in $extensions) {
    $sysAssocPath = "Registry::HKCR\SystemFileAssociations\$ext\shellex\$previewHandlerIID"
    if (Test-Path $sysAssocPath) {
        $existingClsid = (Get-ItemProperty $sysAssocPath -ErrorAction SilentlyContinue).'(default)'
        if ($existingClsid -and $existingClsid -ne $clsid -and -not $systemAssociationBackup.ContainsKey($ext)) {
            $systemAssociationBackup[$ext] = $existingClsid
        }
    }

    $extPath = "Registry::HKCR\$ext"
    if (Test-Path $extPath) {
        $progId = (Get-ItemProperty $extPath -ErrorAction SilentlyContinue).'(default)'
        if ($progId) {
            $progIdShellexPath = "Registry::HKCR\$progId\shellex\$previewHandlerIID"
            if (Test-Path $progIdShellexPath) {
                $existingProgIdClsid = (Get-ItemProperty $progIdShellexPath -ErrorAction SilentlyContinue).'(default)'
                if ($existingProgIdClsid -and $existingProgIdClsid -ne $clsid -and -not $progIdBackup.ContainsKey($progId)) {
                    $progIdBackup[$progId] = $existingProgIdClsid
                }
            }
        }
    }
}

$backupDir = Split-Path $backupPath
if (-not (Test-Path $backupDir)) { New-Item $backupDir -ItemType Directory -Force | Out-Null }
$backup = [ordered]@{
    SystemFileAssociations = $systemAssociationBackup
    ProgIdAssociations = $progIdBackup
}
$backup | ConvertTo-Json -Depth 4 | Set-Content $backupPath -Encoding UTF8
Write-Host "  Preserved $($systemAssociationBackup.Count) system and $($progIdBackup.Count) ProgID handler(s)" -ForegroundColor Green

# Step 3: Register COM class with regasm
$regasm = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
if (-not (Test-Path $regasm)) {
    Write-Error "RegAsm.exe not found at: $regasm"
    exit 1
}

Write-Host "Registering COM component..."
$previousSkipExtensionRegistration = $env:AUDEX_SKIP_EXTENSION_REGISTRATION
$env:AUDEX_SKIP_EXTENSION_REGISTRATION = "1"
try {
    & $regasm $dllPath /codebase /tlb 2>&1 | Write-Host
    $regasmExitCode = $LASTEXITCODE
}
finally {
    if ($null -eq $previousSkipExtensionRegistration) {
        Remove-Item Env:AUDEX_SKIP_EXTENSION_REGISTRATION -ErrorAction SilentlyContinue
    }
    else {
        $env:AUDEX_SKIP_EXTENSION_REGISTRATION = $previousSkipExtensionRegistration
    }
}
if ($regasmExitCode -ne 0) {
    Write-Error "RegAsm failed with exit code $regasmExitCode."
    exit 1
}
Start-Sleep -Milliseconds 200

# Step 4: Set preview handler registry entries
Write-Host "Configuring preview handler registry entries..."

$clsidPath = "Registry::HKCR\CLSID\$clsid"
if (Test-Path $clsidPath) {
    Set-ItemProperty $clsidPath -Name "DisplayName" -Value "Audex"
    Set-ItemProperty $clsidPath -Name "AppID" -Value $appId

    # Allow .NET CLR to initialize in prevhost.exe by disabling low-integrity isolation
    # Without this, the CLR can't load managed assemblies in low-IL prevhost.exe
    New-ItemProperty $clsidPath -Name "DisableLowILProcessIsolation" -Value 1 -PropertyType DWord -Force | Out-Null

    # Ensure ThreadingModel is Apartment (matches all working preview handlers)
    $inprocPath = "$clsidPath\InprocServer32"
    if (Test-Path $inprocPath) {
        Set-ItemProperty $inprocPath -Name "ThreadingModel" -Value "Apartment"
    }

    Write-Host "  CLSID configured (with DisableLowILProcessIsolation)" -ForegroundColor Green
} else {
    Write-Error "CLSID not found after regasm - registration failed"
    exit 1
}

# Step 5: Register in global PreviewHandlers list
$phPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers"
if (-not (Test-Path $phPath)) {
    New-Item -Path $phPath -Force | Out-Null
}
New-ItemProperty -Path $phPath -Name $clsid -Value "Audex" -PropertyType String -Force | Out-Null
Write-Host "  PreviewHandlers entry added (HKLM)" -ForegroundColor Green

# Step 6: Register file extension associations
foreach ($ext in $extensions) {
    # SystemFileAssociations - most reliable on modern Windows
    $sysAssocPath = "Registry::HKCR\SystemFileAssociations\$ext\shellex\$previewHandlerIID"
    if (-not (Test-Path $sysAssocPath)) {
        New-Item -Path $sysAssocPath -Force | Out-Null
    }
    Set-ItemProperty $sysAssocPath -Name "(default)" -Value $clsid
    Write-Host "  Registered: $ext (SystemFileAssociations)" -ForegroundColor Green

    # Also register under the extension's HKCR ProgID shellex
    $extPath = "Registry::HKCR\$ext"
    if (Test-Path $extPath) {
        $progId = (Get-ItemProperty $extPath).'(default)'
        if ($progId) {
            $progIdShellexPath = "Registry::HKCR\$progId\shellex\$previewHandlerIID"
            if (-not (Test-Path $progIdShellexPath)) {
                New-Item -Path $progIdShellexPath -Force | Out-Null
            }
            Set-ItemProperty $progIdShellexPath -Name "(default)" -Value $clsid
            Write-Host "  Registered: $ext -> $progId (ProgID shellex)" -ForegroundColor Green
        }
    }

    # NOTE: Do NOT modify UserChoice/AppX ProgIds - they are protected and cause Explorer freezes
}

Write-Host ""
Write-Host "Registration complete!" -ForegroundColor Green
Write-Host ""
Write-Host "To activate, restart Windows Explorer:"
Write-Host "  Stop-Process -Name explorer -Force"
Write-Host ""
