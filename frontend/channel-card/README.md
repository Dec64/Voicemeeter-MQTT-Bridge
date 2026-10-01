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

Still required: remaining editor sections, layout choices, shared authenticated HA subscription,
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
