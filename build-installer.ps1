<#
Voicemeeter MQTT Bridge
Copyright (C) 2026 Richard Cornwell <rcp@techtoknow.net>
Licensed under the GNU General Public License v3.0. See LICENSE.
#>

param(
    [string]$IsccPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Remove-PrivateInstallerArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDir
    )

    if (-not (Test-Path $PublishDir)) {
        return
    }

    # Files that must never be shipped in the installer.
    # appsettings.demo.json is intentionally allowed.
    $privatePatterns = @(
        "appsettings.json",
        "*.log",
        "*.user",
        "*.suo",
        "*.db",
        "*.sqlite",
        "*.sqlite3"
    )

    foreach ($pattern in $privatePatterns) {
        Get-ChildItem -Path $PublishDir -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "Removing private installer artifact: $($_.FullName)" -ForegroundColor Yellow
            Remove-Item $_.FullName -Force
        }
    }
}

function Test-InstallerInputSafety {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDir,
        [Parameter(Mandatory = $true)]
        [string]$InstallerScript
    )

    $blockedFiles = @(
        "appsettings.json",
        "voicemeeter-mqtt-bridge.log",
        "mqtt-visor.log"
    )

    foreach ($blocked in $blockedFiles) {
        $path = Join-Path $PublishDir $blocked
        if (Test-Path $path) {
            throw "Unsafe installer input found: $path. This file may contain private settings or passwords."
        }
    }

    $issText = Get-Content $InstallerScript -Raw

    if ($issText -match 'Source:\s*"\.\.\\bin\\Release\\net8\.0-windows\\win-x64\\publish\\\*"') {
        throw "Unsafe Inno Setup wildcard detected. Installer must use explicit files, not publish\*."
    }

    if ($issText -match 'DestName:\s*"appsettings\.json"') {
        throw "Unsafe Inno Setup rule detected. Installer must not install a live appsettings.json."
    }
}

.\build.ps1

$publishDir = Join-Path $root "bin\Release\net8.0-windows\win-x64\publish"
$installerScript = Join-Path $root "installer\VoicemeeterMqttBridge.iss"

Remove-PrivateInstallerArtifacts -PublishDir $publishDir
Test-InstallerInputSafety -PublishDir $publishDir -InstallerScript $installerScript

$exePath = Join-Path $publishDir "VoicemeeterMqttBridge.exe"
if (-not (Test-Path $exePath)) {
    throw "Expected published EXE not found: $exePath"
}


function Add-IsccCandidate {
    param(
        [System.Collections.Generic.List[string]]$List,
        [string]$Path
    )

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $List.Add($Path)
    }
}

function Find-InnoSetupCompiler {
    param(
        [string]$OverridePath
    )

    $candidates = [System.Collections.Generic.List[string]]::new()

    # 1) Explicit override from the command line:
    #    .\build-installer.ps1 -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    Add-IsccCandidate $candidates $OverridePath

    # 2) Common install paths. Use ${env:ProgramFiles(x86)} syntax because
    #    $env:ProgramFiles(x86) expands incorrectly in PowerShell strings.
    Add-IsccCandidate $candidates (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    Add-IsccCandidate $candidates (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    Add-IsccCandidate $candidates (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")

    # 3) Registry App Paths, when present.
    $appPathKeys = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe"
    )

    foreach ($key in $appPathKeys) {
        try {
            if (Test-Path $key) {
                $value = (Get-ItemProperty -Path $key)."(default)"
                Add-IsccCandidate $candidates $value
            }
        } catch { }
    }

    # 4) PATH lookup.
    try {
        $whereResults = @(where.exe ISCC.exe 2>$null)
        foreach ($result in $whereResults) {
            Add-IsccCandidate $candidates $result
        }
    } catch { }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    return $null
}

$iscc = Find-InnoSetupCompiler -OverridePath $IsccPath

if (-not $iscc) {
    Write-Host "" -ForegroundColor Yellow
    Write-Host "Inno Setup 6 compiler was not found automatically." -ForegroundColor Yellow
    Write-Host "Checked common Program Files paths, App Paths registry keys, and PATH." -ForegroundColor Yellow
    Write-Host "" -ForegroundColor Yellow
    Write-Host "Try one of these:" -ForegroundColor Yellow
    Write-Host "  .\build-installer.ps1 -IsccPath \"C:\Program Files (x86)\Inno Setup 6\ISCC.exe\"" -ForegroundColor Yellow
    Write-Host "  .\build-installer.ps1 -IsccPath \"C:\Program Files\Inno Setup 6\ISCC.exe\"" -ForegroundColor Yellow
    Write-Host "" -ForegroundColor Yellow
    throw "Inno Setup 6 compiler not found."
}

Write-Host "Using Inno Setup compiler: $iscc" -ForegroundColor Cyan

& $iscc "$root\installer\VoicemeeterMqttBridge.iss"

Write-Host ""
Write-Host "Installer output:" -ForegroundColor Green
Write-Host "$root\installer\output"
