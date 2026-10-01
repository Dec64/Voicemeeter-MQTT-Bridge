"""Render exported SlowSensorDiscovery payloads with Jinja2 (not an HA runtime test)."""
import json
import sys
from pathlib import Path

from jinja2 import Environment, StrictUndefined


def verify(path: str) -> int:
    payloads = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    expected_ids = {f"voicemeeter_test_pc_v2_{source}_{metric}"
                    for source in ("strip_0", "bus_5") for metric in ("peak", "active", "clip")}
    assert len(payloads) == 6 and {p["unique_id"] for p in payloads} == expected_ids, "Expected the six generated probe payloads"
    env = Environment(undefined=StrictUndefined)
    count = 0
    for payload in payloads:
        source = "bus:5" if "_bus_5_" in payload["unique_id"] else "strip:0"
        tap = "output" if source == "bus:5" else "pre"
        peak = payload["unique_id"].endswith("_peak")
        field = tap + "_dbfs" if peak else "clipping" if payload["unique_id"].endswith("_clip") else "active"
        value_template = env.from_string(payload["value_template"])
        available_template = env.from_string(payload["availability"][1]["value_template"])

        def render(frame, expected, availability):
            nonlocal count
            context = {} if frame == "undefined" else {"value_json": frame}
            assert value_template.render(**context) == expected, (payload["unique_id"], frame)
            assert available_template.render(**context) == availability, (payload["unique_id"], frame)
            count += 2

        def frame(value, **overrides):
            entry = {"available": True, "sensor_tap": tap, field: value, **overrides}
            return {"schema": 2, "sources": {source: entry}}

        render(frame(-12 if peak else True), "-12" if peak else "ON", "online")
        render(frame(-90 if peak else False), "-90" if peak else "OFF", "online")
        render(frame(None), "None", "offline")
        render(frame("bad"), "None", "offline")
        render(frame(True if peak else 1), "None", "offline")
        render(frame(-12 if peak else True, available=False), "None", "offline")
        render(frame(-12 if peak else True, sensor_tap="wrong"), "None", "offline")
        wrong_schema = frame(-12 if peak else True)
        wrong_schema["schema"] = 1
        render(wrong_schema, "None", "offline")
        for invalid in ({}, {"schema": 2}, {"schema": 2, "sources": {}},
                        {"schema": 2, "sources": []}, {"schema": 2, "sources": {source: None}},
                        None, [], "undefined"):
            render(invalid, "None", "offline")
    return count


if __name__ == "__main__":
    print(f"Passed {verify(sys.argv[1])} template rendering assertions")
