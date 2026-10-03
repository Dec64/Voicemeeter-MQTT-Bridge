import "../src/voicemeeter-channel-card.js";

const stage = document.querySelector("#stage"), result = document.querySelector("#result");
const assert = (value, message) => { if (!value) throw new Error(message); };
const wait = async (predicate, attempts = 150) => {
  for (let i = 0; i < attempts; i++) { if (predicate()) return; await new Promise(resolve => setTimeout(resolve, 20)); }
  throw new Error(`Timed out waiting for browser state: ${predicate}`);
};
function connection() {
  const callbacks = new Map(), events = new Map();
  return { callbacks, events, opens: 0, closes: 0, connected: true,
    addEventListener(name, callback) { events.set(name, callback); }, removeEventListener(name) { events.delete(name); },
    async subscribeMessage(callback, request) {
      this.opens++; callbacks.set(request.trigger.topic, callback);
      return async () => { this.closes++; };
    },
    emit(suffix, payload) {
      const topic = `mock/pc/v2/${suffix}`;
      callbacks.get(topic)?.({ variables: { trigger: { platform: "mqtt", topic, payload: JSON.stringify(payload) } } });
    }
  };
}
const metadata = session => ({ schema: 2, engine: "potato", session_id: session, sources: [
  { id: "strip:0", kind: "strip", index: 0, enabled: true, label: "Input <b>literal</b>", taps: ["pre"] },
  { id: "bus:5", kind: "bus", index: 5, enabled: true, label: "Output", taps: ["output"] }] });
const frame = (session, seq, level = -18) => ({ schema: 2, session_id: session, seq, published_at_utc: new Date().toISOString(),
  sources: { "strip:0": { available: true, pre_dbfs: level }, "bus:5": { available: true, output_dbfs: -32 } } });
const reading = card => card.shadowRoot.querySelector(".value").textContent;
const status = card => card.shadowRoot.querySelector(".status-text").textContent;
function card(config, hass) {
  const element = document.createElement("voicemeeter-channel-card"); element.setConfig(config); element.hass = hass; stage.append(element); return element;
}

async function run() {
  const c = connection(), checks = [];
  const cards = ["strip:0", "bus:5"].map(id => card({ bridge: { base_topic: "mock/pc" }, source: { id } }, { connection: c }));
  await wait(() => c.opens === 2 && cards.every(item => item.visible));
  c.emit("meters/fast", frame("one", 0)); assert(cards.every(item => reading(item) === "—"), "unapproved frame displayed");
  c.emit("metadata", metadata("one")); c.emit("meters/fast", frame("one", 0));
  await wait(() => reading(cards[0]) === "-18.0" && reading(cards[1]) === "-32.0");
  assert(cards[0].shadowRoot.querySelector("h2").textContent === "Input <b>literal</b>", "label changed");
  assert(!cards[0].shadowRoot.querySelector("h2 b"), "HTML label executed");
  checks.push("shared pair, source isolation, metadata gate and literal labels");
  c.emit("metadata", metadata("two")); await wait(() => cards.every(item => reading(item) === "—"));
  c.emit("meters/fast", frame("one", 1, -1)); assert(cards.every(item => reading(item) === "—"), "retired session displayed");
  c.emit("meters/fast", frame("two", 0, -24)); await wait(() => reading(cards[0]) === "-24.0");
  assert(cards[0].motion.history.length === 1 && cards[0].motion.history[0].level === -24,
    "retired session retained peak history");
  checks.push("session restart clears values and accepts matching sequence zero");
  cards[0].style.display = "none"; await wait(() => !cards[0].visible); assert(c.closes === 0, "visible sibling lost its feed");
  cards[1].style.display = "none"; await wait(() => c.closes === 2);
  assert(cards.every(item => item.expiryTimer === undefined && item.renderer.pending === null), "hidden work remained");
  cards.forEach(item => { item.style.display = ""; }); await wait(() => c.opens === 4 && cards.every(item => item.visible));
  await wait(() => cards.every(item => reading(item) === "—"));
  c.emit("metadata", metadata("three")); c.emit("meters/fast", frame("three", 0)); await wait(() => reading(cards[0]) === "-18.0");
  checks.push("offscreen teardown, sibling ownership and fresh visible resume");
  Object.defineProperty(document, "hidden", { configurable: true, value: true }); document.dispatchEvent(new Event("visibilitychange"));
  await wait(() => c.closes === 4);
  delete document.hidden; document.dispatchEvent(new Event("visibilitychange")); await wait(() => c.opens === 6);
  cards.forEach(item => item.remove()); await wait(() => c.closes === 6);
  assert(c.events.size === 0, "disconnect listener leaked"); checks.push("simulated hidden-tab pause and final removal cleanup");

  const slowConnection = connection();
  const sensorHass = age => ({ connection: slowConnection, states: { "sensor.input": { state: "-26",
    last_updated: new Date(Date.now() - age).toISOString(), attributes: { unit_of_measurement: "dBFS" } } } });
  const slow = card({ bridge: { transport: "entities_only" }, source: { id: "strip:0" }, entities: { meters: { pre: "sensor.input" } } }, sensorHass(0));
  await wait(() => reading(slow) === "-26.0"); assert(status(slow).includes("reduced freshness"), "missing fallback badge");
  slow.hass = sensorHass(14900); await wait(() => status(slow).includes("Stale")); assert(reading(slow) === "—", "stale sensor displayed");
  assert(slowConnection.opens === 0, "sensors-only subscribed"); slow.remove();
  checks.push("sensors-only mode, reduced freshness and timer expiry");

  const auto = card({ bridge: { base_topic: "mock/pc" }, source: { id: "strip:0" }, entities: { meters: { pre: "sensor.input" } } }, sensorHass(0));
  await wait(() => reading(auto) === "-26.0" && slowConnection.opens === 2);
  slowConnection.emit("metadata", metadata("auto")); slowConnection.emit("meters/fast", frame("auto", 0, -12));
  await wait(() => reading(auto) === "-12.0"); await wait(() => reading(auto) === "-26.0");
  auto.remove(); await wait(() => slowConnection.closes === 2);
  checks.push("auto upgrades to fast and falls back after fast expiry");

  const measuredConnection = connection();
  const measured = card({ diagnostics: true, bridge: { base_topic: "mock/pc", transport: "native_ws" },
    source: { id: "strip:0" } }, { connection: measuredConnection });
  await wait(() => measuredConnection.opens === 2 && measured.visible);
  const panel = measured.shadowRoot.querySelector(".measurement-root");
  assert(panel?.querySelector("button"), "measurement controls missing");
  panel.querySelector("[data-action=start]").click();
  measuredConnection.emit("metadata", metadata("measured"));
  measuredConnection.emit("meters/fast", frame("measured", 0, -15));
  await wait(() => reading(measured) === "-15.0");
  panel.querySelector("[data-action=stop]").click();
  let report = JSON.parse(panel.querySelector("textarea").value);
  assert(report.received_frames === 1 && report.dom_updates === 1, "receipt/DOM not recorded");
  assert(report.end_reason === "stopped" && report.source === "strip:0", "report identity wrong");
  panel.querySelector("select").value = "10";
  panel.querySelector("[data-action=start]").click();
  await wait(() => panel.querySelector(".measurement-status").textContent === "Measurement complete", 600);
  report = JSON.parse(panel.querySelector("textarea").value);
  assert(report.elapsed_ms === 10000 && report.received_frames === 0, "deadline or zero-data accounting wrong");
  panel.querySelector("[data-action=start]").click();
  measured.style.display = "none"; await wait(() => !measured.visible);
  report = JSON.parse(panel.querySelector("textarea").value);
  assert(report.end_reason === "hidden", "hidden measurement did not stop");
  measured.remove(); await wait(() => measuredConnection.closes === 2);
  checks.push("opt-in receipt/DOM report, restart and hidden measurement cleanup");
  return checks;
}
try {
  const checks = await run(); window.browserCheckResult = { passed: true, checks };
  result.textContent = `PASS (${checks.length} scenarios)\n${checks.join("\n")}`;
} catch (error) {
  stage.replaceChildren(); delete document.hidden;
  window.browserCheckResult = { passed: false, error: error.message }; result.textContent = `FAIL: ${error.message}`;
  console.error(error);
}
