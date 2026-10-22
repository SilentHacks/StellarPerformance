# Batch recovery (2.0.0)

Previously, native hover or highlighting permanently dismantled an affected mesh
batch. Interacting with more parts gradually lost the rendering benefit. Recovery
now restores unchanged geometry after the interaction ends.

## Behavior and tradeoffs

- Native renderers are enabled before native hover, highlight, removal or valve
  pose changes run. Overlapping and nested interactions hold the affected batch
  until all matching callbacks finish.
- After a six-frame interaction debounce, unchanged renderers can reuse their
  existing combined mesh. Changed materials, poses and renderer properties remain
  native. Recovery keeps the original vertex data and exact source triangle ranges;
  it introduces no numerical approximation to simulation calculations.
- Construction scanning yields between parts and partitions. A global cooperative
  budget is approximately 1.25 ms, with at most one mesh commit or recovery per
  frame. Each mesh is limited to 64 sources, 16,000 vertices and 96,000 indices.
  Unity calls, allocation and garbage collection can still exceed that budget.
- Cached triangle indices have a global 16 MiB ceiling. Extra geometry remains
  native. Smaller batches can cost additional draw calls and leave small groups
  unbatched; retained meshes also keep conservative bounds after sources are pruned.
- Edits cancel unfinished preparation. Unload releases retained meshes and cached
  indices. Unsupported layouts, failed recovery or interaction-tracking overflow
  fall back to native rendering.

Simulation, circuits, physics, colliders, object picking, visual refresh rates,
inventory, save schemas and network formats retain their native implementations.
The existing incremental-room gas rounding tradeoff is unchanged.

## Native checks

The final implementation passed these checks inside the inspected Unity/Mono game:

- Native physics raycast, interaction filters and hover tracker acquired a batched
  part, kept native rendering during the hold, and recovered after leaving.
  This used controlled ray inputs, not desktop mouse movement.
- Overlapping hover/highlight and nested root/child hover callbacks did not recover
  early. Twelve checked recoveries preserved raw authoritative part, physics and
  gas fingerprints. Successful recoveries also preserved mesh identity, vertex data
  and exact original triangle ranges.
- Native visual-instancer lifecycle: 49 additions, 12 removals, construction while
  preparation was pending, and unload. Original collider identities were preserved.
- A separate mouse-driven build-menu test placed one Constant through the game's
  normal UI in a disposable world. It is a limited placement smoke test; manual
  mouse hover acquisition and manual pickup were not established by that run.
- Untouched native method oracles matched 12,000 factory steps, 192,000 circuit
  states and 25,472 fluid comparisons. Twin native PhysX scenes matched for 600
  steps with zero measured position, velocity or rotation error. Room edit,
  partition, breach, reseal and reload checks passed with the existing rounding
  tolerance.

The offline suite passes 43 cases, including overlapping interaction bookkeeping,
bounded tracking, removal and frame-counter wraparound. Trace-analysis tests pass
15 cases. These checks do not establish arbitrary building, flight, docking,
multiplayer or third-party mod compatibility.

Raw drivers, frame traces, screenshots, failed pilots and restoration receipts are
kept outside the repository in `private/batch-recovery-20261021`. No private driver
or test save belongs in a release package.

## Matched measurements, 21 October 2026

Three fresh-process pairs compared the current HEAD mod with recovery, using the
same 20-ship seed and a frozen sequence of 117 native highlight/remove calls.
Order was control/candidate, candidate/control, control/candidate. Each process had
35 seconds of focused warmup and two settled 25-second frame captures: intact
startup batching, then the same interaction sequence. Expensive correctness probes
and CPU sampling were absent from the frame captures. Both arms used the same
lightweight observer and native 50 Hz simulation. All 12 captures retained focus
and had no save write during capture.

Test PC: Ryzen 7 3800X, RTX 2070 SUPER, Direct3D 11, 2560 × 1080, quality level 0,
VSync off and no FPS cap. The existing mod stack and world content were retained.
Control is the previous mod at `1c49ec8`, not the unmodded game.

| Pair | Intact startup FPS, control / recovery | After interactions FPS, control / recovery | Post-interaction FPS change |
| --- | --- | --- | --- |
| 1 | 63.50 / 61.76 | 50.49 / 58.31 | +15.48% |
| 2 | 63.76 / 63.56 | 50.26 / 58.47 | +16.33% |
| 3 | 59.54 / 61.10 | 49.90 / 58.18 | +16.59% |

Equal-weight paired log frame-time ratios give **+16.13% FPS**
and **-13.89% mean frame time** after the interaction
sequence. Median per-run FPS was 50.26 →
58.31. Intact startup measured
**-0.17% FPS**. This is recovery of batching lost through
interactions, not an additional gain over intact startup or a general gameplay
average. Three pairs on one world and PC do not establish a general confidence
interval or reliable 1% low improvement.

The control began with 2,327 sources in 117 batches and ended with zero batches.
Recovery began with 2,320 sources in 141 batches and retained 1,990 sources in 123
batches afterward. Changed native renderers were deliberately left unbatched.
Initial cached indices used 9,935,808 bytes (9.48 MiB), within the 16 MiB ceiling;
that ceiling covers cached indices, not all renderer or mesh memory.

### Recovery cost and rebuild hitches

Across the two candidate runs with observer exports, 240 recovery calls took
0.081 ms median, 0.197 ms p95 and 0.680 ms
maximum; 0 exceeded 1 ms. Recovery reused the existing combined mesh.

The table below measures the mod's batching `Update` method, including initial
construction, first-use/JIT, warmup and captures. It excludes the expensive
correctness probes. These are method timings, not whole-game frame times:

| Run | p99 batching Update, ms | Maximum, ms | Calls over 5 ms |
| --- | --- | --- | --- |
| Control, pair 2 | 0.037 | 76.364 | 1 |
| Recovery, pair 2 | 0.642 | 6.360 | 1 |
| Control, pair 3 | 0.040 | 68.166 | 1 |
| Recovery, pair 3 | 0.548 | 6.743 | 1 |

Median batching `Update` time across these captures was 0.019 ms
for control and 0.041 ms for recovery. Scheduling adds bookkeeping
and takes more frames to finish construction; its benefit is a smaller peak cost.

The first pair has complete frame traces but no CPU observer export because the
driver's direct completion path omitted it. The remaining pairs exported through
the same frozen driver's post-capture branch. The DLL and driver hashes and capture
paths were identical throughout. Cooperative scheduling reduces individual build
bursts, but cannot remove Unity allocation, mesh-upload or garbage-collection stalls.

The exact candidate DLL SHA-256 is:

```text
0D187A82753DA7E8176E7C4B9141734B167BC1B1DCD404E25C071E9614704019
```

The [machine-readable evidence](batch-recovery-evidence.json) records per-run frame
statistics, hashes, costs and scope. Original installed files and all 135 existing
world files were hash-verified after each run; temporary drivers and owned test
worlds were retired. The original installed mod was restored. This is tested source
work, not a published release or permanent installation.
