# Timed meter windows and activity

`MeterWindowAccumulator` is an offline, single-caller building block. It accepts `SourcePeak` observations and returns immutable snapshots. It does not call the DLL, schedule samples, publish MQTT, or enable v2. It uses `TimeProvider` monotonic timestamps; UTC changes cannot alter holds or window durations.

Create one accumulator per publishing cadence and feed each the same observations. Completing a fast window does not consume the slow window's peak. Storage is fixed per enabled source/tap, not a growing list of audio samples. Each completion reports actual elapsed duration and clears window observations.

Each level is the maximum observed linear peak during that window, converted through `PeakMath`. Activity and clipping describe the latest observed state with the configured hysteresis/hold, evaluated at completion. Thus a window can contain a loud peak but end with `active=false` after observed silence. This difference is deliberate: the peak remembers the interval; the flags describe the current observed state.

| Setting | Default | Behavior |
|---|---|---|
| `activityHysteresisDb` | 3 dB | Turn on above `activityThresholdDbfs`; release at or below threshold minus hysteresis after the hold. |
| `activityHoldMs` | 250 ms | Minimum hold since the last above-threshold observation. A valid zero bypasses this hold and becomes inactive immediately. |
| `clipHysteresisDb` | 0.5 dB | Turn on at or above `clipThresholdDbfs`; release strictly below threshold minus hysteresis after the hold. |
| `clipHoldMs` | 2000 ms | Keep an observed clipping indication briefly visible, including during subsequent silence. |

Holds accept 0–60000 ms. Hysteresis must be finite and nonnegative. The activity release threshold must be at or above the display floor; the clipping release threshold must be above it so silence can release the strict comparison. Zero hold/hysteresis are supported. These additive settings preserve disabled v2 defaults and do not change existing live settings files.

A missing configured tap, explicit unavailable observation, or invalid amplitude invalidates its entire source for the window. Its level and flags become null, and all that source's held flags reset. Other sources stay independent. A later valid window can recover; an empty window cannot borrow earlier levels. Taps keep separate levels and flags; bus output never becomes an invented pre-mute reading.

The caller owns sampling completeness and scheduling. This class cannot detect a missing poll within a window if no failure observation is supplied. Each completed snapshot retains its clock and window-start timestamp internally; `IsFresh(maximumAge)` checks that its conservative age remains strictly below a caller-supplied positive budget. This includes time spent measuring, waiting to enqueue and waiting to send. `LatestMeterSnapshotQueue.ReadFreshAsync` drops expired/untracked snapshots. The runtime must choose budgets per cadence, recheck after waits, and enforce connection/session transitions. Polling cannot guarantee capture of peaks between observations.

`AggregateFrameBuilder` serializes these timed snapshots and preserves their flags. `LatestMeterSnapshotQueue` holds the newest pending snapshot until a consumer serializes it with a publication timestamp. [MeterTelemetryLoop](METER-SCHEDULER.md) now schedules independent windows behind the v2 enable flags, but is not wired into the running bridge. No MQTT feed or discovery is enabled here. Rendering peak hold/history belongs to the required modular HACS card; these building blocks do not implement that card or demonstrate HA throughput/latency. See [the message and queue contract](MQTT-V2-PROTOCOL.md).
