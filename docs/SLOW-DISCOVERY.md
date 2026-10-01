# Slow sensor discovery payloads

`SlowSensorDiscovery.Build` produces MQTT discovery messages without sending them. Each registry-enabled source receives a numeric peak sensor and activity/clipping binary sensors when v2 and the slow stream are enabled. Fast-only and empty profiles produce none. No existing discovery or running application path calls this builder yet.

IDs are `voicemeeter_<computer>_v2_strip_<index>_<peak|active|clip>` and the bus equivalent. Computer identity must be stable ASCII letters/digits/underscore/hyphen; unsupported characters are rejected rather than sanitized into possible collisions. Display labels and aliases never enter IDs, topics or template code. Device identifiers match the legacy `voicemeeter_mqtt_bridge_<computer>` convention. No HA entity IDs are assumed.

Discovery messages are retained QoS 1; every state subscription uses `BASE/v2/meters/slow` at QoS 0. Peak uses pre-fader when selected, otherwise the first selected tap; buses use output. Templates require the matching `sensor_tap`, schema 2, source availability, and a numeric/boolean field as appropriate. Missing, null, wrong-type and mismatched-tap fields produce `None`, never fabricated zero or OFF. Valid silence remains at its measured floor.

Availability requires both existing `BASE/availability` and the per-source validity template on the slow topic (`availability_mode: all`). The caller supplies `expire_after` in seconds, strictly greater than the slow window, to stop a stalled stream presenting indefinitely fresh values. This is not a measured delivery deadline. Session-aware reconnect/status policy and actual HA expiration behavior still need integration testing.

The payload has no `state_class`, `force_update` or JSON attributes subscription: it does not opt audio meters into long-term statistics or create attribute updates on every aggregate. Recorder exclusion, if wanted, must be a documented owner choice; no global configuration is changed. Removal of disabled sources requires an explicit future retained-discovery retirement policy; this builder emits no deletion messages.

Contract references checked 2026-10-01: [HA MQTT sensor](https://www.home-assistant.io/integrations/sensor.mqtt/) and [HA MQTT binary sensor](https://www.home-assistant.io/integrations/binary_sensor.mqtt/). Local rendering uses Jinja2 3.1.6 against payloads exported from the actual builder. It is not HA schema/entity verification. Read-only HA template evaluation was attempted but all available Web HA MCP connections required reauthentication.

The modular HACS card, visual editor and shared subscription remain required. Payload construction does not prove fast HA streaming performance.
