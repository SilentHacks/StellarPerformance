# 1.0.0 minimal-core evidence

Measurements below apply to the released five-feature DLL;
older audit percentages remain historical evidence.

The current guarded CPU optimisations have separate [focused FPS and native
preservation evidence](CPU_RESEARCH.md). Their five startup switches now default
on for new or missing preferences across every tier; existing stored `false`
values remain respected and F8 does not reset them. The promotion changes defaults
and the resulting DLL hash, not those CPU algorithms or guards. Recorded runs
retain their measured artifact hashes and explicit settings; they are not new
measurements of the preference-policy revision. Replacement save pipelines retain
their own writer, with no additional measured serialization gain on the installed
Foundations/Workshop path. These workload-specific results do not change the
historical released-build comparison below.

## Measured performance (2026-10-18)

**The reduced build lowered mean frame time by 47.98%**
in six matched pairs on the declared workload. The exploratory paired bootstrap 95%
interval was -48.47% to -47.49% change in mean frame time. The corresponding
paired average-FPS increase was 92.23%.
These are workload measurements, not an all-world guarantee.

| Profile | Average FPS | Mean ms | p95 ms | p99 ms | 1% low FPS |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mod absent | 36.78 | 27.187 | 35.232 | 43.421 | 20.35 |
| Five-feature defaults | 70.56 | 14.173 | 18.783 | 21.810 | 37.64 |

Table entries are medians of six per-run summaries, not pooled-frame statistics.
The primary effect uses equally weighted paired log ratios. Bootstrap resamples the six
matched runs, never individual frames (10,000 draws, fixed seed 1000); the interval is
exploratory and assumes representative independent runs. Each pair's frame-time change
was: -48.98%, -47.95%, -47.15%, -47.94%, -47.37%, -48.46%.

Protocol: twelve fresh native game processes; alternating N/M then M/N pair order;
45 seconds focused warmup, screenshot, 3 seconds settling, 45 seconds raw frame intervals
per process. Same stationary saved view and starting 20-ship world, no input replay.
Every captured frame is retained; all twelve runs passed focus, completion, workload,
configuration, mod-presence and save-write checks. All five modules installed in every
M run. No captures were rejected or silently excluded.

Machine: Ryzen 7 3800X, RTX 2070 SUPER, Direct3D11, 2560x1080, quality level 0,
VSync off and uncapped target FPS. Other installed mods were retained, including
Foundations, Workshop and SDModKit. N means Stellar Performance absent on that same
stack; it is not an entirely unmodded-game measurement. Results do not cover travel,
construction, moving views or multiplayer frame rates.

Candidate DLL SHA-256:
`9739430D903E8803C6847CBF6809A12B16B2D4B3EAB1D1311E53F21A589E60C1`

Starting world SHA-256:
`6BC3E3C031DCFF08CC05133CAB347963861FF87743EFB243D6E3AD2A2B823288`

The [machine-readable evidence](minimal-evidence.json) contains every per-run summary,
paired effect, native result, restoration receipt and hashes of the raw evidence.
Private recorder source, traces, logs, settings and screenshots are preserved in
`private/minimal-1.0.0/` outside the repository. The actual installed DLL was restored
after measurement; the released DLL matches the measured build. Installation and publication are recorded separately in [RELEASE.md](RELEASE.md).

## Native follow-up results

Two primary exact-candidate peer sessions passed with room forwarding enabled and disabled.
A third session added an explicit positive control for varied nonzero native fluid flows.
They used two actual game processes over loopback IP, including join/disconnect, mirror
restoration and hull removal/re-addition. Remote wall/outside classification and room
connectivity matched authoritative samples before, during and after the edit.

The native sessions also passed:

- 1,000 native grid edits against an emitted untouched bounds oracle, exercising installed invalidation hooks.
- Four native electrolyzer scenarios, 3,000 steps each: every compared intermediate state, output, power request and event count matched the native-rate oracle. The old doubled-step counterexample remains historical evidence for removing cadence.
- Live fluid updater comparisons, including varying rates and forced topology rebuilds. Full counts, maximum flow error and scoped CPU timings are in the JSON receipt; intercepted presentation calls mean those timings exclude native material/animation work and cannot be treated as full-frame gains.
- Actual batching restoration via native highlight/remove-highlight calls, preserving part object/state and attached-collider identity. The selected fixture has no attached colliders, so it establishes no positive collider coverage.
- Live Harmony ownership inventory containing only the retained feature families.

The private native sessions retained the same candidate DLL hash as the frame captures.
The positive-control session observed 1,792
nonzero native flow outputs spanning 6
distinct flow values; all 25,472 comparisons
matched with maximum observed flow error 0.0.
The two primary sessions each compared 25,472 outputs, varied 552 part rates and forced
16 topology rebuilds. Their isolated fluid CPU medians were 4.065/4.098 ms native versus
0.100/0.101 ms cached. These timings intercept presentation calls and are not full-frame savings.

These tests support the retained contracts, with the limitations below.

## Scope and implementation

Five features remain: ship-grid bounds, fluid visual topology, incremental rooms,
single-player room forwarding and guarded fixed opaque mesh batching. Their defaults
are enabled. Batching continuous validation remains an optional compatibility guard.

Removed implementations include factory cadence, stable-signal sleeping, state/query
reuse, physics scene/transform changes, loader reflection caching, picking throttling,
UI throttling/culling/distance limits, terrain cadence, detail/shadow limits, visual
refresh caps and the other secondary caches. Removed preference keys are inert.
Native factory, circuit, physics, network, picking and visual update rates remain.

| Decision | Reason |
| --- | --- |
| Keep grid bounds | Exact cached computation with native mutation invalidation. |
| Keep fluid topology, remove cadence/material shortcuts | Reuse connectivity while reading live flow and calling native visuals every frame. The historical fluid ablation combined caching and refresh reuse, so its 47% figure cannot be attributed to topology alone. |
| Keep incremental rooms | Local reconstruction avoids large native region scans; retain explicit small gas rounding allowance and native fallback. |
| Keep single-player room forwarding | Avoid a duplicate local tree while querying the authoritative geometry; restore native mirrors on peer join and debug use. |
| Keep guarded batching | Substantial historical marginal effect; retain inspected shaders, scripts, transforms and native interaction restoration. |
| Remove factory cadence | Missed pulses, feedback evaluation counts, outage sampling and discarded pending time changed computation materially. |
| Remove stable-signal sleeping | Callback differences and extra invalidation machinery for about 0.77% historical marginal frame effect. |
| Remove physics/loader changes | Approximately zero/inconclusive historical frame effects; unchanged-transform guard itself was slower in the fair native microbenchmark. This is a cost/complexity decision, not a demonstrated physics regression. |
| Remove terrain/UI/detail/cable caps | Small or unearned marginal gains; moving-scene perceptual acceptance remained incomplete. |
| Remove remaining secondary caches | Reduce patch surface, optional adapters and invalidation complexity. Some fixtures matched native, but they lacked a compelling isolated contribution to this small release. |

Historical marginal effects come from the [0.9.0 feature audit](FEATURE_AUDIT.md).
They are conditional on that older reference stack and are neither additive nor new-build measurements.

## Behaviour evidence and limits

All 40 current offline cases and 15 frame-analysis tests passed. The offline suite covers exact bounds, fluid flow/balance
formulas, room geometry/connectivity/volumes/gas identity, saved scan seeds, breach and
reseal, partitions/joins, random edit traces, forwarding/restoration, batching shader,
transform and valve rules, metadata and removed-feature contracts. It executes managed
copies rather than installing runtime detours on CoreCLR. Shipping metadata is checked
on the actual net472 DLL as well as the same-source test assembly.

The repeated atmosphere fixture compares 256 edits in 8- and 40-voxel box shells.
The current-run maximum native/mod difference was about 0.000720 atmosphere;
maximum optimised/independent weighted-rule error was about 0.000000687 atmosphere.
This is a measured numerical allowance, not bitwise identity or a global bound. Native
thresholds close to either result could differ. Current-run output is retained privately.

Native integration receipts are included in the machine-readable evidence above.
Geometry equivalence permits a bijective remapping of room IDs: IDs are labels, while
wall/outside classification and connectivity must agree. A passing highlight-restoration
component check does not establish pixel-perfect rendering or complete hover/click,
collider, moving-scene, docking or WAN/Steam multiplayer coverage.

There is no categorical proof for every possible world, mod combination and future game
build. The defensible claim is measured performance on a declared workload, native rates,
removal of known divergent paths, and bounded behavioural evidence for the retained code.

## Restoration

All 15 owned test worlds were moved to the private archive after validation. All 157
pre-existing files (including 135 world files) matched their pre-test hashes. The
original installed DLL and preferences were restored, private drivers were removed
from the game installation, and no game process remained at final verification.

The 23 October tier/default follow-up, stronger Aggressive refresh limits and
rejected/private candidate screens are recorded in [live presets](PRESETS.md)
and [tier evidence](tier-frontier-evidence.json). These are separate from the
historical artifact measurements above.
