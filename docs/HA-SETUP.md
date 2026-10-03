# Home Assistant setup

The reusable channel card can run directly in an authenticated HA dashboard using
the existing `subscribe_trigger` WebSocket API. No broker credentials or HA tokens
belong in card configuration. Native subscriptions require an administrator account
on the inspected HA Core 2026.9.4. For the standalone card package, manual installation and planned HACS custom repository workflow, see [distribution instructions](../frontend/channel-card/DISTRIBUTION.md). The exporter prepares a separate HACS repository; publication and actual HACS install/update acceptance are still pending.

The visual editor selects canonical source IDs using metadata labels and can suggest actual HA entities by MQTT unique ID. Enable advanced discovery groups in the bridge before requesting those suggestions. No guessed entity ID is required. History and peak hold are per card and tap; fallback sensors supply only their actual slow measurements. See [five independent cards](../examples/five-channel-cards.yaml) for reviewable configuration.

The dedicated test dashboard now has 1/5/8/16-card views. Its current resource is `/local/voicemeeter-v2-dev-rc1-final/voicemeeter-channel-card.js`; previous directories remain available for rollback. This test resource is separate from an eventual HACS installation. Office Hub has not been changed.

## Stage and register the card

Back up the dashboard and resource registry before changing an existing installation.
From the repository root, stage into a **new** directory under HA's `config/www` share:

```powershell
node tools/stage-channel-card.mjs '\\homeassistant\config\www\voicemeeter-v2-dev-BUILD'
```

Replace BUILD with a unique build identifier. The parent directory must already exist.
The command refuses an existing destination and copies only JavaScript modules and
LICENSE, writing SHA-256 hashes to manifest.json. A failed copy can leave an incomplete
new directory; register it only after a successful run and hash verification. Use a
new directory for each version, including its relative imports, to avoid mixed caches.

Register `/local/voicemeeter-v2-dev-BUILD/voicemeeter-channel-card.js` as a **module**
dashboard resource, using HA's resource API or Settings → Dashboards → Resources.
Reload the browser after registration. No HA restart is needed.

## A live test view

Run DevelopmentBridge with the explicit private configuration described in
[DEVELOPMENT-RUNNER.md](DEVELOPMENT-RUNNER.md). Match its resolved base topic exactly.
Use a separate client ID/topic while the installed bridge runs. The following is a
generic example, not an owner source-name assignment:

```yaml
title: Voicemeeter live test
views:
  - title: Live meters
    path: meters
    cards:
      - type: custom:voicemeeter-channel-card
        bridge:
          base_topic: voicemeeter-development/example-pc
          transport: native_ws
        source:
          id: strip:0
      - type: custom:voicemeeter-channel-card
        bridge:
          base_topic: voicemeeter-development/example-pc
          transport: native_ws
        source:
          id: strip:0
        meter:
          mute_display_mode: post_mute
      - type: custom:voicemeeter-channel-card
        bridge:
          base_topic: voicemeeter-development/example-pc
          transport: native_ws
        source:
          id: strip:5
      - type: custom:voicemeeter-channel-card
        bridge:
          base_topic: voicemeeter-development/example-pc
          transport: native_ws
        source:
          id: strip:6
      - type: custom:voicemeeter-channel-card
        bridge:
          base_topic: voicemeeter-development/example-pc
          transport: native_ws
        source:
          id: bus:0
```

Each card selects its own canonical source/tap; duplicate source cards are intentional
and share the underlying subscriptions. Controls are omitted until their HA entity
mappings are verified. `native_ws` prevents legacy sensor fallback from being mistaken
for real fast delivery. Do not map legacy raw-channel meters as combined dBFS sensors.

When the publisher stops, readings must clear and show stale data. The test dashboard
can remain installed, but it will not show live readings until the development runner
is running. The separate localhost preview continues to use mocks.

## Troubleshooting and rollback

- Waiting for metadata: check the topic and v2 enablement, then publisher status.
- Stream unavailable: check MQTT integration and admin permission; never grant admin
  automatically or embed a token to bypass the permission requirement.
- Metadata arrives but no reading: check stream/source enablement, matching sessions
  and PC/browser clocks. Fast frames must be less than 750 ms old with no future skew.
- Old code after update: change the version directory in the resource URL and reload.
- Stale data after the timed run: expected; launch another bounded run for testing.

Rollback a dedicated test deployment by removing only its dashboard and resource
through HA's APIs, then optionally removing its exact version directory. Do not restore
the entire resources file over unrelated later changes. Stop the development runner;
the original installed bridge and legacy entities continue running.

Real rendering is a functional smoke check. The blueprint's 15–30 minute benchmarks,
publish-to-display latency, 1/5/8/16-card load, target tablet, reconnect and physical
audio-source verification are separate acceptance gates. No rate SLA follows from a
short live demonstration. Slow sensors, capabilities, source-name mapping and HACS
installation remain tracked project work.

## Measurement build handover (2026-10-02)

The dedicated test resource was updated to the `voicemeeter-v2-dev-062e309`
version directory. MCP confirmed the URL; dashboard diagnostics and live validation
remain unfinished. See [MEASUREMENT-PROGRESS.md](MEASUREMENT-PROGRESS.md) for the
exact stopping point, test commands and proposed next implementation commit.
