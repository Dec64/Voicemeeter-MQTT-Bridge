<#
Voicemeeter MQTT Bridge
Copyright (C) 2026 Richard Cornwell <rcp@techtoknow.net>
Licensed under the GNU General Public License v3.0. See LICENSE.
#>

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

dotnet restore
# Framework-dependent build for testing:
dotnet build -c Release
# Single-file EXE:
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true

$publishDir = Join-Path $root "bin\Release\net8.0-windows\win-x64\publish"

# Never leave private runtime settings/log files in the publish folder.
# This protects installer builds from accidentally shipping MQTT passwords or local state.
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
        Write-Host "Removing private publish artifact: $($_.FullName)" -ForegroundColor Yellow
        Remove-Item $_.FullName -Force
    }
}

Copy-Item "$root\appsettings.demo.json" "$publishDir\appsettings.demo.json" -Force

Write-Host ""
Write-Host "EXE output:" -ForegroundColor Green
Write-Host "$publishDir\VoicemeeterMqttBridge.exe"
