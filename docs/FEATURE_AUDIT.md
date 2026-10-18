> Historical 0.9.0 audit. The resulting five-feature 1.0.0 candidate is described and measured in [BENCHMARKS.md](BENCHMARKS.md). Removed implementation fixtures are preserved in the private starting-checkout snapshot; old results do not apply to the new DLL.

# Feature audit before 1.0.0

Measured evidence and release recommendations, 17-18 October 2026. Shipping source/defaults were not changed by this audit.

## Release recommendation

Keep the demonstrated cache and geometry savings. Reject coarse factory cadence as a preservation default. This audit establishes specific preservation contracts and specific counterexamples; it does not establish that every current default is imperceptible or that every optimisation improves every workload.

- **Discard the factory throttle from the preservation profile** (`PartSimulationDivisor=1`). Its median frame-time reduction was only 2.11% here, with observations spanning 4.34% faster to 0.04% slower. Meaningful pulse, feedback, control-transition and production differences already disqualify it. Native Mono electrolyzers reproduced 59.40117 output versus 60.00099 under doubled steps with brief power outages. Native-rate configured methods matched the untouched native oracle at every tested step, including power requests, inventory/state, output and publication counts.
- **Restore native picking frequency** (`PickingCameraDivisor=1`). Three runs show no reliable frame benefit; the calibrated actual camera gate adds two rendered frames of buffer age. That is an interaction change, although no wrong click was demonstrated.
- **Keep grid bounds, query/state reuse and supported batching within their tested contracts.** Disabling grid bounds, fluid topology and mesh batching increased mean frame time by approximately 32.30%, 47.02% and 16.17% respectively. These are marginal costs in the reference configuration, not additive savings or a universal FPS promise. Fluid topology's ablation also removes its fluid refresh reuse; it is not a pure same-frequency cache comparison.
- **Keep incremental rooms with documented gas rounding limits.** The native 40³ shell edit fixture took 7.28 ms to add and 5.60 ms to remove an interior voxel with incremental rooms disabled; the first enabled reference took 0.098 ms and 0.054 ms. These are targeted room-update medians, not complete construction or FPS timings. Connectivity, geometry, volume, usable seeds and gas identity passed the tested edits; broad threshold equivalence remains open.
- **Keep single-player room forwarding within the tested native/loopback scope.** Actual second-process join rebuilt the local tree from one leaf to 208,279 leaves. Native hull removal/restoration, remote queries, host-local queries and disconnect passed. Both enabled and disabled configurations were tested. WAN, Steam transport, remote production chains and pressurised-room gas replication are not certified.
- **Split/review the physics feature for cost.** The fair same-delegate benchmark measured about 20 ns for an unchanged native setter and 37 ns for the guard. The complete feature's frame effect is inconclusive (+0.05% median disable cost). Forced-gravity dynamic-body tests matched position, velocity, quaternion components and sleep state exactly; there is no demonstrated numerical physics regression here. Scene caching, ship interpolation, travel and docking require separate workload evidence.
- **Rework or demote unearned presentation trade-offs.** Prefer native UI distance/refresh, cable refresh and shadow/detail limits until dynamic acceptance demonstrates that their differences are imperceptible. The isolated screens found only small effects for these switches. Preserve safe caching/culling independently where possible. Terrain cadence had a consistent approximately 3.15% disable cost, but moving LOD transitions and terrain edits remain acceptance work.

Stable signals matched the native circuit states in the tested traces at native factory cadence, while deliberately coalescing redundant callbacks. Keep that distinction documented for observer mods. A negligible stationary effect does not invalidate an allocation/query optimisation exercised by a different workload; it also does not establish that optimisation's performance benefit.

## Native follow-up evidence and limits

The matched-build follow-ups use a graphics-enabled host and an actual second game process over loopback IP, with room forwarding enabled and disabled. Each fixture compares approximately 1,027 authoritative sample positions at each remote phase. This is real native networking, not a mocked connection-count test, and remains limited to loopback geometry/transition coverage.

Native-rate machine tests compare 3,000 steps in each of four scenarios per session: continuous operation, scarce input, limited capacity and brief power outages. Both configured and untouched native methods use isolated state. This proves the tested method-level outcomes; it does not prove an arbitrary factory network.

The dynamic-body follow-up explicitly sets and restores gravity, verifies floor arrival, and records sleeping frames. All six paired position/rotation/both scenarios matched exactly; both bodies slept for 547 of 600 steps in each scenario. The earlier floor fixture did not establish collision coverage under the game's zero global gravity and is not used as proof of collision or collider-reactivation equivalence. Full ship physics remains open.

The graphics-enabled native highlight check restored five source renderers and retained the part object/state. Its chosen visual root contained zero colliders, so it supplies no positive collider-identity coverage. It also does not establish highlight pixels, real hover/click targeting, all lighting, materials or moving-part restoration. Reviewed near-field captures showed no obvious geometry regression; static views do not prove distant-shadow, popping or refresh-latency acceptance.

Two preliminary peer runs accidentally used the restored installed binary (`B01F3F…`) rather than the frozen frame-test build (`430360…`). They are excluded from candidate conclusions, retained privately, and listed in the evidence JSON. Corrected runs enforce and record the frozen binary in both private instances. The original installation remains restored to its original binary; the candidate was not installed permanently.

## Verification and remaining release work

The optional-enabled C# suite passes 298 cases; core passes 294 with four expected adapter skips. All 14 Python frame-analysis tests pass. The 68 frame captures pass focus, duration, workload, configuration, binary and graphics checks. Native post-capture probes have 70 successful check receipts. Characterisation tests that reproduce known failures remain explicitly separate from equivalence evidence.

The final 1.0.0 configuration still needs a combined-profile comparison, representative active production/travel/docking and dynamic visual/input acceptance. The table below quantifies individual switches; it cannot predict the sum of several setting changes. No shipping source/default changes, commits, permanent deployment or publication were made by this audit. Original installed files and original saves were hash-verified after the temporary frame-test deployment; the isolated peer runs separately verified all pre-existing world files.


## Frame performance

68 fresh-process captures, each 25 seconds of focused warm-up and 30 seconds measured, with the same 20-ship seed and shipping binary. Reference uses native factory cadence (`PartSimulationDivisor=1`) and all other shipping defaults. Controls bracket groups of five changes; ten preselected features and two adaptive follow-ups have three observations. Other rows are single-run screens. Grid-bounds and fluid-topology repeats were added after their initial large screening effects; original runs remain included.

FPS is elapsed frames / elapsed time. References are interpolated in log frame time between neighbouring control runs. The changed setting is generally OFF/native; factory cadence instead changes divisor 1 to 2. Positive cost means the changed setting was slower. FPS columns show the paired observation with the median effect, so they refer to the same run; percentages show the median and observed range across runs. These conditional effects overlap and must not be added. Small effects, isolated screens and unexercised paths do not establish a benefit.

| Feature | Changed setting FPS | Reference FPS | Changed frame-time cost | Runs |
| --- | ---: | ---: | ---: | ---: |
| picking | 90.73 | 90.53 | -0.22% (-0.27 to +0.67) | 3 |
| factory-cadence | 95.88 | 93.86 | -2.11% (-4.34 to +0.04) | 3 |
| stable-signals | 93.35 | 94.08 | +0.77% (+0.33 to +1.32) | 3 |
| signal-queries | 94.69 | 94.19 | -0.53% (-1.80 to +0.66) | 3 |
| single-player-rooms | 90.16 | 88.65 | -1.67% (screen) | 1 |
| incremental-rooms | 89.91 | 88.51 | -1.56% (screen) | 1 |
| physics-transforms | 94.42 | 94.46 | +0.05% (-1.73 to +0.66) | 3 |
| stateful-queries | 88.63 | 88.92 | +0.33% (screen) | 1 |
| native-state-reuse | 89.40 | 89.13 | -0.31% (screen) | 1 |
| world-snapshots | 89.40 | 89.34 | -0.07% (screen) | 1 |
| cable-buffers | 89.74 | 89.64 | -0.11% (screen) | 1 |
| grid-bounds | 68.28 | 90.34 | +32.30% (+31.88 to +34.53) | 3 |
| fluid-topology | 61.64 | 90.62 | +47.02% (+46.16 to +47.29) | 3 |
| part-visuals | 88.98 | 89.90 | +1.03% (screen) | 1 |
| tool-refs | 90.16 | 89.98 | -0.20% (screen) | 1 |
| distiller-slots | 89.26 | 90.04 | +0.87% (screen) | 1 |
| world-visuals | 89.09 | 90.02 | +1.05% (screen) | 1 |
| client-signals | 90.66 | 90.01 | -0.72% (screen) | 1 |
| world-ui | 91.59 | 94.03 | +2.66% (+1.99 to +3.43) | 3 |
| ui-culling | 87.09 | 89.97 | +3.32% (screen) | 1 |
| ui-distance | 90.22 | 90.81 | +0.66% (screen) | 1 |
| ui-refresh | 89.73 | 91.55 | +2.03% (screen) | 1 |
| renderer-detail | 93.18 | 93.98 | +0.86% (-0.32 to +1.50) | 3 |
| tiny-meshes | 94.23 | 93.04 | -1.26% (screen) | 1 |
| shadows | 93.21 | 93.80 | +0.63% (screen) | 1 |
| mesh-batching | 81.29 | 94.43 | +16.17% (+15.22 to +16.76) | 3 |
| terrain | 91.27 | 94.15 | +3.15% (+2.81 to +3.72) | 3 |
| cable-refresh | 92.99 | 94.12 | +1.21% (screen) | 1 |
| loader-cache | 94.47 | 93.98 | -0.51% (-0.56 to +0.09) | 3 |
| packet-trailer | 94.63 | 93.85 | -0.82% (screen) | 1 |

Reference mean frame times ranged from 10.570 to 11.330 ms. This range describes observed drift, not a confidence interval. Per-run frame tails and validity checks are retained in [feature-evidence.json](feature-evidence.json).

Native probes: 70 check receipts; 0 failed checks; 0 probe error files. Failures, if present, are retained in the evidence JSON and must be resolved or explicitly classified before release.

## Behaviour and recommendations

“Keep within scope” means the tested preservation contract passed. “Conditional keep” means no failure was established but required dynamic/integration coverage remains open. Neither is a whole-game proof.

| Feature | Recommendation | Behaviour evidence and limits |
| --- | --- | --- |
| factory-cadence | Discard from the preservation profile; use divisor 1 | Known meaningful failures: missed one-tick signal pulse, feedback advances half as often, lost factory time on transitions into native-rate controls, and different power-interruption output. A performance gain cannot qualify this as equivalent. CadenceEvidenceTests, FactoryOutcomeTests, StableCircuitExecutionTests; actual installed feature ablation. |
| picking | Discard throttle from preservation defaults; use divisor 1 | Actual camera gate adds up to two rendered frames of ID-buffer age in the calibrated native render-target check. Native interaction consumes that buffer. No wrong native click was demonstrated; responsiveness is not certified. Native red/green camera calibration with shipping gate enabled/disabled, after FPS capture. Forced readback wall times are excluded from ordinary input-latency claims. |
| stable-signals | Keep conditionally at native factory cadence | Native and configured circuits match intermediate state in the tested changing/stable chain, including variable step lengths; redundant callbacks deliberately coalesce. Observer-mod compatibility and arbitrary topology are not established. Native Mono circuit differential plus StableCircuitExecutionTests and StableSignalTests. Factory cadence is held at 1. |
| signal-queries | Keep within inspected scope | Scoped input projection retains native execution ordering and live values in the tested circuit. Topology invalidation and role guards also have offline contracts. Native circuit differential with actual installed projection, SignalEvaluationTests and PatchCompositionTests. |
| single-player-rooms | Keep within tested native and loopback scope | Forwarded queries matched authoritative geometry through actual hull removal/restoration. On real second-process join the local tree rebuilt from one leaf to 208,279; host-local/remote geometry and disconnect checks passed with forwarding enabled and disabled. Native client mirrors can transiently lag after edits. WAN/Steam transport, gas replication and remote production remain open. Native real-ship query fixtures, matched-binary two-process loopback checks and SinglePlayerRoomTests. |
| incremental-rooms | Keep with documented numerical tolerance and limits | Repeated topology/volume/seed and gas-identity checks pass. Floating-point atmosphere differs slightly from native; weighted-reference error and repeated-edit drift are documented. Universal threshold equivalence is not claimed. IncrementalRoomTests, native synthetic edit/join/breach/reseal/gas-reload fixtures, actual real-ship hull edits. Targeted edit timings are separate from stationary FPS. |
| physics-transforms | Review/split redundant setters from scene caching | Forced-gravity dynamic bodies matched position, velocity, rotation and sleep state exactly in six paired 600-step scenarios. Same-delegate setter benchmarks measured approximately 20 ns native versus 37 ns guarded. Whole-feature frame effect is inconclusive; review/split setters from scene caching. No demonstrated numerical physics regression; full ship interpolation/docking/space reassignment remain open. Matched-binary native dynamic bodies with floor arrival and sleeping counts, fair alternating-order benchmarks, three whole-feature frame comparisons and PhysicsSceneTests. Initial zero-gravity floor probe does not prove collision/reactivation coverage. |
| stateful-queries | Keep within inspected mutation scope | Native membership, iteration order and live part state agree after tested add/remove and reused IDs. Snapshot lifetime and arbitrary mod mutation are outside this guarantee. Native 2,000 mutation/query comparisons with actual installed cache hooks, QueryCacheTests, all 25 patched call-site contracts. |
| native-state-reuse | Keep within inspected record types | Preserves field bits and native publications while reusing equal immutable records. Custom derived records retain fallback clone semantics. Reference-identity observers remain a compatibility distinction. Native circuit state/event comparisons and NativeStateReuseTests, including signed zero and NaN payloads. |
| world-snapshots | Conditional keep; broader runtime mutation coverage open | Scoped reuse has offline membership/order, nesting and cleanup contracts. Stationary native run exercises normal loading, not all despawn/travel mutations. SnapshotPatchTests, native frame ablation; no full world-lifecycle certification. |
| cable-buffers | Conditional keep | Exact-length cleared scratch buffers retain tested mesh data and nested-call separation. Dynamic native wiring/mesh-upload coverage remains open. CableScopeTests, ReuseTests and native frame ablation. |
| grid-bounds | Keep within inspected mutation scope | Native bounds match after 1,000 actual grid mutations across positive/negative coordinates and chunk boundaries. Native stateful hull additions route through the hooked SetPart path; removals have their own hook. Native mutation differential and cached-read timings; QueryCacheTests; inspected native mutation call paths. |
| fluid-topology | Keep guarded topology cache; review cosmetic refresh separately | Reuses visual topology while live flow values remain live. Offline topology invalidation passes; actual wiring/disconnect/retype and flow-reversal visual acceptance remain open. Disabling this feature also removes its fluid-flow refresh reuse: the ablation includes this interaction with CableVisualHz; it does not isolate topology caching at identical visual refresh frequency. CableTopologyTests, native frame ablation and screenshots. |
| part-visuals | Conditional keep; moving presentation coverage open | Equal-state work is deferred; simulation remains native. Offline changed-state and liquid-level contracts pass. Static views cannot establish all movement/billboard/refill transitions. PartVisualTests, native frame ablation and screenshots. |
| tool-refs | Conditional keep for inspected ModKit version | Cached references preserve tested tool-switch and replacement behaviour. ModKit adapter is version/owner guarded; broad native tool switching remains open. ToolRefsTests, OptionalModTests and native module/ablation receipts. |
| distiller-slots | Keep within equal-slot contract; active-chain coverage open | Equal inventory arrays reuse identity; changed quantities and content retain the new array. Offline comparison passes. This is not an active native distiller production-chain certification. The native candidate array is still constructed before the replaced setter; this is not evidence that candidate-array allocation was eliminated. DistillerSlotTests and inspected native call-site composition; stationary frame ablation may not exercise active distillers. |
| world-visuals | Conditional keep; travel/unload coverage open | Exact transform/visual reuse has offline contracts; stationary native view supplies limited integration evidence. ManagedPatchTests/PartVisualTests and native frame ablation; no broad travel/docking claim. |
| client-signals | Conditional keep; native rewiring coverage open | Socket/membership projection preserves tested live values and supported/custom fallback behaviour. Dynamic native wire changes remain a release coverage gap. CableTopologyTests, OptionalAdapterTests and native frame ablation. |
| world-ui | Conditional keep of culling; review presentation delays | Hidden UI is deferred and numeric readouts refresh at 10 Hz unless focused. This intentionally changes visible timing; focus/edit and low-FPS responsiveness are not proven by static screenshots. WorldUiAndBatchingTests/VisualTradeoffTests, native frame ablation and captured views. |
| ui-culling | Conditional keep; visibility transitions remain open | Offscreen canvas suppression has restoration/focus contracts. Multi-camera and native moving-view transitions need broader acceptance. WorldUiAndBatchingTests/VisualTradeoffTests and native frame ablation. |
| ui-distance | Demote distance cap pending perceptual acceptance; prefer native | 100 m cap intentionally omits distant readouts. It cannot receive an imperceptibility claim solely from a stationary near-field view. Zero restores native distance. VisualTradeoffTests; isolated distance ablation. Actual distant-display acceptance remains open. |
| ui-refresh | Demote refresh cap pending dynamic acceptance; prefer native | 10 Hz numeric display refresh adds a nominal refresh interval of delay plus frame scheduling; focused controls bypass throttling. Simulation values are live but readout timing differs. PendingDisplay transition tests; isolated refresh ablation. Native dynamic-readout input latency remains open. |
| renderer-detail | Conditional visual keep; perceptibility remains unproven | Tiny geometry and distant shadows are intentionally omitted. Multi-camera conservative rules, near restoration and hysteresis have offline tests; view samples do not cover threshold travel or all lighting. VisualTradeoffTests, three-view captures and native ablation. |
| tiny-meshes | Conditional visual keep; threshold acceptance open | Default 3-pixel threshold can omit small distant meshes. Offline projection/hysteresis and near-field protection pass; popping across distance/FOV/resolution is not certified. VisualTradeoffTests and isolated threshold ablation. |
| shadows | Demote shadow cap pending visual parity evidence | Default shadow-distance cap intentionally removes shadows beyond 40 m. These can be perceptible even when meshes are small; stationary FPS benefit is insufficient evidence of visual parity. VisualTradeoffTests, isolated shadow-distance ablation and captured views; moving daylight acceptance remains open. |
| mesh-batching | Conditional keep; dynamic restoration coverage open | Supported opaque renderers combine while gameplay objects/colliders stay present. Native startup logs establish actual replacements; static views do not certify hover/highlight, moving parts, lighting changes or all edits. Matched-build native highlight/removal calls restored five source renderers and retained the part object/state; the selected visual root had zero colliders, so no positive collider-identity result is claimed. WorldUiAndBatchingTests, native renderer inventory logs, frame ablation and screenshots; real hull edit integration. |
| terrain | Conditional keep; travel/terrain edit coverage open | Reuses LOD traversal for small observer movement with a 15-frame maximum interval. Intended target is presentation; unchanged observer does not prove arbitrary terrain edit/collision behaviour. FrameCadenceTests and inspected visual call site; native stationary ablation and views. No travel/collider certification. |
| cable-refresh | Demote fluid refresh throttle pending dynamic acceptance; keep graph cache | Fluid cosmetic values refresh at 20 Hz while animation runs each frame. Skipped signal passes still update glow from live values. Fluid refresh timing can differ by a nominal 50 ms plus frame scheduling; fast flow changes and low-FPS visual acceptance remain open. FrameCadenceTests/CableTopologyTests and native frame ablation; no electrical/fluid simulation cadence change from this switch. |
| loader-cache | Keep for inspected loader version | Replaces repeated reflection with the same sibling-order calls at the inspected update position. Version/layout guards fail closed; future loader versions require renewed inspection. EntryPointTests/NativeContractTests and native runtime on/off module receipts. |
| packet-trailer | Keep as bounded framing hardening; no FPS claim required | Capacity correction preserves native channel framing/payload under tested boundary lengths. This is not evidence of reduced bandwidth or WAN reliability. TrailerBoundaryTests, TrailerRegressionTests and native installed frame ablation; native loopback peer follow-up exercises the installed transport stack. |

## Inactive switches

`ContentMembership` and `ScopedHullMeshReads` are guarded out because StellarWorkshop owns their inspected paths in this installed stack. They have no incremental active effect here; their core-only benefit/compatibility was not measured by these captures. `FluidGraphSocketReads`, `BatchingContinuousValidation` and `Diagnostics` are disabled by default and excluded from default-feature benefit claims.

## Scope

This is one Windows machine, one native game version, one dependency stack and one stationary world/view for FPS. The active synthetic checks run after timing. They do not prove all production chains, saves, travel, docking, user interactions, observer mods, hardware or network transports. Existing factory/room differential evidence is linked in [CLAIMS_AUDIT.md](CLAIMS_AUDIT.md); exact omissions remain release work, not implied passes.

Private raw logs, screenshots, immutable seeds, driver source/binary, configuration receipts and restoration hashes are retained in the sibling `private/feature-audit` directory. They are excluded from packaging.
