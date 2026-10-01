import "../src/voicemeeter-channel-card.js";
const incoming = document.querySelector("#incoming"), post = document.querySelector("#post"), output = document.querySelector("#output");
incoming.setConfig({ source: { id: "strip:0", display_name: "Input example" } });
post.setConfig({ source: { id: "strip:0", display_name: "Input example" }, meter: { mute_display_mode: "post_mute" } });
output.setConfig({ source: { id: "bus:5", display_name: "Bus example" } });
const editor = document.querySelector("#editor");
editor.setConfig({ source: { id: "strip:0", display_name: "Input example" } });
editor.addEventListener("config-changed", event => { incoming.setConfig(event.detail.config); sendFixture(); });
let sequence = 0;
function sendFixture() {
  const scenario = document.querySelector("#scenario").value;
  if (scenario === "stale" || document.hidden) return;
  const available = scenario !== "unavailable";
  const input = scenario === "silence" ? -90 : Number(document.querySelector("#input").value);
  const bus = scenario === "silence" ? -90 : Number(document.querySelector("#bus").value);
  const frame = { schema: 2, session_id: "local-fixture-session", seq: sequence++, published_at_utc: new Date().toISOString(), sample_window_ms: 50,
    sources: {
      "strip:0": { available, pre_dbfs: available ? input : null, post_mute_dbfs: available ? scenario === "muted" ? -90 : input : null, active: available ? input > -90 : null, clipping: available ? input >= 0 : null },
      "bus:5": { available, output_dbfs: available ? bus : null, active: available ? bus > -90 : null, clipping: available ? bus >= 0 : null }
    } };
  [incoming, post, output].forEach(card => card.setFrame(frame));
}
document.querySelector("#scenario").addEventListener("change", sendFixture);
document.querySelectorAll("input").forEach(input => input.addEventListener("input", sendFixture));
document.querySelector("#theme").addEventListener("click", event => {
  const light = document.body.classList.toggle("light");
  event.target.textContent = light ? "Dark theme" : "Light theme";
  event.target.setAttribute("aria-pressed", String(light));
});
sendFixture();
const timer = setInterval(sendFixture, 250);
addEventListener("pagehide", () => clearInterval(timer), { once: true });
