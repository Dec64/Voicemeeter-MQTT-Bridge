<#
Voicemeeter MQTT Bridge build script
Copyright (C) 2026 Richard Cornwell <rcp@techtoknow.net>

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ProjectFile = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Find-ProjectFile {
    param([string]$Root, [string]$RequestedProjectFile)

    if (-not [string]::IsNullOrWhiteSpace($RequestedProjectFile)) {
        $resolved = Join-Path $Root $RequestedProjectFile
        if (!(Test-Path $resolved)) {
            throw "Requested project file not found: $resolved"
        }
        return (Resolve-Path $resolved).Path
    }

    $preferred = Join-Path $Root "VoicemeeterMqttBridge.csproj"
    if (Test-Path $preferred) {
        return (Resolve-Path $preferred).Path
    }

    $projects = Get-ChildItem -Path $Root -Filter "*.csproj" -File | Sort-Object Name

    if ($projects.Count -eq 1) {
        return $projects[0].FullName
    }

    if ($projects.Count -eq 0) {
        throw "No .csproj file found in $Root"
    }

    $names = ($projects | ForEach-Object { "  - $($_.Name)" }) -join [Environment]::NewLine
    throw "More than one .csproj file found. Re-run with -ProjectFile `"VoicemeeterMqttBridge.csproj`"." + [Environment]::NewLine + $names
}

$projectPath = Find-ProjectFile -Root $root -RequestedProjectFile $ProjectFile
$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)

$publishDir = Join-Path $root "bin\$Configuration\net8.0-windows\$Runtime\publish"

Write-Host "Project: $projectPath"
Write-Host "Configuration: $Configuration"
Write-Host "Runtime: $Runtime"
Write-Host ""

dotnet restore "$projectPath"
dotnet build "$projectPath" -c $Configuration -r $Runtime --self-contained true
dotnet publish "$projectPath" -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true

if (!(Test-Path $publishDir)) {
    throw "Publish directory was not created: $publishDir"
}

# Do not ever ship live/private runtime files.
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
    Get-ChildItem -Path $publishDir -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Removing private/runtime file from publish output: $($_.FullName)"
        Remove-Item $_.FullName -Force
    }
}

# appsettings.demo.json is safe to ship and useful beside the EXE.
$demoConfig = Join-Path $root "appsettings.demo.json"
if (Test-Path $demoConfig) {
    Copy-Item $demoConfig (Join-Path $publishDir "appsettings.demo.json") -Force
} else {
    Write-Warning "appsettings.demo.json not found at repo root. Creating a safe default demo config in publish output."
    $demoJson = @'
{
  "mqttHost": "127.0.0.1",
  "mqttPort": 1883,
  "mqttUsername": "",
  "mqttPassword": "",
  "baseTopic": "voicemeeter/{computer}",
  "homeAssistantDiscovery": true,
  "homeAssistantPrefix": "homeassistant",
  "publishMeters": false,
  "pollIntervalMs": 500,
  "meterIntervalMs": 1000,
  "startVoicemeeterWithApp": false
}
'@
    Set-Content -Path (Join-Path $publishDir "appsettings.demo.json") -Value $demoJson -Encoding UTF8
}

Write-Host ""
Write-Host "EXE output:"
Write-Host (Join-Path $publishDir "$projectName.exe")
