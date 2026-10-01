import { normalizeConfig } from "./meter-model.js";

export function applyEditorValues(config, values) {
  const next = { ...config,
    type: "custom:voicemeeter-channel-card",
    bridge: { ...config.bridge, base_topic: values.topic, transport: values.transport ?? config.bridge?.transport ?? "auto" },
    source: { ...config.source, id: values.id, display_name: values.label },
    meter: { ...config.meter, floor_dbfs: Number(values.floor), orientation: values.orientation ?? config.meter?.orientation ?? "horizontal" },
    appearance: { ...config.appearance, variant: values.variant ?? config.appearance?.variant ?? "standard" }
  };
  if (values.id.startsWith("bus:")) delete next.meter.mute_display_mode;
  else next.meter.mute_display_mode = values.tap;
  normalizeConfig(next);
  return next;
}
