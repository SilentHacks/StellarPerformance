> Historical 0.9.0 audit. The resulting five-feature 1.0.0 candidate is described and measured in [BENCHMARKS.md](BENCHMARKS.md). Removed implementation fixtures are preserved in the private starting-checkout snapshot; old results do not apply to the new DLL.

# Pre-1.0.0 claims audit

Assessment started 2026-10-17 against source revision `558444e` and the unchanged
0.9.0 shipping DLL:

```text
4303607E772BBA28A0D6A50C895B16E7C98EE422A4F4625DD6DA32FE6895C279
```

Only validation, analysis and documentation change in this audit. No shipping
behaviour, defaults, save schema or network format has changed. This document does
not certify a future 1.0.0 binary. See the [claims register](CLAIMS_REGISTER.md) for
the per-feature contracts, runtime matrix and release criteria.
The subsequent [feature audit](FEATURE_AUDIT.md), completed on 18 October, adds 68
switch/control captures, matched-binary native checks and loopback peer transitions.
Use that report for the latest per-feature measurements and recommendations.

## Decision on the requested claims

**A: the new native pilot demonstrates a substantial frame-time improvement in the
tested stationary 20-ship scene.** Three matched runs per profile measured 44.3% lower
mean frame time with the conservative profile and 56.9% lower with current defaults,
relative to Performance absent on the same installed mod stack. This supports a
scoped performance claim, not "faster everywhere". Default-profile gains include
deliberately reduced presentation and simulation work and do not isolate cadence's
individual benefit. The conservative result shows useful gains without those
documented trade-offs; it does not certify every remaining patch's behaviour.

The [0.9.0 validation report](VALIDATION.md) also records approximately 226 MB fewer room structures, 3.68 million
fewer room objects, and connection-union allocation reduced from about 1.02 MB/s to
zero in the inspected world. Its short matched FPS traces were 91.60/91.60, so those
savings must not be presented as an established FPS increase. Scopes overlap and
the allocation results cannot be added as independent savings. The older traces
compared two already-optimised configurations; they are a different comparison from
this audit's absent-mod baseline.

**B: the current default profile cannot support an unqualified gameplay-equivalence
claim.** Native-method fixtures and the shipping cadence decision demonstrate missed
pulses, different resource-production outcomes and lost pending simulation time. A
bounded gas rounding difference does not explain or justify those distinct effects.

The audit therefore blocks the combined statement "improves performance without
meaningfully changing gameplay" as a general claim for the current defaults.
Characterisation tests intentionally pass when a known counterexample is reproduced;
their green status does not override this decision.

## Measured behaviour evidence

The starting offline suite passed 280/280 optional-enabled cases. The final suite
passes 298/298 optional-enabled cases; core passes 294 with four explicit adapter
skips. Fourteen standard-library frame-analysis tests also pass. Raw receipts are
retained privately. Offline tests use .NET 9 and inspected native methods/emitted copies,
not Harmony detours on CoreCLR. They do not certify all Unity/Mono integration paths.

### Factory cadence: meaningful counterexamples

| Fixture | Native timing | Divisor 2 | Implication |
| --- | --- | --- | --- |
| One-tick input pulse through the native signal chain | Observes the 20 ms pulse | Misses it entirely; final output still matches | Final-state equality alone misses behavioural changes |
| 1,000 cycles: one skipped tick followed by an every-tick control state | About 40 s advanced | About 20 s advanced | Resetting cadence discards pending time; repeated transitions accumulate deficit |
| Self-add feedback with constant input 2, over 120 s | Output 12,000 | Output 6,000 | A larger step cannot compensate a per-evaluation operation |
| Native electrolyzer, 200 ice, ample capacity, 60 s continuous power | 60.001183 output | 60.00099 output | Approximately linear steady work is close in this fixture |
| Same electrolyzer with a one-tick outage every 100 ticks | 59.40117 output | 60.00099 output | Sampling phase misses outages; about 0.60 units / 1.01% more output |
| Electrolyzer with one input item | Final output 1 | Final output 1 | Equal final totals conceal an intermediate difference of about 0.02 units |
| Electrolyzer with output capacity 0.03 | Final output 0.03 | Final output 0.03 | Capacity clamp preserved in this fixture; completion timing differs |

The control-transition counterexample also reproduces at divisors 3 and 4, discarding
about 40/60 seconds respectively over the same 1,000 cycles. These are deliberately
adversarial scheduler inputs, not a prediction of normal-play incidence. The runtime
prefix maps piloting, lever interaction and ship repositioning to the tested reset.
Ordinary-play frequency and resulting losses still need a recorded input replay.

For variable input durations 0.01/0.03 s, divisor 2 advances 0.06 s rather than their
0.04 s sum; reversing them advances 0.02 s. This is a scheduler contract limitation.
Its reachability from the supported game's normal fixed-tick caller is not established,
so it is not presented as an observed gameplay defect.

Both native and coarser-step electrolyzer fixtures accumulate float accounting error:
maximum measured ice/output balance residuals were about 0.01163/0.01106 units over
60 seconds. These are reported separately from their output difference. The tests
use an analytical roundoff allowance for the synthetic linear accounting checks;
that allowance is not an approved gameplay or conservation tolerance. Actual fluid
networks, recipes, production chains and power scheduling remain untested here.
The first fixture draft used a fixed 0.002-unit balance allowance and failed on the
native path itself. That draft is retained privately. The revised fixture records
both residuals and checks a loose operation-count/float-epsilon envelope; no gameplay
acceptance limit was widened or declared satisfied by that revision.

Recommendation: use native factory cadence for an equivalence-focused default and
retain coarse cadence as an explicitly described option, unless a narrower cadence
implementation earns its default through outcome tests. Merely retaining elapsed
time would not prevent missed pulses or changed per-evaluation circuit behaviour.
This audit leaves the shipping setting unchanged for review.

### Stable signals at native cadence

A deterministic trace ran 30,000 factory steps alternating stable and changing
inputs, with 0.02/0.04 s durations. All 90,000 compared native gate/processor states
matched. Native callbacks numbered 94,800 versus 50,370 with sleeping. This supports
value preservation for the tested circuit under native cadence and confirms the
intentional event coalescing. It establishes neither arbitrary observer-mod equivalence
nor the correctness of the surrounding doubled-step factory cadence.
The final reference loop is an untouched emitted copy of the native operation loop;
the tested path composes native-state reuse and stable-signal skipping.

Existing native-loop differential tests also cover feedback, math/unary operations,
tiny/nonfinite inputs, derivatives, busy backoff, topology changes, nesting and cleanup.
Those are supporting contract evidence, not coverage of every circuit/mod combination.

### Incremental rooms

Each fixture filled and reopened one interior voxel 256 times. Geometry, wall counts,
bijective room connectivity, room volumes and usable saved seeds matched after every
edit: 512 edit checkpoints per fixture. Gas densities were measured separately.

| Box size | Maximum native/mod density difference | Maximum mod/weighted-rule error |
| --- | --- | --- |
| 8 (343 interior voxels) | 0.000015676 atmosphere | 0.000000170 atmosphere |
| 40 (59,319 interior voxels) | 0.000719637 atmosphere | 0.000000687 atmosphere |

The independent accounting reference is `initialDensity * ((V - 1) / V)^cycles`:
filling a voxel displaces gas and reopening it introduces vacuum under the native
rule. It is an accounting reference for this fixture, not a model of all gas physics.

The large fixture's native/mod difference was approximately 0.000572 at cycle 24,
0.000276 at cycle 96 and 0.000720 at cycle 256. It is neither a monotonic divergence
nor a proven global bound. The optimised density stayed closer to the weighted-rule
reference than the native float fold in this fixture. Native numerical identity and
physical accuracy are different criteria.

Judgment: these results support accepting a documented rounding trade-off for these
edits. They do not establish harmlessness near actual pressure/sensor thresholds,
under unlimited edits, with unequal gas species, real doors or multiplayer. Those
cases remain required before a broad B claim.

## Native performance pilot

The private protocol uses the same measurement driver for absent-mod (N), conservative
(S), and current default (D) profiles, retaining the other installed mods. It starts
each run from an identical fresh copy of the backed-up 20-ship world. Three blocks
rotate order as N/S/D, D/N/S, S/D/N. Each declares 45 seconds of focused warm-up and
45 seconds of raw frame intervals with shipping diagnostics off. This is a stationary
pilot on one machine and dependency stack, without input replay.

The recorded startup graphics/system receipt is Ryzen 7 3800X, RTX 2070 SUPER,
Direct3D 11, 2560×1080, quality index 0, v-sync 0 and target FPS -1. All nine saved
views were visually reviewed and retained the intended camera framing and scene.
These receipts/screenshots do not establish pixel identity or equal downstream
simulation activity. Other installed mods were BuildPlus, Crouch, ExpandedStacks,
ItemLines, ModManager, SandboxMenu, SDModKit, StellarFoundations, StellarUnlock and
StellarWorkshop; their file hashes are retained in the private backup receipt.
Performance's content-membership and hull-read modules yielded to Workshop's existing
caches. Expected method-discovery warnings and these two guarded skips remain in the
logs. Neither Performance's own diagnostics nor allocation probes were active.

The conservative profile sets `PartSimulationDivisor=1`, `StableSignals=false`,
`IncrementalRooms=false`, `SinglePlayerRooms=false`, `ShipPartBatching=false`,
`RendererDetail=false`, `WorldUiVisibility=false`, `TerrainTraversalCadence=false`,
`CableVisualHz=0` and `PickingCameraDivisor=1`. All remaining settings use fresh
defaults. "Conservative" describes the intended profile, not a certification of
every enabled patch's semantics.

Initial driver attempts are preserved and excluded. They include a startup overload
selection error, a save-observer conflict with Foundations, hidden/unfocused rendering,
and direct executable launches without Steam's app context that remained on a loading
screen. Focus alone did not establish a loaded workload. The revised driver uses normal
Steam startup, requires loaded ships, installs no save hooks and records save-file
write timestamps. Window capture via computer-use timed out on two attempts; driver
screenshots and native logs supply the visual/readiness checks instead.
Lightweight accessibility-only activation was used before later traces; a focus
check also coincided with the beginning of S1. Its frames remain in the report.
No builds or offline test runs overlap accepted frame traces.
One later S2 attempt lost focus for 102 of its 2,690 frames near the end. The runner
rejected the entire attempt under its predeclared focus rule and restored the original
installation. Its raw trace, logs, settings and manifest entry remain in a separate
rejected-attempt directory. S2 was repeated from the seed before continuing the last
block. The repeat preserves configuration and seed identity but adds a temporal gap;
this remains an exploratory pilot, not a fully randomised experiment.

| Run | Average FPS | 1% low FPS | p99 frame ms | >50 ms frames/min | GC0 delta |
| --- | --- | --- | --- | --- | --- |
| N1 | 36.55 | 20.12 | 46.05 | 5.33 | 12 |
| S1 | 64.49 | 40.18 | 20.66 | 0.00 | 3 |
| D1 | 83.81 | 51.14 | 17.93 | 0.00 | 3 |
| D2 | 82.19 | 46.73 | 18.22 | 0.00 | 2 |
| N2 | 35.47 | 20.04 | 45.72 | 9.33 | 12 |
| S2 | 64.28 | 35.89 | 22.25 | 0.00 | 2 |
| S3 | 64.20 | 38.44 | 21.06 | 0.00 | 2 |
| D3 | 83.15 | 46.50 | 18.38 | 0.00 | 2 |
| N3 | 35.48 | 20.12 | 45.37 | 6.66 | 12 |

Profile ranges and medians (run summaries, not pooled frame percentiles):

| Profile | Average FPS range | Median run average FPS | Median run 1% low FPS | Median run p99 ms |
| --- | --- | --- | --- | --- |
| N: Performance absent | 35.47–36.55 | 35.48 | 20.12 | 45.72 |
| S: conservative | 64.20–64.49 | 64.28 | 38.44 | 21.06 |
| D: defaults | 82.19–83.81 | 83.15 | 46.73 | 18.22 |

Matched N/S mean-frame-time change: -44.30% (equally weighted run log ratios).
Matched N/D mean-frame-time change: -56.86% (equally weighted run log ratios).
Matched S/D mean-frame-time change: -22.55% (equally weighted run log ratios).

All nine accepted traces were fully focused, recorded loaded ships and unchanged world-save timestamps, and passed the comparator flags. Native logs were retained. No confidence interval is estimated from three pairs; the table reports an exploratory single-scene result.

Restoration verified 121 original files, including 99 original world files. Installed DLL/preferences were restored; private driver/request files were retired. Additional isolated test worlds remain outside the repository.

The complete aggregate results, build identity and raw-trace/log hashes are recorded in [claims-evidence.json](claims-evidence.json).

Suggested supported wording: "In a stationary 20-ship test scene on the inspected
Windows/game/mod configuration, three runs per profile recorded 44% lower mean frame
time with conservative settings and 57% lower with the default profile. Results vary
by workload. Defaults include visual and factory-timing trade-offs."

For B, retain feature-specific statements: supported native-cadence signal values
matched the tested traces; room geometry matched the tested edit sequences; gas
arithmetic had the measured rounding differences. Full construction, connected-factory,
travel/interaction and real-peer multiplayer coverage remain open. The current default
factory cadence prevents promoting those limited statements to general gameplay
equivalence. Moving it off by default is a recommendation, not an implemented change.


## Reproduction and interpretation

Run the optional-enabled offline suite using the commands in [development](DEVELOPMENT.md).
Tests marked `Evidence=KnownDivergence` preserve counterexamples, not equivalence:

```powershell
dotnet test tests/StellarPerformance.Tests/StellarPerformance.Tests.csproj -c Release -p:IncludeOptionalModTests=true --filter "Evidence=KnownDivergence" --logger "trx;LogFileName=counterexamples.trx"
python -m unittest discover -s tests -p test_frame_traces.py
python scripts/CompareFrameTraces.py <private-evidence-directory>/runs.json
```

The raw-frame comparator checks complete matched runs and retains bad runs with flags.
Per-run FPS is frame count divided by elapsed time. The 1% low is 1,000 divided by the
mean of the slowest ceil(1% of frames). p95/p99 use linear interpolation within each
run. Matched effects use equally weighted log ratios of run summaries; percentiles
are not pooled across runs. Fewer than six matched runs receive no bootstrap interval.
The standard-library comparator tests verify these definitions and invalid-input handling.

Private worlds, game/loader assemblies, native inspection, drivers, configurations,
screenshots and logs remain outside this repository and release package. Keep their
hash receipts alongside the published aggregate assessment. No release was packaged,
published or pushed as part of this audit.
