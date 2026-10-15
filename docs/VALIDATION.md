# Validation and limits

## 0.7.0 candidate

The Release net472 DLL builds with warnings treated as errors. The offline suite executes
the retained native IL transformations and managed semantics without installing runtime
detours on .NET 9. Dedicated checks cover inactive-space retention, private-binding
initialization, replacement-hook policy, manifest/assembly/loader version agreement,
optional dependency absence in the shipping assembly, and embedded MIT/credit notices.

The full optional-mod suite currently contains 251 passing cases. The default core run
explicitly skips four adapter cases. Removed experimental collection tests are no longer
counted; the earlier 258-case count describes 0.6.0, not this candidate.

The current candidate DLL SHA-256 is
`5B6B4AA334B1A4C8235F1D6A38E7B6DB0BD7E277383E39D02EFCB21EC01FC686`.
It also disables feature diagnostic counters outside explicit captures. The Release build
and offline suite cover this follow-up, including native record reuse and room reconstruction
with counters both enabled and disabled. This follow-up DLL has not been rerun in Unity/Mono.

The preceding candidate DLL SHA-256 was
`B5330DB60DC6E713622BF0A72877E8CA8EBC5633FC6A0C585EAB6FFC408C47D5`.
Native Unity/Mono validation of that preceding DLL on an isolated world copy established:

- Default feature settings, with Frame diagnostics enabled solely for the driver, installed
  all expected modules without Workshop. SDModKit and the world's other content dependencies
  were present. The membership and hull paths installed instead of being skipped. Startup,
  stationary operation, camera turn/return and normal exit completed without player-log exceptions.
- With the original mod set, optional features enabled and Full diagnostics, all eight probe
  groups installed. The two expected Workshop duplicate-cache warnings were retained. Repeated
  synthetic room edits, occupied old seeds, partition, join, breach, reseal and native gas
  reload passed. Batching combined 1,649 source renderers into 84 replacements; native highlight
  and remove-highlight restored the affected part correctly in inspected Unity screenshots.
- The game exited normally after every run. All 64 original mod/configuration/world files,
  including all 45 pre-existing world files, were hash-verified after restoring the prior
  installation. Only the newly created isolated test-world copy was archived afterward.

An earlier exploratory no-Workshop run also removed required content mods and logged missing
custom part IDs. It was excluded from clean gameplay and performance evidence and replaced
by the corrected dependency-preserving run. No new FPS improvement is claimed from these
smoke runs. Native operation without SDModKit remains untested; the separate core-only
reference-tree build/test passed with no optional-mod directory (245 passed, four explicit skips).

Private driver code, game logs, screenshots, worlds and receipts are intentionally outside
the repository. They are not included in source history or release archives.

## Remaining gameplay coverage

- Actual panel placement/removal, doors, bulk building and save/reload after room edits.
- Extended ship travel, docking, loading/unloading and interactions with batching enabled.
- Remote host/client construction, fluid/signal consistency and real peer transport boundaries.
- Broader graphics configurations and third-party mod combinations.

Incremental rooms and mesh batching remain opt-in. Native synthetic octree tests are not
full ship construction tests. Inspecting transformed methods does not prove every Unity
hook's runtime installation. A known compatible startup does not prove arbitrary mod
coexistence or compatibility with later game builds.

## Historical evidence

These measurements describe older local artifacts and specific controlled scenes. They
are context for retaining features, not 0.7.0 performance claims.

- 0.5.0 batching experiment: stationary average FPS 59.20 to 77.64, with the same isolated
  save seed and 75-second settling protocol, compared across mod versions. Its final
  interaction-restoration revision was smoke-tested separately, not separately benchmarked.
- Final 0.6.0 room artifact: synthetic 40×40×40 voxel shell, 24 samples per operation.
  Median native/mod room-update milliseconds: interior add 7.2487/0.3378, interior remove
  6.0758/0.1089, exterior add 7.6647/0.1416, exterior remove 6.2082/0.1469. These do not
  measure complete building, collider, mesh or frame latency.
- The local room path's double-precision aggregate arithmetic follows native weighted
  gas/vacuum rules but changes rounding order. After warm-up and 24 interior add/remove
  cycles, density was 0.36984 versus native 0.36928, about 0.00056 atmosphere apart.
  General reconstruction retains native float accumulation order. Large volumes fall
  back to general reconstruction. Do not claim bitwise gas equivalence.
- Prior signal projection and fluid socket-read experiments reduced measured subsystem
  work. Their single comparisons do not establish universal FPS gains.
- Removed scratch-collection experiments did not demonstrate an FPS benefit worth their
  maintenance cost in the recorded comparisons.

## Repeating useful checks

Use an isolated copy of a backed-up world. Record the exact DLL hash, settings, optional
mods, graphics settings, view, warm-up, workload and capture mode. Compare the same build
with only the intended switch changed. Validate startup messages before interpreting a
capture. Exercise edits and reload correctness separately from frame-time measurement.

`Frame` diagnostics avoid timing hooks but still add bookkeeping. `Lean` and `Full` hooks
measure overlapping wall-clock paths and must not be added together. Full mode serializes
states for diagnostic comparisons and can materially alter timing. Renderer references
are not draw calls. Unavailable allocation/GPU counters are unavailable, not zero.

`scripts/ComparePerformanceCaptures.ps1` accepts baseline and candidate logs with explicit
capture selections. It flags incomplete reports, mismatched modes/workloads/durations,
save activity and unavailable counters. Its summary averages per-capture summaries;
percentiles and 1% lows are not pooled. Keep raw evidence private and publish only the
specific, reproducible result supported by it.
