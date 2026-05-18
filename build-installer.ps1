<#
Voicemeeter MQTT Bridge installer build script
Copyright (C) 2026 Richard Cornwell <rcp@techtoknow.net>

Builds the win-x64 executable and then compiles the Inno Setup installer.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ProjectFile = "",
    [string]$IsccPath = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Find-Iscc {
    param([string]$Requested)

    if (-not [string]::IsNullOrWhiteSpace($Requested)) {
        if (Test-Path $Requested) { return (Resolve-Path $Requested).Path }
        throw "Requested ISCC.exe path not found: $Requested"
    }

    $candidates = @()

    if (${env:ProgramFiles(x86)}) {
        $candidates += "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    }

    if ($env:ProgramFiles) {
        $candidates += "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    }

    if ($env:LOCALAPPDATA) {
        $candidates += "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    }

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    $appPathKeys = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe"
    )

    foreach ($key in $appPathKeys) {
        try {
            if (Test-Path $key) {
                $value = (Get-ItemProperty -Path $key)."(default)"
                if ($value -and (Test-Path $value)) {
                    return (Resolve-Path $value).Path
                }
            }
        } catch { }
    }

    try {
        $where = & where.exe ISCC.exe 2>$null
        if ($LASTEXITCODE -eq 0 -and $where) {
            $first = ($where | Select-Object -First 1)
            if ($first -and (Test-Path $first)) {
                return (Resolve-Path $first).Path
            }
        }
    } catch { }

    throw "Inno Setup 6 compiler not found. Install Inno Setup 6 or run: .\build-installer.ps1 -IsccPath `"C:\Program Files (x86)\Inno Setup 6\ISCC.exe`""
}

$buildScript = Join-Path $root "build.ps1"
if (!(Test-Path $buildScript)) {
    throw "build.ps1 not found at $buildScript"
}

$buildArgs = @(
    "-ExecutionPolicy", "Bypass",
    "-File", $buildScript,
    "-Configuration", $Configuration,
    "-Runtime", $Runtime
)

if (-not [string]::IsNullOrWhiteSpace($ProjectFile)) {
    $buildArgs += @("-ProjectFile", $ProjectFile)
}

& powershell @buildArgs

$publishDir = Join-Path $root "bin\$Configuration\net8.0-windows\$Runtime\publish"
if (!(Test-Path $publishDir)) {
    throw "Publish directory not found after build: $publishDir"
}

# Hard safety check: never package private runtime files.
$privateFiles = @(
    (Join-Path $publishDir "appsettings.json"),
    (Join-Path $publishDir "voicemeeter-mqtt-bridge.log")
)

foreach ($file in $privateFiles) {
    if (Test-Path $file) {
        throw "Unsafe private runtime file exists in publish output and must not be shipped: $file"
    }
}

$issPath = Join-Path $root "installer\VoicemeeterMqttBridge.iss"
if (!(Test-Path $issPath)) {
    throw "Inno Setup script not found: $issPath"
}

$issContent = Get-Content $issPath -Raw

if ($issContent -match 'publish\\\*' -or $issContent -match 'publish/\*') {
    throw "Unsafe Inno Setup wildcard detected. Installer must use explicit files, not publish\*."
}

if ($issContent -match 'DestName:\s*"appsettings\.json"' -or $issContent -match 'appsettings\.json') {
    if ($issContent -notmatch 'appsettings\.demo\.json') {
        throw "Installer script references appsettings.json. Only appsettings.demo.json should be installed."
    }
}

$iscc = Find-Iscc -Requested $IsccPath
Write-Host "Using Inno Setup compiler: $iscc"

& $iscc $issPath

Write-Host ""
Write-Host "Installer build complete."
