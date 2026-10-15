# Configuration and compatibility

## Supported versions

- Windows; inspected StellarDrive Demo 0.8.2 / Unity 6000.3.24f1 assemblies.
- MelonLoader 0.7.3 Open-Beta (the tested Mono/net472 loader).
- SDModKit is optional. Its adapters target the inspected 1.3.3 build. Workshop is
  optional; its existing membership and hull caches take precedence over duplicates.

Start the game and inspect the Stellar Performance messages in `MelonLoader/Latest.log`.
Each module reports installed, disabled, or skipped. Modules whose inspected assembly
identities differ are skipped. Conflicting body replacements are skipped; observer
hooks are allowed where the original callbacks remain in use. Changing patches after
initialization is outside the supported contract, except the room module's dirty-update
owner recheck. Compatibility checks cannot guarantee support for arbitrary other mods.

## Settings

Settings are in `[StellarPerformance]` in `UserData/MelonPreferences.cfg` and require a
restart, except capture duration is read when a capture starts. Existing installations
retain their saved values: upgrading does **not** overwrite explicit saved choices.
To adopt the 0.9 profile on an older installation, enable `IncrementalRooms`,
`ShipPartBatching`, `SignalEvaluationReads`, `RendererDetail`, `StableSignals`,
`SinglePlayerRooms`, `TerrainTraversalCadence` and `LoaderReflectionCache`, set
`WorldUiMaximumDistance = 100.0`, `WorldUiRefreshHz = 10.0`,
`MinimumDetailPixels = 3.0`, `PartShadowDistance = 40.0`, `CableVisualHz = 20.0`,
`PickingCameraDivisor = 3` and `PartSimulationDivisor = 2`. Set `Diagnostics = false`
for ordinary play. Review the cadence tradeoffs below before adopting this profile.

| Setting | New-install default | Purpose |
| --- | --- | --- |
| `WorldObjectSnapshots` | `true` | Host: one native world-object snapshot per synchronization pass. |
| `CableMeshBuffers` | `true` | Reuse zeroed, exact-length arrays during synchronous cable mesh uploads; retained storage capped at 6 MiB per thread. |
| `ContentMembership` | `true` | Optional SDModKit adapter: cache membership while resolving definitions and state live. |
| `ScopedHullMeshReads` | `true` | Reuse source mesh reads within one rebuild; defer to Workshop's cache when present. |
| `ShipGridBounds` | `true` | Cache ship bounds with grid-mutation invalidation. |
| `StatefulPartQueries` | `true` | Reuse resolved part collections across native updater queries. |
| `FluidCableTopology` | `true` | Rebuild the visual graph on cable/connection/part changes; read flow state live. |
| `PartVisualUpdates` | `true` | Avoid redundant idle, billboard and liquid presentation work. |
| `ModKitToolRefs` | `true` | Optional SDModKit adapter: resolve currently selected tools without scene scans. |
| `DistillerSlotReuse` | `true` | Preserve equal inventory-slot arrays so native record equality can filter updates. |
| `PhysicsSceneTransforms` | `true` | Avoid exact-equal collider writes and repeated scene-preparation work; prune inactive spaces. |
| `WorldVisualUpdates` | `true` | Reduce repeated hotspot transform and cleanup work. |
| `ClientSignalCables` | `true` | Reuse validated signal membership and per-prefab signal socket reads. |
| `NativeStateReuse` | `true` | Reuse unchanged native records while keeping calculations and state events. |
| `StableSignals` | `true` | Host: skip unchanged native signal operations in their original order; changing values wake immediately and derivatives stay native. |
| `StableSignalRecheckSeconds` | `0.25` | Maximum stable-value refresh interval in simulation seconds; initial deadlines are staggered. Wiring is rebuilt on native topology invalidation. Clamped to 0.02–1. |
| `WorldUiVisibility` | `true` | Coalesce hidden display changes and refresh on re-entry. |
| `CullOffscreenWorldUi` | `true` | Suppress registered offscreen Canvases; requires `WorldUiVisibility`. Focused fields remain active. |
| `WorldUiMaximumDistance` | `100.0` | Part UI drawing distance in metres; focused fields remain active. Zero preserves native distance, clamped to 0–9000 m. |
| `WorldUiRefreshHz` | `10.0` | Numeric signal/gas readouts coalesce to this rate. Re-entry, focused fields and control changes refresh immediately. Zero uses native frequency; maximum 60 Hz. |
| `RendererDetail` | `true` | Sample supported opaque fixed part renderers at 10 Hz; omit tiny distant geometry and distant shadow casting. All nearby geometry within 20 m stays native. |
| `MinimumDetailPixels` | `3.0` | Minimum projected mesh diameter beyond 20 m, using the largest relevant active-camera projection. Hysteresis restores at 1.5× the threshold. Zero disables mesh omission; maximum 16 pixels. |
| `PartShadowDistance` | `40.0` | Stop supported fixed parts casting shadows beyond this many metres; restore at 90% of the distance. Zero preserves native shadows; maximum 9000 m. |
| `PacketTrailerHardening` | `true` | Preserve native channel framing while providing safe trailer capacity. No MTU, queue or congestion fix is claimed. |
| `FluidGraphSocketReads` | `false` | Experimental fallback: reuse socket arrays within one fluid update. Assumes prefab hierarchy is stable during that call. |
| `SignalEvaluationReads` | `true` | Host signal membership projection; values, roles and schedules stay live. Connection snapshots retain at most 1,024 entries and 4 MiB per calling thread. |
| `IncrementalRooms` | `true` | Local room reconstruction with valid reload coordinates; small gas rounding differences are accepted. Do not stack room rewriters. |
| `SinglePlayerRooms` | `true` | Single-player host: forward local room-position queries to the server and omit the duplicate client tree. Native client reconstruction returns when another player joins or a room debug visualizer needs the tree. Conflicting room patches disable this module. |
| `ShipPartBatching` | `true` | Batch supported fixed opaque part meshes. Unsupported geometry stays native. |
| `BatchingContinuousValidation` | `false` | Inspect batching sources every frame for compatibility. Can add substantial CPU cost. |
| `TerrainTraversalCadence` | `true` | Reuse terrain LOD selection until movement exceeds max(0.25 m, 0.2% of altitude), refreshing at least every 15 rendered frames. Reuse completed empty mesh deltas between traversals; flushes and outstanding deltas remain live. |
| `CableVisualHz` | `20.0` | Recompute cosmetic signal glow targets and cached fluid-flow balancing at this rate; animation continues each frame. Fluid cadence requires `FluidCableTopology`. Zero uses every frame; clamped to 0–60 Hz. |
| `PickingCameraDivisor` | `3` | Render the object-ID camera every Nth frame; hover/interaction feedback may arrive a few frames later. Values at or below 1 preserve every-frame rendering; maximum 6. |
| `PartSimulationDivisor` | `2` | Host: advance factory signals, fluids, machines, power and rooms every Nth network tick with a multiplied step. Values at or below 1 preserve every-tick simulation; maximum 4. Piloting, lever use and ship repositioning bypass this cadence. |
| `LoaderReflectionCache` | `true` | Replace the inspected MelonLoader support component's repeated sibling-order reflection with direct transform calls. Skip if the expected support layout is unavailable. |
| `Diagnostics` | `false` | Enable bounded captures with Ctrl+Shift+F8. |
| `DiagnosticMode` | `"Lean"` | `Frame`: frame/GC bookkeeping. `Lean`: aggregate probes. `Full`: detailed, more expensive probes. |
| `CaptureSeconds` | `30.0` | Capture duration, clamped to 5–120 seconds. |

`CableScratchCollections`, `SignalScratchCollections` and `TerrainScratchCollections`
were removed in 0.7.0. `SleepingMachinery` and `IdleMachineProbeSeconds` were removed
before the 0.9.0 release. Old saved keys are ignored; they no longer install patches.

Profiling support remains available for troubleshooting, but `Diagnostics = false`
installs no profiling hooks and performs no captures. Feature diagnostic counters are
collected only during an explicit capture, including when diagnostics are enabled.
`DiagnosticMode` and `CaptureSeconds` are dormant while diagnostics are disabled.
Existing saved `Diagnostics = true` is preserved on upgrade; set it to `false` and
restart to disable diagnostics on an existing installation. Normal startup and
compatibility warning messages remain available.

Stable signals compare inputs bit for bit and decide as each native operation is reached,
so changes traverse a chain in native order during the same tick. Supported math gates,
non-derivative unary processors and transmitters sleep only while their published state
and inputs remain unchanged. Cable groups skip recomputation while their cached native
source values are unchanged. Derivatives, nonfinite inputs and unsupported part states
run on each factory step. `PartSimulationDivisor` controls the frequency of those steps.
Topology rebuilds invalidate observations; periodic refresh bounds missed
membership changes. Identical callbacks are coalesced while asleep, which may affect mods
using unchanged updates as a clock. The cache allows 8,192 combined parts/groups and
16,384 source references per updater, with a 1,024-socket group limit; larger cases fall
back to native execution. Set `StableSignals = false` to restore native signal
operation execution on each factory step. Also set `PartSimulationDivisor = 1` for
native every-tick signal scheduling.
Operations whose inputs repeatedly change use native execution for 250 ms before
checking sleep eligibility again, limiting checking overhead on busy circuits. This
backoff delays entering sleep, without delaying signal evaluation.

Batching includes inspected fixed bodies on signal parts, tanks, valves, thrusters,
distillers, electrolyzers, constants, gas meters/exhausts and strictly screened generic
parts. Moving machinery, liquid renderers, text and effects remain native. Supported
bodies use readable single-submesh opaque meshes, matching materials and vertex
layouts, and uniform positive transforms. It supports stock URP shaders and the inspected
`Custom/PlanetObject` asset fingerprint. Screens, unknown custom shaders, animated
assemblies, unsupported scripts, mirrored/nonuniform transforms and lightmapped geometry
retain native rendering. Interaction restores affected batches. Building, travel and
multiplayer coverage limits remain documented in [validation](VALIDATION.md).

Renderer detail applies only to supported fixed part bodies and their batch replacements,
with at most 32,768 tracked renderers. It preserves native renderer enablement and restores
its own suppression on shutdown or a runtime error. It does not change ID rendering,
colliders, physics, signal values, fluid quantities or production rates. Individual small
meshes can pop in/out at distance and distant part shadows disappear. Geometry within
20 m retains full detail; visibility is sampled every 0.1 seconds, so fast approaches can
briefly show the previous distant detail. All-camera projection checks preserve zoomed views.

Factory cadence preserves global physics/network ticks and save/network formats, but a
larger step is an approximation: discrete production thresholds, resource transfers,
fluid relaxation, derivatives and feedback can differ. At the inspected 50 Hz network
rate, the default schedules factory work at 25 Hz. Entering an every-tick control state
resets the cadence without catching up a pending skipped tick. This is separate from
the removed idle-machinery sleeping experiment.

To restore native scheduling and presentation detail, close the game and set:

```toml
[StellarPerformance]
PartSimulationDivisor = 1
StableSignals = false
TerrainTraversalCadence = false
CableVisualHz = 0.0
PickingCameraDivisor = 1
WorldUiRefreshHz = 0.0
WorldUiMaximumDistance = 0.0
RendererDetail = false
ShipPartBatching = false
IncrementalRooms = false
SinglePlayerRooms = false
```

Edit the existing section rather than adding a duplicate. Other exact-work reuse settings
can remain enabled. Terrain selection can lag small movements or terrain edits until its
next refresh; broader travel and editing coverage remains open.
