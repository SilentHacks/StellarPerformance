> Historical 0.9.0 audit. The resulting five-feature 1.0.0 candidate is described and measured in [BENCHMARKS.md](BENCHMARKS.md). Removed implementation fixtures are preserved in the private starting-checkout snapshot; old results do not apply to the new DLL.

# 1.0.0 claims register

This register separates intended contracts from evidence. An offline pass does not
establish Unity integration, arbitrary mod compatibility or a whole-game FPS gain.
Current decisions and measured counterexamples are in [the claims audit](CLAIMS_AUDIT.md).

## Required contracts by feature

| Settings / feature | Intended contract or accepted change | Evidence required before a broad claim |
| --- | --- | --- |
| `WorldObjectSnapshots` | Same world objects and native synchronization order within the scoped pass | Compare membership/order and mutation/exception cases; measure copy count, allocation and complete server work |
| `CableMeshBuffers` | Exact-length cleared scratch arrays; same uploaded mesh data | Compare vertices/indices and nested uploads; bounded retention; native render check and allocation measurement |
| `ContentMembership` | Membership cache invalidates; definitions and state stay live | Definition replacement, add/remove and optional-mod contracts; runtime dependency combinations |
| `ScopedHullMeshReads` | Same mesh reads within one rebuild | Mesh replacement/rebuild/nesting and Workshop precedence; native geometry and rebuild timings |
| `ShipGridBounds` | Exact bounds after grid mutations | Positive/negative coordinates, all mutation paths, unload/reload; compare native bounds |
| `StatefulPartQueries` | Same collection membership after part changes | Add/remove/type changes and lifetime transitions; native query comparison and retained memory |
| `FluidCableTopology` | Same visual topology; live flow state | Wire/connect/disconnect/retype/part removal, flow reversal and travel; visual restore and allocation timings |
| `PartVisualUpdates` | Equal-state reuse; deferred redundant presentation | Changes, liquid levels, transforms, billboard movement and re-entry; view comparisons |
| `ModKitToolRefs` | Same currently selected tools | Tool switch, missing/replaced tools and inspected optional-mod runtime |
| `DistillerSlotReuse` | Equal slot contents; native state/update semantics | Item identity, quantity, ordering and native update/network observations |
| `PhysicsSceneTransforms` | Exact-equal writes only; native physics cadence | Scene creation/destruction, docking, motion and collision outcomes; native-runtime integration |
| `WorldVisualUpdates` | Same transforms and cleanup | Moving worlds, hotspots, unload and repeated rebuilds; native visual comparison |
| `ClientSignalCables` | Same socket/membership projection; values remain live | Wiring changes, supported/custom sockets and native visual values |
| `NativeStateReuse` | Same field bits and required events; record identity may be reused | Native differential state/event tests, nonfinite values, optional observer contracts |
| `SignalEvaluationReads` | Native value and operation ordering; cache invalidation | Native differential traces with changing topology and roles; allocation and total update cost |
| `StableSignals`, `StableSignalRecheckSeconds` | Same supported values per factory step; repeated identical callbacks coalesce | Long differential state/event traces, feedback/derivatives, topology and observer mods; timing measured separately |
| `IncrementalRooms` | Exact connectivity/geometry/volume and usable saved coordinates; gas rounding differs | Repeated edits, unequal-density joins, breach/reseal and save/reload; error trend, gas identity, conservation and threshold outcomes |
| `SinglePlayerRooms` | Authoritative queries forwarded; presentation timing can change; native mirrors return when required | Door/edit queries, real peer join/disconnect, debug enable, reconstruction hitch and memory |
| `ShipPartBatching`, `BatchingContinuousValidation` | Supported opaque renderers combined; gameplay objects/colliders retained | Placement/removal/highlight/hover, lighting/material changes, travel/docking/reload and peer visibility |
| `WorldUiVisibility`, `CullOffscreenWorldUi`, `WorldUiMaximumDistance`, `WorldUiRefreshHz` | Hidden/distant displays deferred; numeric readouts at 10 Hz; focused controls responsive | Visibility/focus/edit transitions; screenshots and bounded input-to-feedback latency at slow/fast frame rates |
| `RendererDetail`, `MinimumDetailPixels`, `PartShadowDistance` | Tiny distant geometry and distant shadows omitted | Camera/FOV/resolution changes, popping, lights/shadows and near-field restoration; visual acceptance plus frame timings |
| `TerrainTraversalCadence` | LOD selection reused for small movement, at most 15 frames | Travel, terrain edits, graphics changes and outstanding deltas; no missing collision/interaction dependencies; native runtime |
| `CableVisualHz` | Cosmetic values at 20 Hz; animation per frame | Flow/signal changes, frame-rate dependence and measured visual lag |
| `PickingCameraDivisor` | ID camera every third frame | Correct selection/click after rapid aim or object movement; stale-target checks and latency in milliseconds |
| `PartSimulationDivisor` | Factory advances every second tick using a larger step; control states bypass cadence | Elapsed-time accounting, discrete production, pulses, feedback, power/fluids, ordering and transitions; known counterexamples currently prevent equivalence |
| `LoaderReflectionCache` | Same native sibling ordering without repeated reflection | Inspected loader installation, execution position, exceptions, UI ordering and allocation measurement |
| `PacketTrailerHardening` | Native channel framing and payload; safe capacity | Every boundary length, native parser roundtrip and host/client transport; no bandwidth/congestion claim |
| `FluidGraphSocketReads` (off) | Experimental socket reuse within one fluid update | Prefab mutation/custom callbacks and native output comparison; excluded from default-profile claims |
| `Diagnostics`, `DiagnosticMode`, `CaptureSeconds` | Off by default; bounded opt-in probes | No normal feature-path diagnostic initialization; identical measurement overhead in paired runs; unavailable counters stay unavailable |

## Acceptance rules

Exact-preservation contracts require matching results and observable required events
at the relevant synchronization point. Allocation identity and internal room IDs need
not match if they are outside the contract; room connectivity must match under a
bijective room-ID mapping. Native state equality must be complemented by event traces.

Hard failures include changed topology, invalid saved coordinates, duplicated/lost
discrete items, wrong selection/actions, unexpected missed signal events, broken save
reloads and persistent host/client disagreement. Keeping a save or network *format*
unchanged is insufficient to establish equal saved state or multiplayer behaviour.

Numerical approximation requires absolute and relative error, intermediate and final
state, event timing, repeated-action drift and an independent physical/accounting
reference where available. A numerical margin is approved only after checking the
actual affected game thresholds. A small average error does not excuse a different
threshold decision. No universal percentage tolerance is currently approved.

Presentation changes require explicit visual acceptance and an input-to-feedback
budget in milliseconds, including slow frame rates. A frame divisor is not itself a
latency bound. Static screenshots cannot prove control responsiveness.

## Remaining runtime matrix

Every row needs a native/conservative/default comparison with identical starting
world, dependencies and timestamped inputs. Compare simulation at matched elapsed
time, allowing cadence phase differences only within a stated timing budget.

| Workload | Required observations | Current breadth |
| --- | --- | --- |
| Small and large stationary ships | Raw frame intervals, allocation, memory, loaded workload and view | Absent/conservative/default pilot plus 68 per-switch/control captures in one 20-ship world; see feature audit |
| Active factory and scarce resources | Discrete output count, consumption, power, fluid inventory and completion times | Offline and native Mono electrolyzer fixtures; native-rate intermediate states/events match; full factory networks remain open |
| Signals and thresholds | Intermediate outputs, edge/event count, feedback, derivatives, topology changes | Offline and native 64-gate differential traces; cadence has counterexamples; broad topology/observer coverage remains open |
| Construction and rooms | Real panels/doors, partition/join, breach/reseal, bulk edits, gas and save/reload | Native hull edits, synthetic join/breach/reseal/gas reload and repeated managed edits; broad doors/construction/save reload remain open |
| Travel and interactions | Docking, ship movement, terrain LOD, hover/click, batching restoration and unload | Native highlight restoration and calibrated picking-buffer age checked; real clicks, travel/docking and moving LOD remain open |
| Multiplayer | Real remote join/disconnect, edits, production, transport and client reconciliation | Real second-process loopback join/edit/disconnect and room-mirror restoration passed; WAN/Steam transport, gas replication and production remain open |
| Graphics and dependencies | Low/high frame rates, resolutions/FOVs, dependency combinations and core-only install | Native captured views and inspected guards; dynamic visual/input acceptance and core-only performance remain open |

## Performance protocol

Freeze the build hash and declare the workload, duration, warm-up, repetition count,
run ordering, primary metric and exclusion rules before collecting the comparison.
Restore each run from the same immutable seed. Verify actual settings and installed
modules, focus, view, graphics, dependencies and workload activity. Keep save workloads
as a separate declared scenario; preserve and report incidental saves and errors.

Use absent-mod, conservative and default profiles. Isolate individual switches in
follow-up runs to attribute a result; whole-profile differences do not identify the
responsible feature. Whole-game measurements retain normal rendering, physics and
simulation. Profile allocations/subsystems separately from frame traces. Never add
overlapping wall-clock scopes or treat skip counts as performance measurements.

Use repeated matched runs as the statistical unit. Report per-run elapsed-time FPS,
mean frame time, p95/p99, the explicit slowest-1%-mean FPS definition, long-frame rates,
allocation/GC and retained memory. Do not average raw FPS samples, pool percentiles
across runs or treat thousands of frames as thousands of independent experiments.
Show uncertainty and every run, including regressions. A three-block pilot is
exploratory; it cannot establish a universal improvement or a representative tail.

For raw traces, `scripts/CompareFrameTraces.py` checks matched blocks, starting-world
hashes, focus, durations, completion receipts, save-file writes and runtime errors.
It retains all frames and runs, and suppresses bootstrap intervals below six pairs.
It cannot establish matched camera/workload activity by itself. The existing capture
comparison script remains available for subsystem diagnostics; its per-capture
summary averages are not raw-frame pooled tails.

## Release decision

1. Keep exact-preservation failures at zero in their supported contract.
2. Resolve known meaningful default-profile counterexamples, or change the advertised
   contract/profile explicitly. A green characterisation test is not an equivalence pass.
3. Approve approximation margins against actual gameplay consequences before declaring B.
4. Require measured practical benefit for each default trade-off; demote features whose
   checks cost more than they save, using native runtime evidence.
5. Scope A to the workloads and metrics actually measured. Scope compatibility to the
   inspected/tested combinations. Retain untested matrix rows as open requirements.
6. Record the final shipping DLL hash, settings, test receipts, limitations and source
   revision. A changed candidate requires a new matching assessment.
