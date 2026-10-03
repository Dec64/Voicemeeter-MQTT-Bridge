# Mixer refresh deployment - 2026-10-03

The card is published as [v2.0.0-rc.4](https://github.com/Dec64/voicemeeter-channel-card/releases/tag/v2.0.0-rc.4). HACS downloaded main revision `160113f`, which is the RC4 tag's commit. All 19 installed files were compared with the exported distribution after normalizing line endings and matched. The Windows bridge remains 2.0.0-rc.1.

The normal HACS Reload action now loads a consistent module set. All 30 relative dependency imports carry a shared content fingerprint. Export verification checks syntax, file existence, fingerprint consistency and equivalence to the source except for import query strings.

Live verification showed the advertised Main Mic name, the +12 scale, segmented horizontal and vertical meters, real input/output peaks, gain readback and the new processing layout. The mic card had 261 advanced mappings, including all 240 parametric EQ-cell controls. Its eight-channel selector, named filter menu and numeric frequency/gain/Q controls were shown with real mixer values and confirmed enabled. Verification expanded the UI without sending audio-control commands. Command behavior is covered by the in-memory browser suite.

EQ-cell discovery was enabled in the existing private bridge profile after a backup; 3,120 cell entries were verified in HA's entity registry. The owner subsequently changed source selections and card appearance during this session; those changes were preserved. Private settings, dashboard backup and the live screenshot remain outside the repository.

Final checks: 91 Node tests; 9 meter browser scenarios; 9 control/editor browser scenarios; all JavaScript syntax and git whitespace checks; distribution and installed-file comparisons. Review is recorded in MIXER-REFRESH-REVIEW.md. No configured frontend lint/typecheck exists. Earlier physical audio, Fire tablet and full load/recovery acceptance limits remain in RELEASE-READINESS.md.

Earlier tags remain unchanged. RC2 supplied the UI refresh; live update testing found the companion-module cache issue fixed in RC3. RC4 also fixes blank display-name fallback. Use RC4 or the current main revision for this refresh.
