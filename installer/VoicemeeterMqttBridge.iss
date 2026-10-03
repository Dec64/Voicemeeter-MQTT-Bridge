; Voicemeeter MQTT Bridge
; Copyright (C) 2026 Richard Cornwell <rcp@techtoknow.net>
; Licensed under the GNU General Public License v3.0. See LICENSE.

#define MyAppName "Voicemeeter MQTT Bridge"
#define MyAppExeName "VoicemeeterMqttBridge.exe"
#define MyAppVersion "2.0.0-rc.1"
#ifdef IsolatedTestBuild
  #define MyAppId "{{31DF9254-F5A6-44A9-8107-FA86EF7E9634}"
  #define MyAppSuffix "-test"
  #undef MyAppName
  #define MyAppName "Voicemeeter MQTT Bridge Installer Test"
#else
  #define MyAppId "{{4A66AF13-D66A-4F2A-9F4F-VOICEMEETERMQTT}}"
  #define MyAppSuffix ""
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Richard Cornwell
DefaultDirName={autopf}\Voicemeeter MQTT Bridge
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=VoicemeeterMqttBridgeSetup-{#MyAppVersion}{#MyAppSuffix}
#ifdef IsolatedTestBuild
PrivilegesRequired=lowest
#endif
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\app.ico
LicenseFile=..\LICENSE

[Files]
; Install only known-safe files. Do not wildcard the publish folder, because it may
; contain a local appsettings.json with MQTT passwords from testing.
Source: "..\bin\Release\net8.0-windows\win-x64\publish\VoicemeeterMqttBridge.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\appsettings.installer-demo.json"; DestDir: "{app}"; DestName: "appsettings.demo.json"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\USER-GUIDE.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\assets\hacs-icon.png"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\COPYING"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\COPYRIGHT.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\assets\app.ico"; DestDir: "{app}\assets"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Open app folder"; Filename: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "runapp"; Description: "Start Voicemeeter MQTT Bridge after installation"; GroupDescription: "After install:"; Flags: checkedonce

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Start {#MyAppName}"; Flags: nowait postinstall skipifsilent; Tasks: runapp
