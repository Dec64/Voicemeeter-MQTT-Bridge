import { normalizeConfig } from "./meter-model.js";
import { normalizeControls } from "./control-model.js";

export function applyEditorValues(config, values) {
  const sourceChanged =
    (config.source?.id ?? "") !== values.id ||
    (config.bridge?.base_topic ?? "") !== values.topic;
  const next = {
    ...config,
    type: "custom:voicemeeter-channel-card",
    bridge: {
      ...config.bridge,
      base_topic: values.topic,
      transport: values.transport ?? config.bridge?.transport ?? "auto",
    },
    source: { ...config.source, id: values.id, display_name: values.label },
    meter: {
      ...config.meter,
      floor_dbfs: Number(values.floor),
      orientation:
        values.orientation ?? config.meter?.orientation ?? "horizontal",
    },
    appearance: {
      ...config.appearance,
      variant: values.variant ?? config.appearance?.variant ?? "standard",
    },
  };
  if (values.id.startsWith("bus:")) delete next.meter.mute_display_mode;
  else next.meter.mute_display_mode = values.tap;
  if (sourceChanged) delete next.entities;
  if (values.sensor !== undefined) {
    const tap = values.id.startsWith("bus:")
      ? "output"
      : values.tap === "post_mute"
        ? "post_mute"
        : "pre";
    next.entities = {
      ...next.entities,
      meters: { ...next.entities?.meters, [tap]: values.sensor },
    };
  }
  if (values.controlSettings) {
    const { gain, mute, solo, routing } = values.controlSettings;
    next.controls = {
      ...config.controls,
      gain: gain.visible,
      mute: mute.visible,
      routing: routing.visible,
    };
    next.entities = {
      ...next.entities,
      gain: gain.entity,
      mute: mute.entity,
      routes: { ...routing.entities },
    };
    if (solo) {
      next.controls.solo = solo.visible;
      next.entities.solo = solo.entity;
    }
  }
  if (values.ceiling !== undefined)
    next.meter.ceiling_dbfs = Number(values.ceiling);
  if (values.meterDisplay) Object.assign(next.meter, values.meterDisplay);
  if (values.appearanceDisplay)
    Object.assign(next.appearance, values.appearanceDisplay);
  if (values.meterColors)
    next.meter.colors = { ...config.meter?.colors, ...values.meterColors };
  if (values.controlColors)
    next.appearance.colors = {
      ...config.appearance?.colors,
      ...values.controlColors,
    };
  normalizeConfig(next);
  normalizeControls(next);
  return next;
}
