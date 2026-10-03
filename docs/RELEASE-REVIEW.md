# Release candidate review - 2026-10-03

Scope: local changes from `c3ba200512e8c2ac2c2316d6bbe72cfa3947b1cc` on `codex/phase0-mapping-tests`. No remote publication or installed bridge replacement occurred.

Intent: complete the opt-in v2 bridge and reusable source card, preserve legacy identities and settings, and prepare Windows/HACS artifacts against the explicit v2 blueprint.

## P1 findings

**#1 Complete owner audio and device acceptance.** `docs/RELEASE-READINESS.md:19`, confidence 100. Source label probes do not prove physical audio assignments or audible control behavior. The completed 900-second HA run measured one selected source at 9.99 Hz with a 63 ms p95 publication-to-DOM estimate in a sixteen-card configured view. It does not establish the entire visible 1/5/8/16-card matrix, Fire tablet performance or restart-order recovery. Complete [owner acceptance](OWNER-ACCEPTANCE.md) and retain actual device reports before accepting the release.

**#2 Verify distribution and approved upgrade.** `docs/RELEASE-READINESS.md:21`, confidence 100. The standalone card tree and Windows installer exist locally. Actual HACS install/update and installed-bridge migration/rollback remain unverified. The handoff reserves publication and installed bridge replacement for owner approval. Obtain that approval, publish the reviewed source/card, and test HACS install/update plus a backed-up upgrade and rollback.

## Verification and deployment

- Backend: 704 passed, zero failures/skips. Frontend: 88 passed. Browser: seven meter and eight control/editor scenarios passed.
- The real settings Apply action preserves an existing 50 ms legacy interval. Both supported window sizes were visually inspected.
- Release publish and both installer builds passed. Final isolated install/uninstall matched the current executable and preserved live settings.
- Full metadata bounds, stream restart history reset, strict control readback and atomic settings preservation have regression coverage.
- Current artifacts are unsigned. Development discovery must remain disabled alongside production because discovery unique IDs can collide despite different topics/clients.
- The installed bridge and Office Hub were not replaced. See [readiness and rollback](RELEASE-READINESS.md) for the remaining steps.

## Coverage

Correctness, testing, maintainability, security, performance, API contract, reliability, adversarial and frontend race passes were performed sequentially in the main context under the user's AGENTS.md mapping. There were no independent reviewers or validators. The user declined the external Claude review; no peer job started and no source was sent.

Reuse, quality and efficiency simplification passes were also performed inline. Meter DOM nodes were cached; independently asserted frontend/backend vendor bounds were retained. No applicable in-tree standards file, learnings corpus or declared review packs were found. No agent-facing feature changed.

Both findings are confirmed requirements gaps, validated in the main context against the blueprint and saved reports. Zero malformed, rejected or unresolved findings remain. Stage costs were not collected. Machine-readable receipts are saved under the machine-local `ce-code-review/20261003-release-rc1` run directory.

## Actionable findings

| # | Finding | Route | Required evidence |
|---|---|---|---|
| 1 | Owner audio and device acceptance | manual -> downstream-resolver | Physical source/control sign-off, complete device/load matrix and recovery reports |
| 2 | Actual distribution and approved upgrade | manual -> downstream-resolver | Owner authorization, actual HACS install/update and backed-up installed upgrade/rollback |

## Verdict

**Not ready for full release.** The local release candidate is built and locally verified. The two acceptance findings above remain required; passing automated tests does not close them.

## Authorized RC publication - 2026-10-03

The owner explicitly requested GitHub publication, HACS installation and replacement of the local bridge after this review. This authorizes the distribution/upgrade actions in finding #2; their results must still be verified. Finding #1 remains a stable-release acceptance requirement. Publication is an explicitly identified RC, not a claim of full hardware acceptance.

The publication delta adds original generated artwork, a plain-language guide, its README presentation and explicit safe installer/card package entries. A sequential correctness/security/contract/documentation pass checked the exporter, installer allowlist, all JSON setting names against source and supported card ranges. No new actionable code defect was retained. The backend/frontend executable code is unchanged from the verified suites. External peer review remains declined; no independent coverage is claimed.

Publication and initial deployment are now verified in [deployment results](DEPLOYMENT-RESULTS.md): public RC artifacts, an actual HACS installation, a backed-up live Windows upgrade, preserved prior root settings and a working production dashboard. Finding #2 is substantially addressed; a subsequent HACS version update and an exercised live rollback remain acceptance evidence. Finding #1 remains open. The stable-release verdict is unchanged.
