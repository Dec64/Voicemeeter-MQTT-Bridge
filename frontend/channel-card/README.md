# Voicemeeter channel card — local development

One custom element represents one selected strip or bus. This folder is a development
workspace in the bridge repository, **not a verified HACS package or installed HA card**.
No repository, dashboard resource, broker connection or entity is created by the preview.
The separate HACS repository/distribution remains part of the blueprint.

From the bridge repository:

```powershell
node --test frontend/channel-card/test/*.test.js
python -m http.server 8765 --bind 127.0.0.1 --directory frontend/channel-card
```

Open `http://127.0.0.1:8765/preview/`. The synthetic examples deliberately use generic
names, not guessed owner source assignments. Fixed fixture values repeat every 250 ms;
there is no random audio motion and this is not a performance benchmark.

Implemented: canonical source selection, per-instance input tap, truthful bus output,
numeric combined peak, calibrated persistent meter shell, light/dark theme variables,
silence/unavailable/stale states, and configuration/model validation. Each card keeps
its own level and sequence state. User labels enter textContent, never executable HTML.
Configuration uses `source.id`, optional `source.display_name`,
`meter.mute_display_mode` (`incoming` or `post_mute` for strips only), and optional
`meter.floor_dbfs` (-120 to -20). Defaults select no source.

The local `setFrame` seam accepts decoded v2-shaped fixtures. It rejects duplicate/old
sequences and a different session until explicitly reset. Receipt freshness expires
at 750 ms. It does **not** prove wall-clock publication freshness, metadata identity,
native HA event shape, session handover or transport authorization. No real transport
should feed it without those checks. A post-mute silent reading does not prove a mute
control is on; actual HA state readback is still needed.

The visual editor is available through `getConfigElement()` and the preview's
"Edit the first card" section. It edits manual source, label, topic, tap and floor,
emits HA-style `config-changed` events, and preserves unrelated configuration. Bus
selection removes the incompatible input tap. Invalid configuration is explained;
metadata/entity suggestions and control-specific sections are not implemented yet.

Still required: remaining editor sections, authenticated HA subscription integration,
source metadata/session validation, ordinary sensor fallback, capability-aware controls,
readback/pending/error states, peak hold/decay/history, visibility-aware rendering,
HACS build/install validation and live performance measurements. No controls are shown
until they can perform real actions. The three-card preview is an example arrangement,
not a fixed mixer component.

Custom-element configuration/sizing follows the [official HA custom-card API](https://developers.home-assistant.io/docs/frontend/custom-ui/custom-card/).
License and attribution remain governed by the bridge repository; resolve its recorded
license discrepancy before publishing a separate card repository.

Layout: `meter.orientation` accepts horizontal/vertical; `appearance.variant` accepts
compact/standard/expanded. The editor exposes both. Vertical fill rises from the
bottom and has a matching scale and accessibility orientation. No animation or peak
hold is implied by a layout change. Grid height is left automatic for wrapping labels.

Shared feed registry: SharedTelemetry keeps one subscription per connection object and
literal topic in a browser module, reference-counted through leases. Final release
awaits pending setup and unsubscribe; replacement setup waits for old cleanup. Failed
cleanup blocks replacement to avoid duplicates. Setup failure can retry after all leases
release; a new connection has its own registry. Closed-generation callbacks are ignored.
Frames are cloned/frozen once before fan-out, so one consumer cannot alter sibling data.
No stale frame cache or cross-tab sharing is claimed. The local preview now uses this
same registry with a fixture connection; real HA subscription is not wired yet.

Native HA transport prototype: `native-ha-transport.js` exports a shared registry using
the supplied existing `hass.connection.subscribeMessage`. It creates no socket or
credentials. Mock tests verify a `subscribe_trigger` MQTT request, exact-topic event
decoding, permission failures and cleanup. Wildcards and HA template delimiters are
rejected. Payloads over 65,536 characters, malformed envelopes and impossible levels
are dropped. Publication timestamps must be UTC and less than 750 ms old, with no
future timestamp allowance; clock skew can therefore reject legitimate readings.

This adapter is deliberately **not connected to the card yet**. Metadata identity and
session handover must be implemented before enabling it. Publication age is not sample
age, retained delivery cannot be identified from this event shape, and first-session
trust is not solved by parsing. Sequence/session rejection still belongs to the model.
Automatic reconnect behavior belongs to HA's client and has not been exercised live.

Contract references inspected: [HA WebSocket API](https://developers.home-assistant.io/docs/api/websocket/),
[Core 2026.9.4 subscription handler](https://github.com/home-assistant/core/blob/2026.9.4/homeassistant/components/websocket_api/commands.py),
[Core 2026.9.4 MQTT trigger](https://github.com/home-assistant/core/blob/2026.9.4/homeassistant/components/mqtt/trigger.py),
and [JS client's event unwrapping](https://github.com/home-assistant/home-assistant-js-websocket/blob/master/lib/connection.ts).
The Core handler requires admin permission. Client source was inspected on 2026-10-01
on its moving master branch; the installed client version and actual HA event capture
remain unverified. These tests establish a source-based prototype contract, not live
compatibility, performance or HACS readiness.
