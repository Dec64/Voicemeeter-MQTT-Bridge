# V2 application runtime

The development bridge now starts MeterTelemetryRuntime when meteringV2.enabled is
true. Defaults remain disabled. This code has not replaced the installed bridge and
has not published to the owner's broker.

## Startup and recovery

1. Register the Remote API client. All native calls, including identity, share one
   RemoteApiOwner thread.
2. Connect MQTT, subscribe legacy command/HA status topics, and complete legacy setup.
   Only the current connection generation may authorize v2 startup.
3. Validate a private v2 settings snapshot and read a Potato 3.x identity of at least
   3.1.0.1. A different engine or failed identity read prevents startup. Configured
   labels or generic names are used; native Unicode labels remain unimplemented.
4. Start the existing supervisor: retained status/metadata, optional slow discovery,
   then independently scheduled fast and slow sampling/publication.
5. Validate engine identity around every sample pass. Detected changes stop the
   session. Failures retry after five seconds while the bridge remains running.

Disconnect or manual reconnect revokes readiness and cancels/drains the session before
reconnecting. Each replacement has a fresh session ID and republishes metadata. HA
online announcements schedule a coalesced refresh outside the MQTT receive callback,
so QoS 1 sends can receive acknowledgements. Legacy discovery is refreshed when enabled;
the replacement v2 session republishes its metadata and optional slow discovery.

Shutdown cancels telemetry, waits for actual sends/native calls, publishes terminal
status when possible, then disconnects MQTT and logs out. Cancellation is not proof
that a send completed; an uncooperative send can delay shutdown. Connection retries
and setup waiters are canceled when the bridge stops.

## Configuration example — not applied

This is only the v2 fragment for an isolated development configuration. It is not an
owner source profile. Existing credentials, topics, settings and installed files are
unchanged. V2 settings are captured at runtime startup; editing them requires an
application restart. The legacy settings dialog does not provide v2 controls.

```json
{
  "meteringV2": {
    "enabled": true,
    "fastEnabled": true,
    "slowEnabled": false,
    "sampleIntervalMs": 50,
    "fastPublishIntervalMs": 50,
    "sources": [
      { "id": "strip:0", "enabled": true, "displayLabel": "Development input" }
    ]
  }
}
```

An enabled slow stream publishes new sensor discovery only when the existing global
homeAssistantDiscovery setting is true. It does not retire old retained discovery.
Legacy meter publication/discovery still follows the existing top-level PublishMeters
settings; the v2 legacyMeters fields are not yet the runtime authority.

Do not launch the development tray bridge beside the installed copy against the same
broker identity. Prepare a separate client ID/topic/config and backup/rollback procedure
before an approved run. This batch does not add an isolated tray-app config-path option.

## Verification and remaining work

The application startup tests drive the real bridge/runtime/supervisor/publisher code
using fake native and MQTT boundaries. They prove metadata-before-frame ordering,
session replacement, readiness generations, default-off behavior and drain-before-logout.
They do not prove actual broker reconnection timing or HA throughput.

The separate read-only probe previously verified the installed Potato 3.1.3.0 engine
and returned 480 available readings. HA MCP still sees 16 available legacy meters.
No v2 broker delivery, HA native WebSocket capture or 10/20 Hz benchmark has been run.
The browser preview remains a mock. One reusable modular card, HACS packaging, advanced
capabilities, real source mapping and physical audio acceptance remain required.

Type/version checks cannot detect a same-version engine restart wholly between checks
or make sequential channel reads atomic. A matching installed SDK header/manual and
Unicode label bindings remain unresolved; this runtime adds no native export signatures.

Next commit: an isolated development launch/configuration path and an explicit dry-run
summary of topics/client identity, so the first broker test can be reviewed before launch.
After approval, capture real metadata/frames in HA and benchmark the native transport.
