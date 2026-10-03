# Voicemeeter in Home Assistant: complete user guide

The Windows bridge reads Voicemeeter Potato and sends its levels and controls to Home Assistant. The channel card shows one input or output. Put several cards on a dashboard to build your own mixer.

There are two installations: the **bridge on the Windows PC** and the **card in Home Assistant through HACS**. Installing the card alone cannot connect to your PC.

This is version 2.0.0-rc.1, a release candidate. Desktop 10 Hz metering has a completed 15-minute measurement. Physical audio assignments, the full load/recovery matrix and Fire tablet acceptance still need owner testing. Installers are unsigned.

## Before you start

- Windows 64-bit with Voicemeeter **Potato** installed and working. Banana and Standard are not qualified by this version.
- Home Assistant 2026.9 or later, with its MQTT integration configured.
- An MQTT broker, usually the Mosquitto Broker add-on. A broker is the message relay between Windows and Home Assistant.
- HACS installed in Home Assistant. HACS installs community dashboard cards; it does not install the Windows bridge.
- The broker address, port and login. Use a broker account intended for the bridge. Keep its password out of screenshots, GitHub issues and card YAML.

## 1. Install the Windows bridge

Download the installer from the bridge repository's Releases page. Exit the existing bridge using its tray menu before upgrading. Back up your settings and old executable first; the backup instructions are below. Run the installer and start **Voicemeeter MQTT Bridge** from the Start menu.

Right-click its notification-area icon and choose Settings. Enter the broker address, port, username and password. `127.0.0.1` means this Windows PC; it is wrong when Mosquitto runs on a separate HA machine. The usual port is `1883`. The current bridge settings do not provide a TLS connection option; do not expose the broker to the public internet.

Leave the base topic as `voicemeeter/{computer}` unless you already use another topic. `{computer}` becomes the sanitized PC name. The card needs the expanded literal topic, for example `voicemeeter/my-pc`, not the text `{computer}`. Check the actual topic in bridge diagnostics or MQTT metadata.

Enable Home Assistant discovery. Discovery tells HA which switches, number controls and sensors exist. Save and restart the bridge. Check HA Settings -> Devices & services -> MQTT for the Voicemeeter device. Confirm the bridge reports MQTT and Voicemeeter connected.

## 2. Enable v2 meters and choose sources

Open **Advanced metering and source mapping** in bridge Settings. Enable v2 telemetry, Fast MQTT stream and Slow HA sensors. Start with Sample `50` ms, Fast `100` ms and Slow `1000` ms. This samples 20 times per second but sends the fast stream 10 times per second. Use Fast `50` ms for a requested 20 Hz trial after testing the device's load; a requested rate is not a guaranteed observed rate.

Select only the sources you need. **Read engine labels** reads names from Voicemeeter. Play or speak into one source at a time and use **Test source** to confirm its meter. That button reads levels; it does not play a test sound or alter routing. Labels alone cannot prove which physical cable carries sound.

| Source ID | Voicemeeter position |
|---|---|
| `strip:0` to `strip:4` | Physical inputs 1 to 5 |
| `strip:5` | First virtual input, usually VAIO |
| `strip:6` | Second virtual input, usually AUX |
| `strip:7` | Third virtual input, usually VAIO3 |
| `bus:0` to `bus:4` | Hardware output buses A1 to A5 |
| `bus:5` to `bus:7` | Virtual output buses B1 to B3 |

A **strip** is an input channel. A **bus** collects routed inputs for an output. IDs start at zero and remain stable when you rename a channel. A display label is a friendly name. An alias is an optional unique identifier, up to 64 letters/numbers/underscores/hyphens; it does not replace the canonical ID used by the card.

Choose **Apply to settings**, then **Save** in the main Settings dialog and restart the bridge. Apply edits a draft; it is not the final disk save. Cancel discards the advanced dialog's draft. **Reset v2 only** resets metering preferences while retaining broker settings. **Export metering** exports metering settings without broker credentials; review friendly labels before sharing it.

## 3. Install the card through HACS

Open HACS -> its three-dot menu -> Custom repositories. Add the published `voicemeeter-channel-card` GitHub URL and choose **Dashboard**. Open the card repository in HACS and Download. For a prerelease, choose/show beta versions or select the published RC version when offered. Refresh the HA page after installation.

Check Settings -> Dashboards -> Resources (enable Advanced Mode in your profile if this is hidden). The resource should be a JavaScript module at `/hacsfiles/voicemeeter-channel-card/voicemeeter-channel-card.js`. HACS should register it; add it only if missing. Do not load both a development/manual copy and the HACS copy of this card. All companion `.js` files must remain beside the entry file.

If the card is missing from the picker, refresh the browser or clear its cache. Do not restart all of HA merely to refresh a dashboard card.

## 4. Make a mixer dashboard

Go to Settings -> Dashboards -> Add dashboard. Choose a user-controlled dashboard, name it **Voicemeeter**, show it in the sidebar and use `mdi:audio-input-stereo-minijack` as its icon. An MDI icon is a theme-aware menu symbol; the repository artwork is a separate image and is not automatically a sidebar icon.

Edit the dashboard -> Add card -> Voicemeeter channel card. Enter the literal bridge base topic and choose a source. Add separate cards for the microphone, other inputs and outputs you want. Use a sections dashboard or grid for clean alignment. Compact density works well for a mixer overview; standard/expanded provides more space for controls.

Start with this meter-only card in the YAML editor if needed:

```yaml
type: custom:voicemeeter-channel-card
bridge:
  base_topic: voicemeeter/my-pc
  transport: auto
source:
  id: strip:0
  display_name: Microphone
meter:
  mute_display_mode: incoming
  floor_dbfs: -90
  orientation: horizontal
  peak_hold_ms: 1500
  show_history: true
  history_seconds: 5
appearance:
  variant: compact
```

Replace `voicemeeter/my-pc` with your actual topic. For a bus use `source.id: bus:0` and omit `mute_display_mode`; buses measure their output.

## 5. Add controls safely

Enable the relevant discovery switches in the bridge first. In the card editor, select gain/mute/solo/routing or a supported processing group. Click **Suggest entities from bridge metadata**. Suggestions match actual MQTT unique IDs, including HA entities you renamed. Confirm they belong to this source. Changing source or topic clears mappings to prevent controlling the previous channel.

An **entity** is HA's name for one sensor or control, such as `number.microphone_gain`. A **unique ID** is its underlying identity; it survives an entity rename. The card uses actual entity IDs to send HA services. It does not invent entity IDs from a label.

Gain adjusts volume. Mute silences a channel. Solo lets you focus on an input according to Voicemeeter's own solo behavior. Routing switches A1-A5/B1-B3 choose destinations for an input. Bus cards have no input routing or solo.

Commands change the real mixer and can affect what people hear. Test at a comfortable listening level. The card waits for actual readback rather than treating a sent request as success. Unavailable or wrongly mapped controls cannot send a valid command. Numeric controls send on change/release rather than continuously during every pointer movement.

If configuring manually, substitute your **actual** entities:

```yaml
controls:
  gain: true
  mute: true
  solo: false
  routing: true
entities:
  gain: number.your_microphone_gain
  mute: switch.your_microphone_mute
  routes:
    A1: switch.your_microphone_a1
    B1: switch.your_microphone_b1
```

Leaving an individual route blank hides it. Every control must have its own entity. Do not use these example names unchanged.

## Every card option

Options are grouped YAML fields. The visual editor covers ordinary setup; YAML is useful for explicit mappings and diagnostics. Control visibility defaults to false. Meter visibility defaults are listed below.

| Option | Default / choices | Meaning |
|---|---|---|
| `type` | `custom:voicemeeter-channel-card` | Identifies this card; required. |
| `bridge.base_topic` | Empty until set | Literal expanded MQTT topic; no wildcards, templates, trailing slash or surrounding spaces. |
| `bridge.transport` | `auto` | `auto` prefers native streaming and can use mapped slow sensors; `native_ws` requires streaming; `entities_only` uses mapped sensors. |
| `source.id` | Empty until selected | One stable `strip:0`-`strip:7` or `bus:0`-`bus:7`. |
| `source.display_name` | Source/metadata name | Optional friendly card title, at most 511 characters. |
| `meter.mute_display_mode` | `incoming` | Inputs: `incoming` is before the fader; `post_mute` is after mute. Omit for buses. |
| `meter.floor_dbfs` | `-90`, range -120 to -20 | Quietest displayed level; changes the drawing, not the audio. |
| `meter.ceiling_dbfs` | `12`, range 0 to +24 | Top of the visual scale. Real peaks above 0 fill the red headroom. This does not boost or limit the audio. |
| `meter.show_peak_value` | `true` | Show the numeric peak dBFS reading in the header. |
| `meter.show_scale` | `true` | Show the meter's level labels. |
| `meter.show_status` | `true` | Show the signal/silence/unavailable dot and text. |
| `meter.show_peak_hold` | `true` | Show the held peak marker. |
| `meter.show_clip` | `true` | Show the short CLIP indication after an observed peak reaches the near-full-scale threshold (-0.1 dBFS). |
| `meter.colors` | See example below | Hex colours for `normal`, `warning`, `clip`, `track` and `peak`. |
| `meter.orientation` | `horizontal` | `horizontal` or `vertical`. |
| `meter.peak_hold_ms` | `1500`, range 0-5000 | How long the peak marker holds before it falls; zero disables the hold. |
| `meter.show_history` | `false` | Show a small graph of recently accepted real measurements. |
| `meter.history_seconds` | `5`, range 3-5 | Length of card history in seconds. |
| `appearance.variant` | `standard` | `compact`: smallest channel strip. `standard`: smaller meters and knobs, with closed processing drawers. `expanded`: larger meters, knobs and graphs, with processing and routing open initially. You can still close a panel. |
| `appearance.presentation` | `channel` | `channel` shows the enabled mixer controls. `meter` shows only the meter, name and chosen meter details; saved control mappings are retained but cannot send commands in this mode. |
| `appearance.name_style` | `full` | `full`: heading. `minimal`: small caption and smaller numeric peak. `hidden`: no name. A blank display name still uses the mixer's source label; use `hidden` to remove it. |
| `appearance.show_source_id` | `false` | Show the technical strip/bus ID above the title. The ID is also in the card tooltip. |
| `appearance.show_tap` | `false` | Show incoming/after-mute/output text in the footer. Also available in the card tooltip. |
| `appearance.colors` | See example below | Hex colours for the control `accent`, active `mute` and active `solo`. |
| `controls.gain` | `false` | Show mapped volume number control. |
| `controls.mute` | `false` | Show mapped mute switch. |
| `controls.solo` | `false` | Show mapped solo switch for an input. |
| `controls.routing` | `false` | Show mapped input destination switches. |
| `controls.mono` | `false` | Show supported mono processing. |
| `controls.compressor` | `false` | Show supported physical-input compression. |
| `controls.gate` | `false` | Show supported physical-input noise gate. |
| `controls.denoiser` | `false` | Show supported physical-input denoiser. |
| `controls.eq` | `false` | Show supported EQ switches/bands. |
| `controls.eq_cells` | `false` | Show supported parametric EQ cells; can create many entities. |
| `entities.gain` | Unset | Actual HA `number.*` entity. |
| `entities.mute`, `entities.solo` | Unset | Actual HA `switch.*` entities. |
| `entities.routes` | Empty | Map A1-A5/B1-B3 to actual `switch.*` entities. |
| `entities.advanced` | Empty | Map canonical advanced control IDs to actual `number.*` or `switch.*`; prefer the suggestion button. |
| `entities.meters` | Empty | Optional fallback sensor mappings: `pre`, `post_mute`, `output`. Use real `sensor.*` entities reporting dBFS. |
| `diagnostics` | `false` | Show developer timing controls/reports. Leave off for ordinary use. |

The native connection is HA's authenticated WebSocket connection, not a browser connection directly to the MQTT broker. Cards in one browser share a subscription. Meters stop painting when hidden. Old/disconnected readings show their status instead of a believable frozen live value. Slow sensors expire after 15 seconds; they do not supply smooth fast animation or fabricated history.

### Meter appearance and mixer controls

The segmented meter attacks immediately and falls smoothly when the sound gets quieter, including when the status changes to Silence. Stale or unavailable data clears the fill. The meter is a combined channel peak meter: its bars are segments of one measured level, not frequency bands. With the operating system's reduced-motion preference enabled, the card shows each measured value immediately.

Mute, Solo, Mono and routing buttons light up when the mixer reports them active. Mute uses red, Solo uses amber, and other active controls use the accent colour. There is no extra On/Off text. An applying indicator and tooltips explain pending changes; the button lights only after the mixer confirms its state. Errors remain visible so a failed action cannot appear successful.

Channel gain uses a fader plus an exact number. Compressor, gate, denoiser and EQ parameters use rotary knobs with editable values below. Drag a knob upwards to increase it, downwards to decrease it. Hold Shift for finer movement. With a knob focused, arrow keys change one advertised step, Page Up/Down change ten steps, and Home/End reach the limits. Escape cancels a drag. Frequency, timing and Q knobs use a logarithmic sweep so low values have useful adjustment space. Enter an exact value underneath when needed. Dragging previews the value; releasing applies it. The control then waits for actual mixer readback.

The compressor includes a setting preview of its input-to-output curve, including threshold, ratio, knee and manual input/output gains. The gate shows the attack/hold/release timing envelope. These are illustrations of the settings, not live gain-reduction or gate-activity meters. The denoiser exposes the native strength and noise-floor controls that Voicemeeter actually supplies.

Parametric EQ has a 20 Hz to 20 kHz response graph, six coloured band selectors per supported channel and a channel menu. Drag a numbered graph handle sideways to change frequency; drag vertically to change gain for Bell, Low shelf and High shelf filters. Other filters use the horizontal position and Q. Arrow Left/Right adjust frequency by a semitone; Up/Down adjust gain by 1 dB for bell/shelf filters. Hold Shift for fine steps. Use the band's Q knob to set its width and the Enabled button to bypass that band. Each command targets only the selected channel and band. Filter type uses named choices: Bell / parametric, Notch, Band pass, Low pass, High pass, Low shelf or High shelf. EQ memory uses A/B buttons.

The solid EQ curve combines enabled bands; the dashed curve shows the selected band. This is a setting preview based on standard [biquad filter equations](https://www.w3.org/TR/audio-eq-cookbook/) at an illustrative 48 kHz sample rate, not a measured analyzer or a guarantee of Voicemeeter's exact internal response. It does not include the master EQ bypass. Numeric values and switches always use real mixer readback.

For a meter on its own, select **Presentation → Meter only** and **Display name style → Minimal caption** in the visual editor. Choose Hidden instead for no caption. Switch off peak number, status, scale and clip text individually for a bare meter. Changing presentation does not erase your mixer mappings. Example:

```yaml
type: custom:voicemeeter-channel-card
bridge:
  base_topic: voicemeeter/your-pc
source:
  id: strip:0
  display_name: MIC
appearance:
  presentation: meter
  name_style: minimal
  variant: compact
meter:
  show_peak_value: false
  show_status: false
  show_scale: false
  show_clip: false
```

For **Show parametric EQ cells** to work, first enable `eq_cells` in the Windows bridge's advanced discovery groups, save and restart the bridge. In the card editor, select the source, enable the group and press **Suggest entities**. The editor matches actual published controls by their stable IDs. Virtual inputs have no parametric cells. Enabling a card checkbox alone cannot create bridge entities.

This example customizes an existing card; keep its own `type`, source, bridge and entity mappings:

```yaml
meter:
  floor_dbfs: -90
  ceiling_dbfs: 12
  show_peak_value: false
  show_scale: true
  show_status: true
  show_peak_hold: true
  show_clip: true
  colors:
    normal: '#78ff8e'
    warning: '#ffd466'
    clip: '#ff5656'
    track: '#07130c'
    peak: '#edffdf'
appearance:
  show_source_id: false
  show_tap: false
  colors:
    accent: '#78ff8e'
    mute: '#ff756b'
    solo: '#ffd466'
```

Colours accept three- or six-digit hex values. Quote them in YAML because an unquoted `#` starts a comment. The visual editor includes colour pickers and all these visibility settings. A +12 scale does not imply every source will reach +12; the card displays the real value supplied by Voicemeeter.

## Every Windows setting

These are the exact JSON names in the private settings file. Most are shown in Settings. Advanced tuning fields without a GUI must be edited only after exiting the bridge and backing up the file. A positive interval is milliseconds; 1000 ms is one second. Preserve valid JSON (double quotes, commas, no comments).

| Setting | Initial default | Meaning |
|---|---|---|
| `mqttHost` | `127.0.0.1` | Broker hostname/IP; use the HA/broker machine's address. |
| `mqttPort` | `1883` | Broker port. |
| `mqttUsername`, `mqttPassword` | Empty | Broker login; private. |
| `clientId` | `voicemeeter-{computer}` | Broker connection identity; each simultaneous bridge needs its own. |
| `baseTopic` | `voicemeeter/{computer}` | Root address for messages. |
| `homeAssistantDiscovery` | `true` | Publish HA entity definitions. |
| `homeAssistantDiscoveryPrefix` | `homeassistant` | Must match HA's configured discovery prefix. |
| `publishDiscoveryOnConnect` | `true` | Republish definitions on MQTT connect. |
| `startPotatoWithApp` | `true` | Start Potato when the bridge starts. |
| `pollIntervalMs` | `250` | Ordinary control-state polling interval. |
| `controlReconcileIntervalMs` | `30000` | Periodic full control-state reconciliation. |
| `publishMeters` | `true` | Legacy meter publishing when v2 is disabled. |
| `publishMetersEveryMs` | `1000` | Legacy cadence when v2 is disabled. |
| `publishAllMappedControls` | `true` | Publish all mapped control state, rather than limiting that state coverage. |
| `enableStripRoutingDiscovery` | `true` | Create input routing entities. |
| `enableStripGainDiscovery` | `true` | Create input gain entities. |
| `enableBusDiscovery` | `true` | Create mapped output bus entities. |
| `enableRecorderDiscovery` | `true` | Create mapped recorder entities. |
| Start with Windows | Off unless enabled | Tray preference registering Windows startup; not a metering JSON field. |

V2 fields below live inside the `meteringV2` object. Older settings load with v2 disabled and inherit the old legacy meter choices. Unknown settings fields survive saves.

| V2 setting | Default / valid range | Meaning |
|---|---|---|
| `enabled` | `false` | Master v2 switch. |
| `sampleIntervalMs` | 50 / 10-1000 | How often the bridge reads native peaks. |
| `fastEnabled` | `false` | Send fast aggregate meter messages. |
| `fastPublishIntervalMs` | 50 / 50-5000 | Fast message interval, at least the sample interval. |
| `slowEnabled` | `true` | Send slow aggregate sensor updates. |
| `slowPublishIntervalMs` | 1000 / 250-60000 | Slow interval, at least the sample interval. |
| `legacyMetersEnabled` | Inherited / otherwise true | Keep old raw meter topics while v2 is enabled. |
| `legacyMetersIntervalMs` | Inherited / otherwise 1000 | Positive legacy interval while v2 is enabled. |
| `displayFloorDbfs` | -90 | Finite negative floor for reported silence. |
| `activityThresholdDbfs` | -48 | Activity starts above this; above floor and at most 0. |
| `activityHysteresisDb` | 3 | Activity clears below threshold minus this margin; release must not fall below floor. |
| `activityHoldMs` | 250 / 0-60000 | Keep activity indication briefly after a signal drops. |
| `clipThresholdDbfs` | -0.1 | Clip threshold; above activity threshold and at most 0. |
| `clipHysteresisDb` | 0.5 | Margin before clearing clipping; release remains above floor. |
| `clipHoldMs` | 2000 / 0-60000 | Keep clip status visible after a peak. |
| `historySeconds` | 5 / 1-60 | Bridge-side history window; independent of card's 3-5 second graph. |
| `advancedDiscoveryGroups` | Empty | Any distinct subset of mono/compressor/gate/denoiser/eq/eq_cells. |
| `sources` | Empty | Up to 16 source profiles; only selected enabled sources participate. |

Each source profile contains `id` (canonical ID), `enabled` (include it), `displayLabel` (optional friendly name, up to 511 characters without control characters), `alias` (optional unique case-insensitive alias) and `meterTaps` (optional list of distinct taps). IDs and nonempty aliases must be unique. Default inputs expose `pre` and `post_mute`; buses expose `output`. Expert JSON may select input `post_fader`. The card currently selects incoming or after mute, not a post-fader view. A tap means the point in the mixer where the level is measured.

Do not enable discovery in a second development bridge on the same PC alongside production: discovery IDs include the PC identity, so different topics/client IDs alone do not prevent entity-definition collisions.

## Processing terms and parameters

Advanced settings are exposed only when supported by native readback and explicitly enabled. Virtual inputs do not expose physical compressor/gate/denoiser or parametric cells. Unsupported parameters are hidden. HA supplies each available numeric control's range and step; the bridge also enforces vendor limits.

| Term / control | Plain meaning |
|---|---|
| Mono | Combine the channel for mono processing. |
| Compressor / Compression | Reduce loud portions to narrow the volume difference. Native main knob range 0-10. |
| Input/output gain | Level before/after compressor (-24 to +24 dB). |
| Ratio | Strength of compression above threshold (1-8). |
| Threshold | Level where compression starts (-40 to -3 dB). |
| Attack / release | How quickly compression begins (0-200 ms) / relaxes (0-5000 ms). |
| Knee | Softness of transition into compression (0-1). |
| Automatic makeup | Let Voicemeeter compensate for compression's level reduction. |
| Gate | Reduce a physical input when it is quiet; main knob 0-10. |
| Gate threshold | Level deciding when gate opens (-60 to -10 dB). |
| Maximum damping | Gate's configured attenuation (-60 to -10 dB). |
| Sidechain band pass | Frequency focus for gate detection (100-4000 Hz); not the audible output EQ. |
| Gate attack/hold/release | Opening time (0-1000 ms), time held (0-5000 ms), closing time (0-5000 ms). |
| Denoiser / noise floor threshold | Native noise-reduction controls, both 0-10 vendor units; not a dB or percentage scale. |
| EQ | Equalizer: adjusts frequency balance. Virtual EQ band gains range -12 to +12 dB. |
| EQ enabled / memory B | Turn supported parametric EQ on; select its stored A/B setting. |
| EQ cell | One filter within one EQ channel. Up to eight channels and six cells each. |
| Cell enabled | Turn that filter on/off. |
| Cell type | Voicemeeter's numeric filter type (0-6); see its manual for filter shapes. |
| Cell frequency | Centre/cutoff frequency (20-20000 Hz). |
| Cell gain | Boost/cut (-12 to +12 dB). |
| Cell Q (quality) | Width/sharpness of filter (1-100); higher usually narrows it. |

Enabling `eq_cells` may add 240 controls per supported source. Start with the few groups you need. The controls panel is created when expanded to avoid filling every card with hundreds of fields.

## Meter and connection terms

**dB** is a relative level difference. **dBFS** measures level relative to digital full scale: 0 is the reference ceiling, negative values are quieter. It is not room loudness in decibels. **Peak** is the largest measured channel level, not average loudness. **Clipping** warns about near-full-scale peaks, not a guarantee that every distortion is detected. A silence indication means level at/below the display floor; unavailable means no valid measurement.

**Pre-fader/incoming** sees audio before volume adjustment, so it can move while the input is muted. **Post-mute** sees after mute. **Post-fader** sees after volume adjustment. **Output** sees a bus's mixed output. Meter display choices do not alter any audio path.

**Hz** means updates per second (100 ms approximately 10 Hz, 50 ms approximately 20 Hz). **Telemetry** means readings/status sent by the bridge. **MQTT topic** means message address. **Retained** means the broker remembers a last message; old retained data does not qualify as a fresh live reading. **Session** identifies a bridge run. **Readback** means the observed actual state after a command. **Hysteresis** means using a separate release threshold to prevent a status flickering near its boundary.

**WebSocket/native_ws** is the live connection through HA. **Fallback** means using slower mapped HA sensors when streaming is unavailable. **Stale** means too old to trust as current. **p95** means 95 percent of accepted measurements fall at/below a reported timing. **DOM** means the browser's page structure; a DOM timing is not proof of when pixels physically lit up. **RC** means release candidate, still awaiting acceptance.

## Updates, backup and rollback

Settings/logs are in `%APPDATA%\Voicemeeter MQTT Bridge`, not Program Files. Use the tray's **Open Config Folder**. Copy `appsettings.json` to a private backup and retain the old installed executable/installer before updating. Settings may contain your broker password. Do not publish the backup. Successful saves keep a prior `appsettings.json.bak` through an atomic replacement.

Update the card in HACS, then refresh browsers. Keep one resource URL. Update the Windows bridge separately after exiting it. To roll back, exit the new bridge, restore the private prior settings and executable, and restart. For the card, select the old version in HACS and refresh. For dashboards restore only that dashboard/resource, not all HA storage. Turning off discovery does not automatically erase previously retained definitions; retire obsolete entities deliberately after checking automations.

## Troubleshooting

| Symptom | Check |
|---|---|
| MQTT disconnected | Broker address/port/login, broker running, Windows network access, unique client ID. |
| Voicemeeter disconnected | Potato installed/running, matching Remote DLL available; bridge loads the locally installed vendor DLL. |
| No source cards/readings | V2 and fast enabled, source enabled, exact expanded topic, HA MQTT integration connected. |
| No entities | Discovery enabled, correct prefix; save/restart. Check HA's MQTT device. |
| Custom element missing | HACS downloaded, module resource correct, every JS companion present, browser refreshed. |
| Element already registered | Remove duplicate resource registrations; keep the HACS entry. |
| Waiting/stale/unavailable | Confirm bridge status, topic, source/tap and freshness. A moving native meter in Potato alone does not prove MQTT delivery. |
| Controls missing/disabled | Correct source, supported group discovered, entity exists/enabled, actual readable state and services. Re-run entity suggestions. |
| Incoming meter moves when muted | Expected: choose After mute to see silence after mute. |
| Slow tablet | Start with 10 Hz, compact layout, fewer visible cards, history/diagnostics off; measure on the actual tablet. |
| Settings edits appear lost | Apply advanced draft, Save main dialog, restart; inspect private log for save errors. |

For a bug report include versions, source IDs, redacted card YAML, symptom and expected behavior. Never include broker passwords, tokens or full private settings. Developer protocol/build details are in the bridge repository's docs; native control shape meanings belong to the Voicemeeter manual.
