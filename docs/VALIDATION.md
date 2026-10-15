# Validation and limits

## 0.9.0 release preparation

The Release net472 build passes with warnings treated as errors. The offline suite
passes 280 cases with the inspected optional mods enabled; the core run passes 276
with four explicit adapter skips. Tests execute emitted native method copies and
managed contracts on .NET 9, without installing Harmony/MonoMod detours on CoreCLR.
They also inspect the actual shipping DLL's versions, loader metadata, embedded MIT
and credit notices, and absence of optional-mod assembly dependencies.

The shipping DLL SHA-256 is:

```text
4303607E772BBA28A0D6A50C895B16E7C98EE422A4F4625DD6DA32FE6895C279
```

Release cleanup normalizes source line endings to the repository's LF convention and
disables implicit source-link metadata in the shipping project so source archives without
`.git` reproduce the DLL. These changes alter compiler/debug identities. Compared with the latest isolated Unity/Mono
candidate (`B01F3FC9D639AB8F38F9ACC711AA35158E3BD6EF21499C98ECDFC0B141A01627`),
all 885 method bodies, embedded resources and runtime metadata except the module identity
are byte-identical. This rebuild has not been separately launched in Unity/Mono.
The ZIP hash, source commit and exact entries are recorded separately
in `artifacts/0.9.0/verification.json` by [the packaging script](../scripts/Package.ps1).
Builds and packaging do not deploy or launch the game.

## Native evidence for the matching runtime code

Validation used StellarDrive Demo 0.8.2, MelonLoader 0.7.3 and an isolated copy of a
backed-up world, retaining that world's content dependencies. The latest host startup
installed the cadence, stable-signal, single-player room and visual modules. Workshop's
existing membership and hull caches took precedence over duplicate Performance caches.
Offline optional-adapter tests also cover the inspected SDModKit and Item Lines builds.

- Single-player room query reuse retained 20 empty client roots. Compared with the same
  scene retaining native mirrors, room nodes/lists/arrays fell by 226,095,424 bytes and
  approximately 3.68 million objects. This measures room structures, not process working set.
- All 519,973 sampled authoritative/client room results matched. Reconstructing the largest
  loaded ship's native client mirror matched room/wall classification at 208,279 samples.
  Live hull removal/replacement also preserved forwarded queries. Door interaction and a
  real remote-player join were not exercised in this check.
- Raising the signal connection cache limit from 128 to 1,024 entries retained its 4 MiB
  ceiling. The final scene retained 190 groups using 139,264 budgeted bytes. In separate
  scoped eight-second runs, allocations inside connection-union reads fell from about
  1.02 MB/s to zero, and total process allocation fell from 11.64 to 10.32 MB/s.
- Focused, uninstrumented 45-second traces of the native-mirror and final configurations
  recorded average FPS 91.60/91.60 and 1% lows 47.40/47.51. These short runs establish no
  whole-game FPS or 1% low improvement.

Allocation scopes can overlap and include background allocations; they were measured
separately from frame traces. The private cumulative allocation counter was checked
against known allocations because Unity's exposed per-thread counter returned zero in
this build. Exploratory captures overlapping development, an unfocused capture and
captures with allocation probes are excluded from FPS comparisons. All 74 original world
files were verified unchanged. Raw evidence, drivers, worlds and backup receipts remain
outside the repository and release archive.

## Behavior tradeoffs

All settings below are reversible; see [configuration](CONFIGURATION.md) for defaults,
bounds, dependencies and native-scheduling overrides. Saved choices survive upgrades.

| Feature | Tradeoff and fallback |
| --- | --- |
| Factory cadence | Default divisor 2 advances signals, fluids, machines, power and rooms every second network tick with a doubled step. Global physics/network ticks and save/network formats stay native, but discrete thresholds, derivatives, feedback, resource transfers and fluid relaxation can differ. Piloting, lever use and ship repositioning restore every-tick processing. Entering these states resets the cadence without catching up a pending skipped tick. Set `PartSimulationDivisor = 1` for native timing. |
| Stable signals | Compare live inputs bit for bit in native operation order on each factory step. Unsupported states, nonfinite inputs and derivatives execute on each such step. Identical callbacks are coalesced while asleep, which can affect mods using them as a clock. Stable values refresh at most every 250 ms of simulation time by default; topology changes invalidate observations. Busy circuits back off to native execution to limit checking overhead. Set `StableSignals = false` to disable. |
| Single-player rooms | Position queries use authoritative room geometry without sharing the mutable tree. Presentation can observe a server edit before mirrored network state arrives. A second connection or active room-debug drawing rebuilds native client mirrors; rebuilding can hitch. The reconstruction mechanism was checked, but a real peer join remains untested. |
| Incremental rooms | Local room reconstruction preserves valid saved coordinates and native weighted gas/vacuum rules, with a different rounding order on the local path. A prior synthetic repeated-edit fixture measured about 0.00056 atmosphere difference. This is not bitwise gas equivalence or full construction coverage. Conflicting room rewriters retain their behavior. |
| Mesh batching | Combine supported fixed opaque bodies; retain native gameplay objects, colliders, ID rendering, liquids, text and effects. Unsupported geometry remains native. Interactions release affected batches, and batching may remain reduced until a topology edit. Broad building, travel and multiplayer coverage remains open. |
| Renderer detail and UI | Keep full nearby geometry within 20 m, omit tiny distant meshes, remove distant shadows, limit part UI to 100 m and coalesce numeric readouts to 10 Hz by default. Popping or brief stale presentation is possible. Focused fields, control changes and UI re-entry refresh immediately. Hidden registered canvases also suppress clipping and restore it on re-entry. |
| Terrain cadence | Reuse LOD selection for small observer movement, refreshing at least every 15 rendered frames; reuse completed empty mesh deltas between traversals. Flushes and outstanding deltas remain live. Small movements or edits can wait until a refresh. Extended travel, terrain editing and graphics changes need broader coverage. |
| Cable and picking cadence | Cosmetic cable values refresh at 20 Hz while animation continues each frame. The object-ID camera renders every third frame; hover/interaction feedback can arrive a few frames later. Native rates are available through settings. |

The removed `SleepingMachinery` experiment is not part of 0.9.0. Sampled main-thread
profiles showed machine updates rising from about 0.04 to 0.11 ms per rendered frame
with that experiment, while stable signals provided the measured subsystem saving.
Old machine-sleeping preference keys are ignored. Factory cadence is a separate,
shipped approximation and must not be described as unchanged simulation scheduling.

## Offline coverage and remaining gameplay checks

The suite covers native signal-loop propagation, feedback, math/unary operations,
transmitters, tiny/nonfinite inputs, changing delta times, busy-circuit backoff, retained
wiring, topology invalidation, nested execution and exception cleanup. It checks room
forwarding/restoration, visual thresholds and hysteresis, UI focus/re-entry, terrain
traversal rewriting, cadence decisions, cable-state equality, private-binding guards,
bounded storage and patch-composition contracts.

Prior native fixtures on earlier 0.8/0.9 artifacts exercised synthetic room edits, occupied
seeds, partitions/joins, breach/reseal, gas reload, renderer restoration and highlight
callbacks across the 13 body types present in that world. Those checks provide supporting
history; they do not establish every hook or workload on this DLL. Earlier stationary
captures of the 0.8 visual profile also do not establish a general 0.9.0 FPS gain.

Remaining coverage includes:

- Full panel placement/removal, doors, bulk building and save/reload after actual edits.
- Extended travel, docking, loading/unloading and repeated interactions with batching.
- Factory throughput, resource conservation, derivatives and feedback over long runs with
  the larger simulation step, including transitions into and out of flight/control states.
- Real remote host/client joins, construction, fluid/signal consistency and transport edges.
- Broader graphics configurations, game versions and third-party mod combinations.

Compatibility uses inspected assembly identities and guarded module installation.
Conflicting replacements or argument rewrites are skipped. Observer hooks can coexist
where callbacks remain active. Changing patches after initialization is outside the
supported contract except for the incremental-room dirty-update owner recheck. A
successful startup cannot establish arbitrary mod compatibility. Native operation
without SDModKit has not been established by these world-based runs; core offline tests
exercise the build without optional-mod references.

## Repeating checks

Use an isolated copy of a backed-up world. Record the exact DLL hash, settings, optional
mods, graphics settings, view, warm-up, workload and capture mode. Compare the same build
with only the intended switch changed. Validate startup messages before interpreting a
capture; exercise edits and reload correctness separately from frame-time measurement.

Diagnostics default off. `Frame` avoids timing hooks but still adds bookkeeping.
`Lean` and `Full` measure overlapping wall-clock paths; do not add their times together.
Full mode adds detailed state/byte/transform probes. Feature counters run only during
an explicitly started capture. Do not infer FPS gains from skip counts or offline tests.

`scripts/ComparePerformanceCaptures.ps1` accepts logs with explicit capture selections.
It flags incomplete reports, mismatched modes/workloads/durations, save activity and
unavailable counters. Its summary averages per-capture summaries; percentiles and 1% lows
are not pooled. Keep raw evidence private and report only the result supported by it.
