# Mixer card refresh review - 2026-10-03

Status: complete. Scope: the channel-card changes after fd1ccc1, their tests and user documentation. Intent: preserve real readings and commands while improving mixer controls, meter appearance and per-card configuration.

### Actionable Findings

None remaining. The implementation loop fixed:

1. Reconfiguration retained a visible error from a previously configured control. Configure now clears old notes; the browser suite verifies clean idle feedback.
2. A processing fader draft could be overwritten by telemetry repaint. Drafts now survive paint until commit or Escape and clear when unavailable or hidden.
3. A blank friendly name left a blank heading. The heading now falls back to the advertised name or stable source ID.
4. Colour values with alpha could lose their alpha in native colour pickers. The new palette consistently accepts three- and six-digit hex colours.
5. HACS changed only the entry-point cache URL, leaving old companion modules cached after its ordinary Reload action. PackageCard now adds one content-derived fingerprint to every relative module import. Export validation checks every dependency is versioned, exists, shares the fingerprint and differs from source only by those query strings. RC3 supersedes RC2 without changing its tag.

### Coverage

Correctness, testing, maintainability, frontend lifecycle and adversarial scenarios were assessed sequentially in this thread, following the user's tool mapping. This is an inline review, not independent corroboration. External cross-model review was not run because the user declined it earlier.

The scan covered source isolation, unavailable/unknown controls, real readback after service acknowledgment, duplicate/sibling route commands, group capability restrictions, EQ channel replacement, config copying, bounds, tooltip text, positive headroom, silence falloff, reduced motion and hidden-card cleanup. The wire-metadata fixture explicitly models the backend fields rather than serializing UI-only filter choices.

Simplification: reuse 1 (shared meter fraction for fast/slow readings); quality 2 (remove superseded inline CSS and format the rewritten controls); efficiency 0 additional changes. Channel/band fields remain lazy. No safety checks were removed.

Validation: 91 Node tests pass; 9 meter browser scenarios and 9 control/editor browser scenarios pass. All source modules pass `node --check`; `git diff --check` passes. The frontend has no configured linter or typechecker. Browser fixtures must run in their active tab; switching away can intentionally pause rendering and cause timing failures.

No MQTT/API contract or Windows executable changed. EQ-cell discovery was enabled in the existing private profile, with a backup; 3,120 cell entities were verified in HA's registry. Command checks use an in-memory mock, so this refresh does not claim physical audible qualification. Earlier Fire tablet and full load/recovery qualification limits remain in RELEASE-READINESS.md.

### Verdict

Ready to publish the card update and verify the HACS installation. Actionable findings: none.
