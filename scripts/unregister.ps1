# Unregister Audex preview handler
# Run as Administrator
#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

$clsid = "{F2A5B8C3-4D7E-4A9B-8C1F-3E6D5A7B9C2E}"
$previewHandlerIID = "{8895b1c6-b41f-4c1c-a562-0d564250836f}"

# Full superset of all extensions that could have been registered.
# The unregister script must try all possible extensions because the user
# may have uninstalled a plugin DLL after registration.
$extensions = @(".wav", ".mp3", ".flac", ".aiff", ".aif", ".ogg", ".aac", ".wma", ".opus", ".m4a", ".mod", ".xm", ".it", ".s3m")

# Stop prevhost to release DLL
Write-Host "Stopping prevhost.exe processes..."
Get-Process prevhost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Remove file extension associations
# Only removes entries where OUR CLSID is registered (does not touch other applications' handlers)
Write-Host "Removing file extension associations..."
foreach ($ext in $extensions) {
    # Remove SystemFileAssociations shellex (only if our CLSID is registered)
    $sysAssocPath = "Registry::HKCR\SystemFileAssociations\$ext\shellex\$previewHandlerIID"
    if (Test-Path $sysAssocPath) {
        $currentHandler = (Get-ItemProperty $sysAssocPath -ErrorAction SilentlyContinue).'(default)'
        if ($currentHandler -eq $clsid) {
            Remove-Item $sysAssocPath -Force -ErrorAction SilentlyContinue
            Write-Host "  Removed: $ext (SystemFileAssociations)"
        }
    }

    # Remove ProgID shellex (only if our CLSID is registered)
    $extPath = "Registry::HKCR\$ext"
    if (Test-Path $extPath) {
        $progId = (Get-ItemProperty $extPath -ErrorAction SilentlyContinue).'(default)'
        if ($progId) {
            $progIdShellexPath = "Registry::HKCR\$progId\shellex\$previewHandlerIID"
            if (Test-Path $progIdShellexPath) {
                $handler = (Get-ItemProperty $progIdShellexPath).'(default)'
                if ($handler -eq $clsid) {
                    Remove-Item $progIdShellexPath -Force -ErrorAction SilentlyContinue
                    Write-Host "  Removed: $ext -> $progId (ProgID shellex)"
                }
            }
        }
    }
}

# Remove from PreviewHandlers list
Write-Host "Removing PreviewHandlers entry..."
$phPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers"
if (Test-Path $phPath) {
    Remove-ItemProperty -Path $phPath -Name $clsid -ErrorAction SilentlyContinue
    Write-Host "  Removed from HKLM"
}
$phPath2 = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers"
if (Test-Path $phPath2) {
    Remove-ItemProperty -Path $phPath2 -Name $clsid -ErrorAction SilentlyContinue
    Write-Host "  Removed from HKCU"
}

# Unregister COM class with regasm
$dllPath = Join-Path $PSScriptRoot "..\src\Audex\bin\x64\Release\net48\Audex.dll"
if (-not (Test-Path $dllPath)) {
    $dllPath = Join-Path $PSScriptRoot "..\src\Audex\bin\x64\Debug\net48\Audex.dll"
}

if (Test-Path $dllPath) {
    $regasm = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
    if (Test-Path $regasm) {
        Write-Host "Unregistering COM component..."
        & $regasm $dllPath /unregister 2>&1 | Write-Host
    }
} else {
    Write-Host "DLL not found - cleaning up CLSID manually..."
    $clsidPath = "Registry::HKCR\CLSID\$clsid"
    if (Test-Path $clsidPath) {
        Remove-Item $clsidPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Removed CLSID"
    }
}

# Restore previous preview handler registrations from backup
# Backup was written to %LOCALAPPDATA%\Audex\prev-handlers.json during register.ps1
Write-Host "Restoring previous preview handler registrations..."
$backupPath = Join-Path $env:LOCALAPPDATA "Audex\prev-handlers.json"

if (Test-Path $backupPath) {
    try {
        $storedBackup = Get-Content $backupPath -Encoding UTF8 | ConvertFrom-Json
        $systemBackup = $storedBackup
        $progIdBackup = $null
        if ($storedBackup.PSObject.Properties.Name -contains "SystemFileAssociations") {
            $systemBackup = $storedBackup.SystemFileAssociations
            $progIdBackup = $storedBackup.ProgIdAssociations
        }

        foreach ($ext in $systemBackup.PSObject.Properties) {
            $extName = $ext.Name
            $previousClsid = $ext.Value
            if ($previousClsid) {
                $sysAssocPath = "Registry::HKCR\SystemFileAssociations\$extName\shellex\$previewHandlerIID"
                if (-not (Test-Path $sysAssocPath)) {
                    New-Item -Path $sysAssocPath -Force | Out-Null
                }
                Set-ItemProperty $sysAssocPath -Name "(default)" -Value $previousClsid
                Write-Host "  Restored: $extName -> $previousClsid" -ForegroundColor Cyan
            }
        }

        if ($null -ne $progIdBackup) {
            foreach ($entry in $progIdBackup.PSObject.Properties) {
                $progId = $entry.Name
                $previousClsid = $entry.Value
                if ($previousClsid) {
                    $progIdShellexPath = "Registry::HKCR\$progId\shellex\$previewHandlerIID"
                    if (-not (Test-Path $progIdShellexPath)) {
                        New-Item -Path $progIdShellexPath -Force | Out-Null
                    }
                    Set-ItemProperty $progIdShellexPath -Name "(default)" -Value $previousClsid
                    Write-Host "  Restored ProgID: $progId -> $previousClsid" -ForegroundColor Cyan
                }
            }
        }

        # Clean up backup file after successful restore
        Remove-Item $backupPath -Force -ErrorAction SilentlyContinue
        Write-Host "  Backup file removed" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Could not restore from backup: $_" -ForegroundColor Yellow
    }
}
else {
    Write-Host "  No backup found - previous handlers were not backed up" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Unregistration complete!" -ForegroundColor Green
Write-Host "Restart Explorer to take effect: Stop-Process -Name explorer -Force"
Write-Host ""
