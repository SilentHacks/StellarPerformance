# Configuration and compatibility

Version 2.0.0 targets the inspected Windows StellarDrive Demo 0.8.2 / Unity
6000.3.24f1 assemblies and MelonLoader 0.7.3. SDModKit support is optional;
fluid topology checks SDModKit's inspected assembly identity when present and permits its known connection hook.

Live preset settings are available through F8 / the menu's Performance button.
They apply during play and save without restart, world reload or reconnect.
See [preset settings and multiplayer rules](PRESETS.md).
Startup feature/compatibility overrides are in `[StellarPerformance]` in
`UserData/MelonPreferences.cfg`. Close the game before editing the file manually.
Existing values for retained keys are
respected. Old keys may remain in the file but have no implementation behind them.
In particular, `PartSimulationDivisor`, `PickingCameraDivisor`, `StableSignals`,
`CableVisualHz` and the old UI/distance/terrain keys are ignored. The live controls
below have separate keys. Obsolete simulation keys cannot alter native stepping.

The new live keys are `Preset` (Balanced by default), `OutlineMode`
(0/native, 1/silhouettes, 2/off), `ReadoutHz` (0/native), `MinimumLabelPixels` (0),
`PresetShadowDistance` (0/native), `PresetShadowCascades` (0/native), `ExhaustDensity` (1),
`PartLabelDistance` (0/native), `PresetShadowResolution` (0/native),
`PresetShadowQuality` (0/game, 1/hard, 2/off), `VegetationVisualRange` (1),
`CableEffectHz` (0/native, 5–60) and `TerrainDetailFrames` (0/native, 2–30).
Unreleased development keys `SceneOutlines` and `DistantReadoutHz` are ignored. Custom reads
these individual values; named presets supply the mapping documented above.
The existing `IncrementalRooms` preference is also updated by the live selector.

| Setting | Default | Purpose |
| --- | --- | --- |
| `ShipGridBounds` | `true` | Cache exact bounds; invalidate on native grid edits. |
| `FluidCableTopology` | `true` | Cache visual connectivity; recompute live flows and native visuals every frame. |
| `IncrementalRooms` | `true` | Reconstruct edited regions locally. Small gas rounding differences remain. |
| `SinglePlayerRooms` | `true` | Forward single-player room queries to the authority; restore the local mirror for peers/debug views. |
| `ShipPartBatching` | `true` | Batch supported fixed opaque meshes; restore native renderers during interaction and recover unchanged geometry afterward. |
| `BatchingContinuousValidation` | `false` | Inspect every batched source each frame, in addition to retained event hooks. Useful when diagnosing compatibility; costs work. |
| `DistillerReplication` | `true` | Suppress consecutive distiller notifications only when native serialized contents are identical. |
| `PartReplicationDeduplication` | `true` | Suppress identical native payloads for the other inspected part types; retain native calculations and genuine encoded changes. |
| `SignalConnectionScan` | `true` | Reuse exact native signal connection unions; retain live classification and circuit operations. |
| `SignalReduction` | `true` | Exact native-state signal reduction without temporary endpoint lists. |
| `PowerTransferScan` | `true` | Avoid repeated cable IDs accumulated by native power-group rebuilds. |
| `SpatialClustering` | `true` | Exact spatial search for surface-space object groups; native clustering below 128 objects or outside supported guards. |
| `EmptyWarpUpdates` | `true` | Avoid no-op warp/ground lookups when the live native warp-part set is empty. Requires the exact inspected Foundations binary and native providers; real warp-drive updates remain unchanged. |
| `SaveSerialization` | `true` | Exact hull-JSON and native-contract reuse on the compatible native save writer; replacement save writers retain their own path. |
| `CanvasCulling` | `true` | Hide inspected offscreen world-space part canvases before UI rendering; keep native readout updates and focused controls. |
| `ReliableTransport` | `false` | Experimental bounded retention/retry of reliable Steam packets after temporary congestion. |
| `NetworkCaptureSeconds` | `0` | Explicit startup capture, up to 600 seconds; writes aggregate and peer CSVs to `UserData`. Zero installs no diagnostic observers. |

Each module logs installed, disabled or skipped in `MelonLoader/Latest.log`.
The five CPU switches default on for new or missing preferences in every tier,
including Faithful and Custom. Existing stored `false` values remain respected;
an upgrade does not distinguish an old saved default from an explicit opt-out.
These are startup settings requiring a restart, independent of the live F8
selector. Selecting a tier does not reset them or retry skipped modules.
Compatibility guards remain active even when a setting is enabled. The inspected
Foundations/Workshop async save pipeline replaces the native writer, so
`SaveSerialization` skips it and adds no measured gain on that path. Their
preservation contracts, targeted measurements and limits are in
[CPU optimisation research](CPU_RESEARCH.md). Gains depend on the workload.
Replication uses the `replication-deduplication` module. `DistillerReplication`
retains its existing meaning; the new switch independently controls the additional
types. Set both to `false` for native replication. These switches have no timer,
precision tolerance or per-player baseline cache. Authoritative callbacks remain
native, but repeated client notifications can be omitted. Native interaction and
pickup state changes are explicitly retained even when their fields are absent
from the payload. Unknown/derived types and conflicting serializers remain native.
See [supported types and evidence](REPLICATION_DEDUPLICATION.md).
Batch recovery waits for overlapping hover/highlight callbacks to end. Changed
materials or poses remain native. Construction work is spread across frames;
individual mesh operations are bounded, but the time budget is cooperative and
cannot guarantee that every frame avoids a stall. Cached triangle indices have a
16 MiB global ceiling; geometry exceeding it stays native.
Assembly identities and patch ownership are checked before installation. Unknown
body replacements fall back to the previous/native implementation. Batching excludes
uninspected scripts, unsupported materials and transforms, and retains native
interaction restoration. A module being enabled in settings does not prove it installed;
check the log when comparing performance.

Canvas culling checks the union of cameras rendering the graphics' layers before
each UI draw. It retains a conservative margin, focused/selected input and native
disabled states. It tracks at most 4,096 canvases and 32 active cameras; overflow or
uncertain camera data retains native drawing. Unknown scripts, nested canvases,
dynamic layout controllers and unbounded text remain native. Root resizing releases
the cached layout. Runtime changes to other cached prefab layout/layer/font properties
by another mod are outside this inspected contract; disable `CanvasCulling` for such
UI modifications. See [canvas validation](CANVAS_CULLING.md).

Changing patches after initialization is outside the supported contract, except the
room dirty-update ownership recheck. Guards do not establish compatibility with arbitrary
mods or future game builds. See [current validation and measurements](BENCHMARKS.md).

The old integrated capture hotkeys and loader reflection patch are removed.
FPS measurements use a separate private native frame recorder, absent from the shipping DLL.
The new network capture is opt-in and adds overhead while active; restore its setting
to zero before measuring FPS. See [large-build optimisation](SCALE_OPTIMISATION.md)
for queue limits, callback tradeoffs, native test results and remaining acceptance.
The aggregate CSV's `suppressedIdenticalPayloads` counts additional suppression
across all supported types, excluding states already filtered by native equality.
Appended `partNotifications` and `suppressedDistillerPayloads` columns distinguish
total activity and the legacy distiller subset.
