# Measurement batch stopping point — 2026-10-02

Four implementation commits are complete on `codex/phase0-mapping-tests`:

| Commit | Result |
| --- | --- |
| `594363e` | Bounded receipt and DOM timing histograms |
| `f229741` | Shared native HA callback timestamps and rejection counters |
| `f377fa3` | Opt-in measurement controls, deadline and hidden-card cleanup |
| `062e309` | Broker receipt rates, spacing and accepted payload byte totals |

The user stopped the planned fifth implementation step and requested documentation
and a new-session handover. No new live timing results exist for this batch.

## Verification

Run from the repository root in PowerShell:

```powershell
node --test frontend/channel-card/test/*.test.js
& '../.local-phase0/dotnet/dotnet.exe' test tests/VoicemeeterMqttBridge.Tests --verbosity quiet
& '../.local-phase0/dotnet/dotnet.exe' build tools/DevelopmentBridge/DevelopmentBridge.csproj --verbosity quiet
```

Results: 81 JavaScript tests, 687 backend tests, runner build succeeded.
The backend build reported the existing Program.cs CS1998 warning; the separate
runner build reported zero warnings/errors. Final single-frame assertions were
also checked with `--filter FullyQualifiedName~DevelopmentTimingTests` (2 passed).
Browser `/browser/checks.html` passed seven fixture scenarios, including a 10-second
automatic deadline, manual stop, restart and hidden-card cleanup. These use mocks,
not HA. A fresh preview origin avoided cached modules; the fixture now waits for
both cards to become visible before its one-shot frame. Review was sequential in
the main agent, not independent agents.

## Actual HA state

The dedicated test dashboard and resources were backed up before the update.
The resource now points to
`/local/voicemeeter-v2-dev-062e309/voicemeeter-channel-card.js`.
The staging tool copied 17 files to a new version directory, and MCP readback
confirmed the resource URL. The old directory remains available for rollback.

The dashboard itself was **not edited** in this batch. Its five cards still lack
`diagnostics: true`. A dashboard tab opened but post-load visual inspection was
interrupted. The new bundle has not yet been verified rendering in HA.
No development runner was running at handover. The installed Windows bridge was
not overwritten. No repository was pushed or PR created.

## Proposed next implementation commit

`test: record live HA telemetry measurement evidence`

Enable diagnostics on one card in the dedicated test dashboard, verify the new
bundle and all five cards, then run the separate development publisher and save the
broker and card reports. Begin with a smoke check before a 15–30 minute run.
Record duration, rates, source/card counts, visibility, CPU/memory and network
observations. A short run does not satisfy the benchmark. DOM-update quantiles do
not establish physical screen presentation latency. The wider 10/20 Hz, device,
multi-tab, reconnect and load matrix remains outstanding.

See [BENCHMARKING.md](BENCHMARKING.md) for measurement definitions,
[HA-SETUP.md](HA-SETUP.md) for installation/rollback, and
[TEST-RESULTS.md](TEST-RESULTS.md) for verification history. The original blueprint
remains scope authority, including modular cards and unverified acceptance gates.
