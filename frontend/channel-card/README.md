# Voicemeeter channel card — local development

One custom element represents one selected strip or bus. This is a development workspace
inside the bridge repository, not an installed card or verified HACS distribution.
The separate HACS repository remains in scope. Nothing here creates a repository,
installs a dashboard resource, writes HA configuration or launches the Windows bridge.

## Run locally

From the bridge repository:

```powershell
node --test frontend/channel-card/test/*.test.js
python -m http.server 8765 --bind 127.0.0.1 --directory frontend/channel-card
```

Open http://127.0.0.1:8765/preview/ for the component and editor, or
http://127.0.0.1:8765/browser/checks.html for repeatable Chromium browser checks.
Control/editor checks are at http://127.0.0.1:8765/browser/control-checks.html.
The checks page uses mock HA connections and reports PASS/FAIL. The preview uses generic
fixed fixtures at 250 ms intervals. Neither page connects to HA or proves throughput.

## Card configuration

```yaml
type: custom:voicemeeter-channel-card
bridge:
  base_topic: voicemeeter/example-pc
  transport: auto
source:
  id: strip:0
  display_name: Example input
meter:
  mute_display_mode: incoming
  floor_dbfs: -90
  orientation: horizontal
appearance:
  variant: standard
controls:
  gain: true
  mute: true
  routing: true
entities:
  gain: number.example_input_gain
  mute: switch.example_input_mute
  routes:
    A1: switch.example_input_a1
    B1: switch.example_input_b1
  meters:
    pre: sensor.example_incoming_peak
    post_mute: sensor.example_after_mute_peak
```

These entity IDs are illustrative; select verified entities for the chosen source and
tap. No owner mappings are inferred. Source defaults to unassigned. Inputs accept
incoming (pre-fader) or post_mute. Buses use output only: omit mute_display_mode and map
entities.meters.output if needed. Floor accepts -120 to -20 dBFS; orientation is
horizontal/vertical and variant is compact/standard/expanded.

The visual editor edits these fields and emits config-changed. Changing the canonical
source or bridge topic clears entity overrides so readings and controls cannot keep targeting
the previous source. Changing only the tap preserves that source's other tap mappings.
Metadata supplies a display label when no override is configured. Labels use textContent.
Metadata/entity pickers and advanced-control editor sections are still pending.

## Core controls

Gain, mute and input routing are opt-in and require explicit entity mappings. Mapping an
entity asserts that it belongs to the selected source; friendly names are never used to
infer ownership. This is manual override support, not automatic capability discovery.
Unmapped controls are omitted with an editor warning. Bus cards omit strip routing.
Leave individual A1–A5/B1–B3 mappings blank to hide those buttons. Duplicate active
entity targets and wrong entity domains are rejected. Unsupported requested controls,
including solo and advanced processing, are preserved and reported as not implemented.

Gain uses number.set_value and HA-provided min/max/step within the existing bridge's
-60..12 dB range. A declared unit must be dB; an omitted unit is accepted for explicitly
mapped legacy gain entities in that range. Dragging edits a local proposed value only;
release/change sends one command. The separate numeric readback remains HA's state.
Escape restores that readback without submitting the draft. Mute and routing use explicit
switch.turn_on/turn_off, not blind toggle. Mute state is independent of meter silence.

All controls require valid HA state and available services. They work in sensors-only
mode and do not require a fast stream. Each control independently shows pending/error
state. Success requires both service completion and matching HA readback; ACK alone never
changes the displayed value. A three-second timeout reports missing confirmation. Retry
remains blocked while the original service promise is unresolved. There is no automatic
retry. Permission errors stay generic. HA state can itself be optimistic depending on its
integration; physical Voicemeeter readback still needs live verification.

Reconfiguration or hiding/removal discards local pending UI/timers and ignores old
completion callbacks; it cannot cancel a command already sent to HA. Other cards continue
to reflect HA state. The preview's gain/mute/routes use an in-memory mock with delayed
readback, never real HA actions. Gain/routes do not simulate audio processing.

## Transport and freshness

Auto prefers native HA and uses explicitly mapped slow sensors while fast data is
waiting or stale. native_ws is fast-only; entities_only never subscribes to MQTT triggers.
A fresh native frame declaring unavailable remains unavailable, without masking it with
an older sensor reading. Unsupported custom_ws is rejected.

The card's hass setter uses the existing authenticated hass.connection.subscribeMessage.
There are no embedded tokens, broker passwords or extra sockets. Visible cards share
one fast subscription and one metadata subscription per connection/base topic in the
current browser module. There is no cross-tab sharing or cached meter-frame replay.
Final release waits for setup and both unsubscribe attempts. Failed cleanup blocks a
replacement on that connection to avoid duplicate subscriptions; a new connection can
recover. Failed setup is displayed; retry currently requires remount/config change.

Validated metadata must identify Potato v2, a session, unique canonical sources and
supported taps. Frames require matching metadata, enabled sources, advertised taps and
increasing sequence numbers. New metadata permits restart at zero. Retired sessions
cannot roll back during that shared feed's lifetime; 128 retirements is a fail-closed
memory bound. Disconnect revokes metadata trust until metadata is delivered again.
This establishes consistency inside the configured trusted MQTT namespace, not
cryptographic publisher identity. Metadata has no ordered revision or signed identity.

Native frames are size-limited, reject malformed or impossible values, and require UTC
publication timestamps (Z or +00:00) less than 750 ms old. That age reduces the remaining
meter lifetime, rather than restarting freshness when the frame reaches the card.
Clock skew can reject legitimate readings. Publication age is not sample age, and the
HA trigger event does not expose retained-delivery status. The local setFrame fixture
seam bypasses metadata; production delivery uses the validated session path.

Slow sensors require finite numeric states with unit dBFS. Missing, unavailable, unknown,
wrong-unit and disconnected states display no reading. The reduced-freshness badge is
always visible in fallback mode. A conservative 15-second last_updated budget clears
old values. An unchanged sensor may therefore go stale even if the device is still
reporting; the timestamp does not establish sample age. Unrelated hass updates never
refresh it. No interpolation invents missing peaks.

## Visibility and verification limits

IntersectionObserver pauses offscreen/display-hidden cards; document visibility pauses
hidden tabs. Hidden cards release leases, clear timers and discard fast readings.
Returning cards wait for metadata and fresh frames. Visible paint requests coalesce to
the latest model state through requestAnimationFrame, capped at 30 fps, with no idle
animation loop. This is a rendering policy, not a measured tablet/HA performance result.

Node tests and the browser checks cover session changes, shared leases, cleanup,
source isolation, labels, fallback, stale timers and visibility. Actual installed HA
client compatibility, real event capture, reconnect behavior and 10/20-Hz benchmarks
with 1/5/8/16 cards still require live verification. No claim of fast HA streaming or
HACS readiness is made.

Still required: automatic capability/entity resolution, solo and advanced controls;
remaining editor sections; peak hold/decay/history; packaging/install checks;
Windows bridge runtime integration and target-device benchmarks. A post-mute silent
reading does not prove the mute control is on. Unsupported controls are not shown as working.
License uncertainty recorded in the root audit must be resolved before publication.

## Contract references

- [HA custom-card API](https://developers.home-assistant.io/docs/frontend/custom-ui/custom-card/)
- [HA WebSocket API](https://developers.home-assistant.io/docs/api/websocket/)
- [Core 2026.9.4 subscription handler](https://github.com/home-assistant/core/blob/2026.9.4/homeassistant/components/websocket_api/commands.py)
- [Core 2026.9.4 MQTT trigger](https://github.com/home-assistant/core/blob/2026.9.4/homeassistant/components/mqtt/trigger.py)
- [HA JS event unwrapping](https://github.com/home-assistant/home-assistant-js-websocket/blob/master/lib/connection.ts)
- [HA state types](https://github.com/home-assistant/home-assistant-js-websocket/blob/master/lib/types.ts)
- [HA frontend service calls and state](https://developers.home-assistant.io/docs/frontend/data/)
- [HA number actions](https://www.home-assistant.io/integrations/number/)
- [HA switch actions](https://www.home-assistant.io/integrations/switch/)

The Core handler requires admin permission. JS master sources were inspected on
2026-10-01; the installed client version and event capture remain unverified.
