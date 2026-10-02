# Remote API evidence and limits

Update 2026-10-02: the separate [read-only meter probe](LIVE-DATA-PROBE.md) exercised
the existing identity/level bindings on the live engine, reporting Potato 3.1.3.0 and
480 available combined readings. No new native exports were added. A matching installed
header/manual and new label/advanced bindings remain unresolved. The probe does not
wire v2 into application startup or establish HA throughput.

Reviewed 2026-09-29 before adding the deterministic sampler. The installed binaries are Potato 3.1.3.0 / Remote64 1.1.3.0, but **no matching installed header/manual was located**. No native binding or live API probe was added. The existing adapter only gains an interface so tests can substitute a fake.

References downloaded outside the bridge repository:

- [Official SDK](https://github.com/vburel2018/Voicemeeter-SDK/tree/02a1abf15358ddb33e588cd31c43576a065f81ac), pinned at `02a1abf15358ddb33e588cd31c43576a065f81ac`.
- `VoicemeeterRemote.h` SHA-256: `5b1fa648f34ea4d002e40d0c7623263bbdf8471f61333a03bfae351d332f5b0b`.
- [Public Remote API manual](https://download.vb-audio.com/Download_CABLE/VoicemeeterRemoteAPI.pdf), December 2023, version 3.1.0.1; SHA-256 `5631e7fbf23bc9eaa2c85d35da20070611dd0dabce697f61795fe74d7b28e1c9`.

## Mapping and signatures

The header's GetLevel section defines modes 0/1/2 as input pre-fader/post-fader/post-mute; mode 3 is output. Its Potato diagram has five stereo inputs, three eight-channel virtual inputs and eight eight-channel outputs. The AUX diagram contains a duplicated `25` where contiguous channel `24` belongs; tests use the blueprint's complete `18..25` interval. Callback layouts in manual pages 22–24 are a separate buffer contract, not additional GetLevel inputs.

Header signature: `long __stdcall VBVMR_GetLevel(long nType, long nuChannel, float * pValue)`; Windows `long` is 32-bit. Nonzero failures include -1 error, -2 no server, -3 unavailable and -4 out of range. The existing C# adapter throws on nonzero results; the new pure sampler keeps that behavior at its boundary and marks only its own source/tap unavailable.

The vendor's `vmr_streamer/vmr_streamer.c` converts `NormalLevel` using `20*log10`, supporting the linear-amplitude conversion used in the tests. This is documentation/source evidence, not an audio calibration of the installed engine. GetLevel and dirty polling each require one calling thread. The production bridge now enforces ownership through [RemoteApiOwner](REMOTE-LIFECYCLE.md); future v2 runtime integration must still confirm Potato before using this map.

Unicode getter remains pending: the header uses an ANSI parameter-name pointer and a 512-wide-character destination for `VBVMR_GetParameterStringW`. Do not marshal both arguments as Unicode by assuming the W suffix applies to both.

## Processing candidates (not enabled or runtime-probed)

Manual pages 11–13 document these ranges; runtime support remains unverified:

| Group | Parameters / ranges |
|---|---|
| Compressor | `Comp` 0–10; input/output gain -24..24; ratio 1–8; threshold -40..-3 dB; attack 0–200 ms; release 0–5000 ms; knee 0–1; makeup boolean |
| Gate | `Gate` 0–10; threshold/damping -60..-10 dB; sidechain 100–4000 Hz; attack 0–1000 ms; hold/release 0–5000 ms |
| Denoiser | Amount and threshold 0–10 |
| EQ | Virtual `EQGain1/2/3` -12..12 dB; physical/bus cells: on, type 0–6, frequency 20–20000 Hz, gain -12..12 dB, Q 1–100; channels 0–7, cells 0–5 |
| Bus | Gain -60..12 dB; mute/EQ boolean; mono also supports value 2 (stereo reverse), which legacy boolean discovery cannot fully represent |

Physical processor support and every parameter path must be probed/version-qualified before new discovery. Native denoiser does not imply external RTX/Broadcast control. Deep EQ must remain opt-in to avoid entity multiplication.

## Deterministic policy implemented

Each sample covers exactly one canonical source and tap. It returns the hottest channel only when **all mapped channels** are valid. A missing/invalid sibling could conceal the maximum, so any such failure makes the combined result unavailable with null levels. This conservative completeness policy resolves the blueprint's MAP-04 requirement; it does not mask partial reads as silence. Valid zero remains available at the display floor. Pre/post taps and subsequent reads cannot mutate a previous sample.

This primitive has no timer, MQTT publication, label registry, activity/clip hysteresis, peak window or runtime enable switch. It is not called by `BridgeService`; enabling a new runtime path is later work behind the blueprint's v2 flags. Legacy payloads remain unchanged.
