# Voicemeeter channel card

Card version **2.0.0-rc.5**, compatible with Windows bridge **2.0.0-rc.1**. Adds rotary audio controls, compressor/gate setting previews and an interactive six-band parametric EQ graph. Standard uses compact drawers; Expanded opens a larger processing rack. Meter-only presentation supports a minimal or hidden name. Versioned companion modules prevent a normal HACS reload from mixing old and new code.

<img src="assets/hacs-icon.png" width="160" alt="Audio levels bridged between a PC and a home">

**New to MQTT or Voicemeeter? Read the [complete plain-language setup guide](USER-GUIDE.md).** It explains both installations, every card/bridge option, all processing parameters, glossary, updates and troubleshooting.

A Home Assistant card for one Voicemeeter Potato strip or output bus. Add independent cards for the sources you need. Incoming and after-mute input taps, output meters, peak hold, measured history and optional controls share one native HA subscription per bridge and browser connection.

Requires the Voicemeeter MQTT Bridge v2, MQTT integration and Home Assistant 2026.9 or later. Tested with Core 2026.9.4. This release candidate is unsigned and still requires device acceptance testing.

## Install

Add `https://github.com/Dec64/voicemeeter-channel-card` as a HACS custom repository of type Dashboard. Download the card and reload the browser. Select the RC version if HACS asks for a prerelease version. HACS registers `voicemeeter-channel-card.js` from `dist`; keep every accompanying JavaScript module. The Windows bridge is installed separately; this card is not an HA backend integration.

For a manual installation, copy every file from `dist` into `/config/www/voicemeeter-channel-card/`. Add `/local/voicemeeter-channel-card/voicemeeter-channel-card.js` as a JavaScript module resource. Reload the browser.

## Configure

```yaml
type: custom:voicemeeter-channel-card
bridge:
  base_topic: voicemeeter/example-pc
  transport: auto
source:
  id: strip:0
meter:
  mute_display_mode: incoming
  peak_hold_ms: 1500
  show_history: true
  history_seconds: 5
appearance:
  variant: compact
```

Choose `strip:0` through `strip:7` or `bus:0` through `bus:7` in the visual editor. Bus cards omit `mute_display_mode`. Labels come from bridge metadata; a display name overrides the label without changing the source identity.

`auto` uses the authenticated HA WebSocket connection and falls back to explicitly mapped slow sensors. `native_ws` requires the native stream. `entities_only` uses mapped HA sensors and does not animate an invented fast stream. Missing, stale or disconnected readings show a status and no value.

Core controls are opt-in: `gain`, `mute`, `solo` and `routing`. Supported processing groups are `mono`, `compressor`, `gate`, `denoiser`, `eq` and `eq_cells`. Enable their discovery in the bridge first, then use **Suggest entities from bridge metadata** in the editor. Suggestions resolve actual registry entries by stable MQTT unique ID, including renamed entities. Explicit entity mappings remain available in YAML. Only supported controls with valid HA readback can send commands. Virtual inputs do not expose physical-input compression, gate, denoiser or parametric cells.

```yaml
controls:
  gain: true
  mute: true
  compressor: true
entities:
  gain: number.your_actual_gain_entity
  mute: switch.your_actual_mute_entity
  advanced:
    strip_0_comp_threshold: number.your_actual_compressor_threshold
```

Gain and numeric processing commands send on release or change. A service acknowledgement does not replace the observed state. Failed or timed-out commands retain the actual readback. Bus cards have no strip routing or solo.

Layout supports horizontal/vertical meters and compact/standard/expanded density. Peak attack is immediate; decay and hold use monotonic time. Reduced-motion mode displays measured values. History contains accepted measurements for the selected source/tap only. Meter painting stops when hidden.

Set `diagnostics: true` to expose bounded developer timing reports. Receipt-to-DOM timing is not physical display latency; UTC publication estimates include clock skew. Disable diagnostics for ordinary dashboards.

## License

GNU GPL v3 or later. The bridge originated with Richard Cornwell; retain LICENSE, COPYING, COPYRIGHT.txt and NOTICE.txt when redistributing. See those files for attribution.
