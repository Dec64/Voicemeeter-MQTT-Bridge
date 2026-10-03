# Owner acceptance record

Use this after reviewing the candidate and approving any installed-bridge or dashboard changes. Unit tests and native label reads cannot confirm physical audio paths or perceived tablet motion. Record failures as well as passes.

## Audio and source identity

For each of Guest Mic (native label strip:1), External PS5 (strip:2), System (strip:5), Chat (strip:6) and Music (strip:7):

1. Record the current gain, mute and routes before changing anything.
2. Produce a recognizable signal on that physical/application path while the others are quiet. Confirm only the intended canonical input meter responds.
3. Compare incoming and post-mute cards for the same input. Toggle mute: incoming still reports the signal; post-mute falls to silence. Restore mute.
4. Change gain and one intended route from both Voicemeeter and HA. Confirm observed HA state reconciles without an optimistic stale value, and confirm the audible route. Restore recorded values.
5. Test supported processing only on an appropriate source. Verify native/HA readback and audible effect, then restore each value. Virtual inputs do not gain physical compression/gate/denoiser support from legacy entity names.

| Source | Physical/application path confirmed | Pre/post-mute | Gain/routes restored | Processing checked |
|---|---|---|---|---|
| strip:1 | pending | pending | pending | pending |
| strip:2 | pending | pending | pending | pending |
| strip:5 | pending | pending | pending | pending |
| strip:6 | pending | pending | pending | pending |
| strip:7 | pending | pending | pending | pending |

Also isolate each intended output bus. Native labels are listed in [source evidence](ACTUAL-SOURCE-MAPPING.md); labels alone do not approve this table.

## Desktop and Fire tablet

Use the dedicated test dashboard, recording the original tablet URL before navigation. Return it afterward. Record actual device model, OS/WebView/browser version, screen orientation and number of visible cards. A remotely reachable settings page does not prove card rendering on that device.

For 1, 5, 8 and 16 visible cards at both 10 and 20 Hz, keep the view visible for 15-30 minutes. Save the diagnostics report before changing views; hidden-tab cleanup ends a run. If all cards do not fit, record the actual visible count rather than claiming sixteen. Disable diagnostics after acceptance.

Record accepted rate; receipt spacing; publication-to-receipt and receipt-to-DOM percentiles; superseded/unpainted/rejected frames; visible attack/decay/hold; browser CPU/memory; and control response. DOM timing does not certify physical screen presentation. Confirm portrait/landscape controls fit and touch targets remain usable.

Test a second tab, removal of all but one card, removal of the final card, returning from a hidden view, reload, and loss/restoration of the native connection. Slow fallback must show its reduced-freshness badge and expire correctly.

## Recovery and distribution

- In an approved test environment, restart broker, HA and Voicemeeter in different orders. Verify new session metadata, availability, discovery and fresh readings recover; do not restart shared production services merely to collect this evidence.
- Verify HA birth causes bounded discovery republishing. Development discovery on the same computer can collide with production identities; see [runner guidance](DEVELOPMENT-RUNNER.md).
- After publication approval, install the standalone card through an actual HACS custom repository, verify every module loads, then exercise an update and rollback.
- After installed-bridge replacement approval, back up the current private config/binary, test migration, then restore them to prove rollback. The separate-AppId smoke test is already passed but does not replace this acceptance.

Attach completed reports and mark each item in [release readiness](RELEASE-READINESS.md). Do not label the candidate a completed release while these rows remain pending.
