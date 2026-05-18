# Voicemeeter MQTT Bridge v1.0.1

Windows tray bridge for **Voicemeeter Potato**, MQTT, and Home Assistant.

Copyright (C) 2026 Richard Cornwell <rcp@techtoknow.net>

This project is licensed under the **GNU General Public License v3.0 or later**. See `LICENSE` and `COPYING` for the full license text.

## What it does

- Loads the Voicemeeter Remote DLL from the local Voicemeeter install.
- Connects to Voicemeeter Potato and exposes mapped controls over MQTT.
- Publishes state changes and accepts MQTT commands.
- Optionally publishes Home Assistant MQTT discovery.
- Runs as a Windows tray application.
- Supports Start with Windows.
- Optionally starts Voicemeeter Potato when the bridge starts.
- Uses `assets\app.ico` for the EXE, installer, and tray icon.

## Important v1.0.1 change: config moved to AppData

Version 1.0.1 fixes a major installed-app issue from v1.0.0.

The app no longer writes live settings or logs to the install folder under `Program Files`. Normal Windows users cannot write there, so saving settings could silently fail or require admin rights.

Runtime files are now stored here:

```text
%AppData%\Voicemeeter MQTT Bridge\appsettings.json
%AppData%\Voicemeeter MQTT Bridge\voicemeeter-mqtt-bridge.log
```

The tray menu now includes **Open Config Folder**, which opens:

```text
%AppData%\Voicemeeter MQTT Bridge\
```

## Settings migration

On first run, the app checks this order:

```text
1. %AppData%\Voicemeeter MQTT Bridge\appsettings.json
2. appsettings.json next to the EXE, only to import old v1.0.0/testing settings
3. appsettings.demo.json next to the EXE
4. built-in defaults
```

After migration, all future saves go to AppData.

## Build EXE

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
.\build.ps1
```

The executable publishes to:

```text
bin\Release\net8.0-windows\win-x64\publish\VoicemeeterMqttBridge.exe
```

`build.ps1` copies `appsettings.demo.json` to the publish folder. It does **not** overwrite your live AppData `appsettings.json`.

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

## Installer notes

The installer is x64-only and installs under `Program Files` on 64-bit Windows.

The installer does **not** need to install a live `appsettings.json`. The app creates the real writable config in AppData on first launch.

The installer should continue to ship only safe files such as:

- `VoicemeeterMqttBridge.exe`
- `appsettings.demo.json`
- `README.md`
- `LICENSE`
- `COPYING`
- `NOTICE.txt`
- `COPYRIGHT.txt`
- `assets\app.ico`

Do not ship:

- `appsettings.json`
- `*.log`
- `*.user`
- `*.suo`
- `*.db`
- `*.sqlite`
- `*.sqlite3`

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

## Home Assistant

When Home Assistant discovery is enabled, the app publishes MQTT discovery payloads using the configured discovery prefix, usually:

```text
homeassistant
```

The default MQTT base topic remains:

```text
voicemeeter/{computer}
```

## License

Voicemeeter MQTT Bridge is free software under the GNU GPL v3.0 or later.

```text
Voicemeeter MQTT Bridge
Copyright (C) 2026 Richard Cornwell <rcp@techtoknow.net>
```

This program is distributed without any warranty; without even the implied warranty of merchantability or fitness for a particular purpose. See the GNU General Public License for more details.
