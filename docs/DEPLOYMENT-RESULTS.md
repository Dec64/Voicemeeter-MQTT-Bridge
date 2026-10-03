# Authorized RC deployment — 2026-10-03

The owner explicitly requested an icon, complete layman's documentation, GitHub publication, HACS installation and replacement of the installed Windows bridge. These actions are complete as an identified release candidate. This record does not certify physical source assignments, audible processing or Fire-tablet performance.

## Published artifacts

- Bridge: [Dec64/Voicemeeter-MQTT-Bridge](https://github.com/Dec64/Voicemeeter-MQTT-Bridge), default branch `v2-release-candidate`, implementation commit `01d6081`.
- Windows release: [v2.0.0-rc.1](https://github.com/Dec64/Voicemeeter-MQTT-Bridge/releases/tag/v2.0.0-rc.1), installer and adjacent SHA-256 checksum.
- Card: [Dec64/voicemeeter-channel-card](https://github.com/Dec64/voicemeeter-channel-card), `main` commit `1796d04`, matching `v2.0.0-rc.1` prerelease.
- Original generated transparent icon appears in the GitHub and HACS README. The complete guide covers installation, all supported card/settings options, terms, troubleshooting and rollback. It is also included with the Windows installer.

The upstream repository was not modified. Private broker credentials/configuration and local backups were excluded from publication.

## Installed Windows bridge

The installed 1.0.1.0 bridge was stopped after backing up its installation and private settings outside the repository. The production installer completed with exit code 0. The installed executable hash matched the release publish executable, and the installer preserved the existing settings file byte-for-byte.

A validated v2 profile was then applied atomically: 50 ms sample interval, 100 ms fast stream (10 Hz), 1000 ms slow stream, all 16 canonical sources enabled. Existing broker/client/topic, startup preferences, legacy discovery and legacy meter enable/cadence were preserved. Optional mono, compressor, gate, denoiser and EQ groups were enabled; parametric EQ cells remain off. The actual C# dry-run validated the private configuration before replacement.

The installed application started as version 2.0.0.0, connected successfully to MQTT and logged an active v2 telemetry session. No gain, mute, routing or processing command was sent during deployment.

## Home Assistant

HACS accepted the standalone custom dashboard repository and downloaded commit `1796d04` into `/config/www/community/voicemeeter-channel-card`. All 18 installed JavaScript modules matched the exported source after normalizing CRLF/LF; four raw hashes differ solely because the local Git checkout uses CRLF. HACS registered `/hacsfiles/voicemeeter-channel-card/voicemeeter-channel-card.js`; the old development resource registration was removed to avoid loading two copies. Old staged files remain available for rollback.

The sidebar now shows **Voicemeeter** with a theme-aware audio icon. Its new **Mixer** view uses the production bridge topic, groups all 16 canonical channels into hardware inputs, PC audio, hardware outputs and virtual outputs, and binds gain/mute controls to exact enabled MQTT entity-registry unique IDs. Development checks remain separate subviews. Office Hub was not changed.

The rendered production view showed native channel labels, current gain/mute readbacks, independent silence/signal states and changing real peak values. Scrolling verified A1-A5 and B1-B3, including the final channel's controls. Full-width cards were visually checked to avoid wrapping channel names into narrow fragments. This proves a working production connection and rendering, not a sustained all-visible-card performance measurement.

## Remaining stable-release acceptance

- Owner confirmation of physical source assignments and audible gain/mute/routing/processing behaviour.
- Full sustained browser/device/recovery matrix, including an actual Fire tablet.
- A subsequent HACS version-update trial and an exercised live rollback. Initial installation and backed-up live upgrade succeeded.

The private installation/configuration backups and saved pre-deployment dashboard configuration remain outside Git. Restore only the specific bridge/configuration/resource/dashboard involved; do not restore all Home Assistant storage.
