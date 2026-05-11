# Voicemeeter MQTT Bridge v1.0.0

Windows tray bridge for **Voicemeeter Potato**, MQTT, and Home Assistant.

Copyright (C) 2026 Richard Cornwell <rcp@techtoknow.net>

This project is licensed under the **GNU General Public License v3.0**. See `LICENSE` for the full license text.

## What it does

- Loads the Voicemeeter Remote DLL from the local Voicemeeter install.
- Connects to Voicemeeter Potato and exposes mapped controls over MQTT.
- Publishes state changes and accepts MQTT commands.
- Optionally publishes Home Assistant MQTT discovery.
- Runs as a Windows tray application.
- Supports Start with Windows.
- Optionally starts Voicemeeter Potato when the bridge starts.
- Uses `assets\app.ico` for the EXE, installer, and tray icon.

## Build EXE

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
.\build.ps1
```

The executable publishes to:

```text
bin\Release\net8.0-windows\win-x64\publish\VoicemeeterMqttBridge.exe
```

`build.ps1` copies `appsettings.demo.json` to the publish folder. It does **not** overwrite your live `appsettings.json`.

## Build installer

Install Inno Setup 6, then run:

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
.\build-installer.ps1
```

The installer will be created under:

```text
installer\output\
```

## Runtime config

The app reads:

```text
appsettings.json
```

If it does not exist, the app creates one with defaults. The demo config files are templates only.

## MQTT topics

Default base topic:

```text
voicemeeter/{computer}
```

Examples:

```text
voicemeeter/my-pc/parameter/strip_0_gain/set
voicemeeter/my-pc/parameter/strip_0_gain/state
voicemeeter/my-pc/parameter/strip_0_mute/set
voicemeeter/my-pc/availability
```

Generic command:

```json
{"parameter":"Strip[0].Gain","value":-6}
```

Publish to:

```text
voicemeeter/my-pc/set
```

## License

Voicemeeter MQTT Bridge is free software under the GNU GPL v3.0.

```text
Voicemeeter MQTT Bridge
Copyright (C) 2026 Richard Cornwell <rcp@techtoknow.net>
```

## Private settings safety

`appsettings.json` is a private runtime file and may contain MQTT credentials. It is intentionally not shipped in the installer or source package.

Safe files to ship:

- `appsettings.demo.json`
- `appsettings.installer-demo.json`

Private files that are automatically stripped from installer input:

- `appsettings.json`
- `*.log`
- `*.user`
- `*.suo`
- `*.db`
- `*.sqlite`
- `*.sqlite3`

`build.ps1` removes these files from the publish folder after publishing. `build-installer.ps1` runs a second safety pass and fails the installer build if a live `appsettings.json` is still present or if the Inno Setup script is changed to use an unsafe wildcard source.


## Installer notes

The installer is x64-only and installs under `Program Files` on 64-bit Windows. The build process strips private runtime files such as `appsettings.json` and logs before compiling the installer. The app manifest has the XML declaration as the first line; do not put comments before it or Windows may report a side-by-side configuration error.


## Installer safety

The installer script intentionally installs explicit files only. It does not package the entire publish folder because that folder may contain a local `appsettings.json` with MQTT credentials from testing. `build.ps1` and `build-installer.ps1` also remove private files such as `appsettings.json`, logs, `.user`, `.suo`, and SQLite/database files before installer compilation.
