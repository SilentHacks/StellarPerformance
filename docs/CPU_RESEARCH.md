# Exact CPU optimisation research — 23 October 2026

Five guarded CPU implementations now **default on for new or missing
preferences in every tier**: signal reduction, accumulated power-list scans,
exact save serialization, large surface-space clustering and empty warp
bookkeeping. Existing stored `false` values remain respected, including in
Faithful and Custom. These startup switches require a restart and remain
independent of live F8 changes. Unsupported inputs and conflicting implementations
retain their native/previous-mod paths; the installed Foundations/Workshop async
writer receives no additional serialization gain.

The focused gameplay follow-up and evolving native acceptance below supersede
the initial investigation's FPS limitation for these particular workloads.
Recorded runs use the explicit switches and artifact hashes in the evidence.
The later default promotion changes startup policy and the resulting DLL hash,
without changing these five CPU algorithms or their guards; it is not an
additional FPS measurement of the preference/preset revision. The release/version rebuild has a separate artifact hash recorded in the release receipt.

## Focused gameplay follow-up

Ryzen 7 3800X / RTX 2070 SUPER, Direct3D 11, 2560×1080, quality level 0,
VSync off and uncapped FPS. Balanced and the installed mod stack stay identical
within each pair. Fresh processes load identical private save copies, warm for
35 focused seconds and settle after a screenshot before a 30-second capture.
No sampler, shadow comparison or simulation observer runs inside accepted FPS
windows. Offline builds/benchmarks are paused. Focus loss or a save write
invalidates a window. Changes within approximately ±3% remain inconclusive.

| Workload (repeat counts below) | Switches off, mean FPS | Switches on, mean FPS | Change |
| --- | ---: | ---: | ---: |
| Earlier three switches, fresh 37-ship world | 60.65 | 63.63 | +4.9% |
| Measured CPU build, fresh 37-ship world, all five requested | 63.58 | 66.24 | +4.2%, variable |
| Earlier three switches, constructed native power-cache history | 56.14 | 63.82 | +13.7% |
| Constructed dense 1,061-ship world, spatial switch only | 2.50 | 8.63 | **3.45×** |
| Measured CPU build, same dense world, spatial + empty warp | 2.40 | 11.39 | **4.75×** |

The earlier fresh and aged pairs request `SignalReduction`, `PowerTransferScan`
and `SaveSerialization` together. Saving skips the installed async writer, so it contributes no demonstrated
gain. These comparisons do not isolate individual signal/power contributions.
All eight windows retain focus, avoid saves and sustain approximately 50 ticks/s.
The fresh pair gains are 4.22%/5.61%; aged gains are 15.11%/12.29%. These are
practical repeats, not a formal statistical confidence claim.

The final ordinary-world result uses two additional opposite-order pairs on the
measured CPU DLL, requesting all five switches together. Their individual changes
are **+2.29% and +6.08%**. The first is inside the ±3% noise band; the overall
result is modest and variable, rather than a dramatic ordinary-world gain.
Both sides sustain approximately 50 actual ticks/s and retain the same geometry.
The older fresh-world result remains separately labeled because it uses the
preceding three-switch artifact; the two sets are not pooled.

The measured-build dense result is one fresh-process pair on the same CPU DLL,
varying only `SpatialClustering` and `EmptyWarpUpdates`; signal reduction,
power scan and save serialization stay off. It directly measures the combined
result rather than multiplying earlier ratios. The two opposite-order spatial
pairs and the separate alternating warp experiment below provide practical
confirmation of the constituent effects; the final combined pair is not itself
a repeated confidence estimate. Final p95 frame time falls 514.36 → 118.39 ms,
and p99 falls 801.63 → 134.68 ms. Native physics endpoints advance by 144 versus
684 steps in approximately 30 seconds: about 4.8 → 22.8 actual steps/s, while
configured 50 Hz remains unchanged. The optimized stress world is still slow.

The aged fixture invokes the **actual native cache rebuild** 1,000 times on each
existing group without changing wiring/meshes. Sixty-five distinct cable IDs become
65,065 visits, projected back to 65 by the mod. Ship physics, part-state and
room-gas fingerprints match before/after aging. This is constructed cache history,
not a manual 1,000-edit playthrough. The initial fresh pilots overlapped research
processes and are excluded. Hidden/unfocused/black captures and method-check/profile
windows remain excluded. Recorder startup zeros bias approximate rendering
averages slightly; they do not invalidate positive whole-frame durations.

### Dense-world breakthrough and its limits

The new spatial rewrite is isolated in two additional opposite-order pairs:
**2.490 → 8.569 FPS** and **2.514 → 8.699 FPS**. Both sides use the same shipping
DLL, seed and configuration apart from `SpatialClustering`. The fixture adds
1,024 copies of a legitimate single-hull ship to the original world, forming a
32×32 grid near the player. Native loading produces 1,061 ships, with 1,041 in
the active surface-space clustering input. It exercises a severe scaling case;
this is not a claim of a 3.45× gain in ordinary 37-ship play.

Warmup waits for the mod's tracked stateful mesh-batching jobs to become quiet,
then allows 35 focused seconds. That readiness check covers 17 stateful batching
records, not every client visual. The first stress pilot still had batch commits
and is excluded. Warmup observation proves the native clustering input size and
candidate selection; all observation hooks are positively removed before FPS
capture. Approximately 11,800 draw calls and 11.64 million triangles remain per
rendered frame on both sides; the improvement does not come from removing ships
or reducing geometry.

Network tick counts rise from approximately **5.0 to 17.3 actual ticks/s**. In the
reversed repeat, read-only endpoints of the native physics updater's tick counter
also advance by 152 versus 522 steps over the respective captures. Thus the
candidate performs more simulation work per wall-clock second, but this stress
world still cannot sustain its configured 50 Hz. The rewrite preserves each
step's computation; it cannot promise identical trajectories at equal wall time
when the original game is already falling behind.

## Exact spatial clustering rewrite

Native clustering materializes every nearby edge, then repeatedly enqueues already
discovered nodes. `SpatialClustering` replaces only this graph block with an active
balanced KD tree. It queries undiscovered nodes and sorts candidates by original
array index, preserving native first-visit BFS and component order. The unchanged
native double-vector distance predicate, including its square root, remains in use.
Position transposition, space transfers, center summation and simulation cadence
execute unchanged. No signal values or simulation steps are cached or skipped.

A simpler grid prototype failed an adversarial layout: eight separated clusters
inside one cell restored quadratic work. The tree passes that case. It removes
quadratic adjacency storage, but pathological exact sphere queries can still take
quadratic time; no universal linear or n-log-n time bound is claimed.

The actual shipping helper and emitted shipping bridge match untouched native
graph code across **222 fixtures, 45,919 ordered nodes and 48,546 center scalar
bits**. Native ship, player and loose-object classes are exercised, along with
threshold rounding, large coordinates, shuffled chains, custom equality and
nonfinite fallback. Clean Unity Mono medians include guards and native-list
conversion, excluding fixture clearing:

| Constructed graph | Original native | Shipping bridge |
| --- | ---: | ---: |
| Dense 2,048 | 438.03 ms | 2.65 ms |
| Dense 4,096 | 2,077.94 ms | 6.08 ms |
| Scattered 4,096 | 248.20 ms | 12.25 ms |
| Chain 4,096 | 253.03 ms | 7.06 ms |
| Adversarial eight-cluster 4,096 | 466.32 ms | 4.99 ms |

These method multipliers are not FPS multipliers. Inputs must have finite
coordinates within ±2^40 and supported identity/equality semantics. Small groups,
duplicate identities, conflicting patches and oversized inputs retain the complete
native graph. Scratch is bounded to 32,768 nodes/32 MiB; no scene or position cache
persists. Runtime ownership checks are separate from pure metadata binding, so
offline tests never query Harmony ownership or install detours on CoreCLR.

An actual-helper calibration challenged the initial 1,024-object activation gate.
Changing only that gate in an emitted copy preserves all native ordered members
and center bits in 24 fixtures. All four tested layouts win at 128 nodes; scattered
128-node inputs improve 0.295 → 0.217 ms, while dense inputs improve
1.693 → 0.096 ms. Scattered 37- and 64-node inputs regress, so the final gate is
**128 objects** and smaller groups retain the complete native graph. This is an
empirical crossover for the inspected runtime, not a universal hardware constant.
The accepted large-world FPS runs used the preceding 1,024 gate; their 1,041-node
input executes the identical graph algorithm with either gate.

## Evolving native simulation acceptance

Disposable twin circuits execute the real native operation stack, maths, state
setters, callbacks and actuator consumers. Only one group-evaluation call is
redirected to the actual shipping helper in the candidate. Checks include 2,400
changing ticks, 960 extra same-tick passes, 276 rewires, 63 part replacements and
343 reentrant callback ticks. All ten unary and ten binary operations execute;
up to 41 actual server endpoint plans are retained. **41,808 state comparisons,
78,662 group comparisons and 82,513 ordered callbacks match bit-for-bit**, including
custom-comparer and derived-state fallback.

Another 1,600 generator/electrolyzer steps cover 357 interruptions and 229 foreign
observer fallback ticks. Native fuel, ice, power and fluid results match, including
23,980 power/fluid scalar and 3,197 cable comparisons. There is no numerical
tolerance in these checks.

The corrected evolving save fixture verifies nonempty public-field DTO contents,
640 byte-identical outputs and 63 actual retained hull segments. An earlier
anonymous DTO was discarded by the native writable-only resolver; that weak save
subcheck is explicitly superseded. No save transaction or physics restore is
inferred from JSON equality. Constructed acceptance does not certify chaotic
long-term physics, building interactions or remote multiplayer.

## Power updates: prevent work from accumulating after edits

The inspected native `PowerServerTracker.EnsureStateStructureIsUpToDate` appends
cable IDs when rebuilding an existing group, without clearing its `CableIds` list.
This repeatedly adds the existing cables as well as retaining removed cable IDs.
`UpdatePowerTransfers` then walks that entire history at the native 50 Hz rate.
This is a session-lifetime scaling problem that a freshly loaded, stationary FPS
benchmark does not reproduce.

An untouched native fixture with 512 initial cables and 100 leaf cable add/delete
cycles ends with 512 connected cables but **103,012 entries** in the native list.
On Unity Mono, the original transfer loop took a median **2,058 µs per tick**;
iterating the 513 distinct IDs in first-occurrence order took **10.34 µs**.
The latter deliberately includes the distinct removed cable ID. Across 120 steps,
all compared power values and 34,884 notifications matched, including zero tick
length, NaN and infinity cases. These timings concern this constructed degraded
group; they do not describe every world or an FPS improvement.

`PowerTransferScan` changes only the list used by that loop. It retains the native
source list, stale IDs, group order, first-occurrence order, power arithmetic and
native equality/notification method. A copied list enumerator validates mutation
in O(1), including same-count replacements. Storage is bounded to 1,024 entries,
8 MiB and 65,536 unique IDs per projection. Very large or unsupported cases retain
the native loop. Unknown notification subscribers, including reentrant callbacks,
also retain the native loop. Installation guards the inspected native equality
and notification implementations.

This avoids the repeated CPU work; it **does not repair native list memory growth**
or remove native stale-ID behavior. Clearing the original list would change a
separate native bug's observable behavior and would discard the safe fallback.

The actual mod helper also passed 2,000 native steps with 12,000 scalar-bit
comparisons and 5,093 foreign-observer fallback events. It installed on the tested
mod stack and recognized the live native `MasterSync` notification subscriber.
The fresh saved world's 17 power groups had no repeated IDs, so there is no
fresh-load FPS benefit to credit to this fix on that world.

## Saving: reuse exact immutable JSON segments

The native save writer creates a new writable-only JSON contract resolver for
each save. It also repeatedly serializes thousands of unchanged hull records.
The 37-ship fixture contains approximately 2.93 MB of JSON and 10,313 hull parts.

Eight alternating native-runtime repetitions on one frozen native DTO produced
byte-identical JSON for every candidate. Representative warmed medians were:

| Serialization path | Median elapsed time |
| --- | ---: |
| Fresh native resolver and native writer | 216 ms |
| Reused native resolver, native writer | 182 ms |
| Reused resolver, direct native-format hull writer | 61 ms |
| Reused resolver and exact unchanged hull segments | 37 ms |

This is roughly **83% less serialization time for unchanged hulls**. The first
segment-cache serialization still constructs those segments and is slower than a
warm hit. Changes rebuild the affected segment. These are serialization timings,
not the complete save transaction: state conversion, backup creation, screenshot,
disk I/O and other mods' hooks are outside this table.

**Installed-stack qualification:** the current Workshop/Foundations combination
already intercepts saving and has its own asynchronous serialization transaction.
`SaveSerialization` correctly skips that conflicting native writer. The native
gain above is useful for the native save path, and is not an additional measured
stall improvement on that installed stack. No compatibility guard was loosened
to force it through another mod's save transaction.

`SaveSerialization` replaces one serialization call inside the native file writer.
The original save transaction, file paths, encoding, exceptions and hooks remain.
It retains the native resolver and owns copies of hull scalar records plus their
exact JSON. Every hit checks a hash **and every field in array order**; engineered
hash collisions and returns to prior values must preserve output. Bounds are
256 entries, 32,768 hulls per segment and 16 MiB of accounted retained storage.
Unusual JSON policies or external converters take the native path. The global
`JsonConvert.DefaultSettings` factory retains its native call count.

The emitted integer/property order includes the native color aliases `R/G/B` as
well as `x/y/z`. No saved precision or schema is removed, and no live Unity state
is accessed from a worker thread.

The earlier failed background-save prototype was not proof that detached saving
cannot work. Its JSON round-trip clone invokes writable `Quaternion.eulerAngles`
setters after raw quaternion fields, changing the quaternion. A direct field clone
instead produced byte-identical native output, including on a worker in the tested
fixture. An asynchronous save transaction still needs ownership, completion,
failure, screenshot and mod-hook ordering work; this implementation uses the
simpler synchronous improvement.

## Signals: specialize bookkeeping, preserve computation

The current union cache avoids rebuilding the connection union. Native evaluation
still creates input/output lists, discards the output list, and looks up emitting
ships and parts again to obtain their values. Merely reinstating the retired input
projection would leave much of this work intact.

`SignalReduction` streams inspected native states through the unchanged native
conflict reducer in the same connection/socket order and multiplicity. It reads
live part state and live emission-socket arrays on every query. There is no signal
sleeping, tick divisor, value cache, approximate arithmetic or callback coalescing.
Unknown providers, derived/custom signal states and conflicting native patches
retain the complete native evaluation method.

Native probes passed 285,926 ordered server/client reductions over 396 groups per
tracker, including isolated split/reconnect graph mutations, plus 1,300 native
field projections and 15,901 constructed group checks. Offline tests expand this
to every byte-valued socket ID and nonfinite/signed-zero inputs. Held-state native
microbenchmarks showed a useful reduction in the group-evaluation path; this is
not evidence of a large whole-game frame-rate improvement. The evolving circuit
and feedback acceptance above extends this evidence; building and remote-player
acceptance remain separate requirements.

The bolder server implementation also retains ordered `StatefulPart` references,
then reads each part's live state. It validates provider/part dictionary identity,
ship-state replacement and copied dictionary mutation versions before reuse.
Pointer caching requires positively verified Mono mutation semantics, default
comparers and a union actually retained by `SignalConnectionScan`; unsupported
runtimes, capacity fallback and uncached unions retain streaming/native behavior.
The plan has its own 1,024-entry/8 MiB bounds and is cleared on scene unload.
No global collection mutator is detoured. Client evaluation retains streaming.
Admission accounting is conservative until scene unload: dead weak-key entries
can be collected, but the plan admission count is not reclaimed individually.
Heavy topology churn can therefore exhaust the 1,024-plan budget and lose coverage
for the rest of that scene. It falls back safely; full coverage in the 396-group
fixture does not imply full coverage in arbitrarily large or long-lived circuits.

An explicitly warmed native server microbenchmark reduced 75 sweeps of 396 groups
from 45.28 ms to 18.05 ms, approximately **60% less time in that tested path**.
This motivated the server plan, rather than reviving the older endpoint-list cache.
Earlier plan timings were invalid as warmed-plan measurements: constructed tests
had exhausted bounded caches before timing. The corrected harness clears and
warms both the union and endpoint caches and records retained entries. This is a
method-path result, not a 60% whole-game performance gain.

Final-build acceptance on the installed stack compared the actual mod helper
against native execution: **323,532 running-world evaluations matched bitwise**,
with zero errors/mismatches. It also exercised nine pointer replacements, a cached
topology edit and four custom-comparer fallback cases. Each timed tracker had all
396 union identities stable; the server retained all 396 endpoint plans.
For 75 warmed sweeps of those groups, the final server helper measured
**44.34 → 19.55 ms** (55.9% less time); the client streaming helper measured
**45.84 → 38.18 ms** (16.7% less time). These timings include the actual helper's
guards. Clients intentionally retain the simpler implementation; the more
general private plan remains research. Instrumented shadow frames are excluded
from performance measurements.

## Ideas tested or deferred

- Reusing repeated part-dictionary lookups in `ShipState.SetPartState` preserved
  10,000 assignments and 20,000 callback observations, but saved only about
  15 nanoseconds per assignment in the native isolated fixture. It was not added.
- Native typed-part membership indexing and authoritative fluid topology caches
  already exist. Replacing them with similar caches would duplicate existing work.
  Fluid pressure redistribution also has order-sensitive floating-point passes;
  no solver sleeping, slower production or physics stepping was introduced.
- Broad multithreading of live circuits/Unity objects would require explicit
  ownership and callback/dependency ordering. It is not justified by a parallel
  loop over the current mutable object graph.
- Globally detouring Mono collection mutators could make signal union validation
  cheaper, but generic sharing, reentrancy and partial mutations on exceptions
  make that a much wider runtime contract. No such detours were added.
- The new spatial rewrite confirms that the earlier 37-ship profile was too small
  to rule out a major clustering opportunity. The dense-world result above
  demonstrates that opportunity without claiming a large gain in small scenes.
- Reusing cable presentation's material lookup and integer shader property IDs
  passes 12,000 native value sequences per cable type, but the observed workload
  budget is below roughly 0.1 ms/frame. It remains a private prototype.
- Terrain empty-delta reuse passes 80 paired native decisions, including 29 hits
  and 40 guard misses, with identical geometry and scratch results. The guarded
  method fixture improves 13.66 → 11.90 ms over 25 repetitions, including native
  mesh reconstruction on both sides. No whole-game FPS gain is established.
- Deferring hidden text geometry remains private: callback subscription changes
  and invalidation before visibility need a stronger lifecycle contract.
- A read-only hull survey finds 5,118 duplicate meshes among 5,348 distinct mesh
  objects across 4,206 chunks, proven by complete vertex/index/layout comparisons.
  That makes canonical geometry and instanced submission a serious next design.
  However, the actual `Custom/PlanetHull` shader has no compiled instanced
  programs, despite misleading variant metadata; the stock instancing switch
  alone cannot deliver this win. A custom shader/render submission path needs
  visual validation of lighting, shadows, highlights and rebuild ownership.

The post-rewrite stress profile also challenges the assumption that those draw
calls dominate this particular workload. Across 14,825 main-thread samples,
about 82.5% include FishNet tick processing, 71.8% include native pre-physics and
22.5% include Foundations' `AssemblyGround` lookup. The graphics worker spends
about 93.7% under its command-stream wait path. These are overlapping whole-capture
stack shares, not additive costs or precisely aligned per-frame timings. The
initial Mono/native clock origins differ, so only whole-capture attribution is
accepted from this profile. Sampled frames remain excluded from FPS comparisons.

The ground lookup performs a fresh Unity service-object/component search even
for ships without warp drives. In the stress scene it repeatedly searches 61
tagged service objects. A private empty-part prototype gives **8.608 / 11.798 /
8.589 / 11.894 FPS** in alternating control/prototype/control/prototype 20-second
windows with spatial clustering enabled throughout: **+37.77%** between the two
means. Controls remove all prototype hooks and agree within 0.23%; enabled phases
agree within 0.82%. Focus and save-write checks pass. Native physics endpoints
advance by 346 / 472 / 344 / 476 steps, corroborating increased simulation
throughput. This is additional headroom in the same constructed stress workload,
not an ordinary-world gain or a directly measured combined multiplier.

`EmptyWarpUpdates` implements that design behind a separate startup switch,
now default on for new or missing preferences across every tier.
It checks the actual updater, exact native ship, live native providers, current
typed warp-part set and valid space record on every invocation. Derived/custom
containers, nondefault comparers, nonempty sets and unsupported lifecycle inputs
retain the original composed Foundations/native update. It caches no part-list,
grounding, root, provider or topology result. Nested thread-local context and a
finalizer preserve context across exceptions and reentrant calls. Ownership guards
reject other warp observers and changed decision/collection methods. Installation
waits until all mods have initialized, because Foundations installs its wrapper
after Stellar Performance in this stack.

The paired private wrapper fixture covers 296 cases: empty/unrelated/removed and
re-added warp parts, six directly grounded ships, 31 ungrounded ships and invalid
spaces. It positively exercises 74 successful nonempty actions, 24 actual ordered
callbacks and 31 matching malformed-space exceptions. Raw physics/timer/memory
scalar bits match and the live authoritative world fingerprint is unchanged.
No connected-root fixture was present, so this does not certify that case.

Final-artifact native acceptance also executes all 296 cases through the actual
installed shipping wrapper, alongside emitted original/helper copies. Six further
guards cover mismatched/null owners, a derived empty set whose interface iterator
yields a real part, a custom comparer and a missing provider. Production counters
positively record 299 dispatched queries, 185 skips and 114 native fallbacks.
Context is restored after every case, including exceptions, and diagnostic state
is restored afterward. The final artifact passes the full 113-case offline suite
and all evolving circuit, machine, save and clustering comparisons above.

The contract is scoped to valid inspected scenes. Skipping `AssemblyGround` also
leaves its shared temporary root-list scratch unchanged. All inspected native and
workspace mod consumers rebuild and consume that scratch synchronously; it is
not authoritative state. Foreign code retaining that collection, and incidental
lookup errors from corrupt root/service graphs, are outside this proof. The
shipping adapter adds stricter collection and provider-lifecycle guards than the
prototype, so the prototype's exact FPS is not a final-artifact FPS claim.

A separate native service-query experiment replaces array results with the native
list-filling overload, preserving the same internal enumeration flags and full
ordering. It passes 302 component queries, 953 element identity comparisons and 80
complete locator checks, including interface, duplicate-component, mutation and
destruction-callback cases. No actual missing-script null slot was present. This
remains private, with no FPS claim; globally detouring shared generic methods is
not part of the design.

## Reproduction and limits

The shipping sources are built for net472. Offline tests execute native method
copies on .NET 9 and never install Harmony detours on CoreCLR. Native probes run
inside the installed Unity/Mono runtime with a disposable copy of the 37-ship
world and isolated loader/configuration directories. All 157 original mod,
configuration and save files, including 135 world files, are hash-verified.

Some exploratory launches failed before loading the intended world. Background
captures also produced black screenshots or lost focus. **None of their frame
rates qualify as normal gameplay performance evidence.** The focused follow-up
above supplies workload-specific FPS evidence. Multiplayer certification and
arbitrary-mod compatibility remain outside this acceptance.

Raw drivers, unsuccessful pilots, logs, scratch saves and captures remain in
the sibling `private/cpu-breakthrough-20261023` and
`private/cpu-followup-20261023` directories and must not be published.
The initial [method evidence record](cpu-research-evidence.json) and
[focused follow-up evidence](cpu-followup-evidence.json) contain artifact/run
hashes, exact checks, timings and restoration receipts. Test totals and the
measured CPU artifact hash remain attached to that evidence; subsequent policy
builds have their own hashes.

`SignalReduction`, `PowerTransferScan`, `SaveSerialization`, `SpatialClustering`
and `EmptyWarpUpdates` each retain an independent `false` opt-out in the
`[StellarPerformance]` startup section. Edit while the game is closed and restart;
the F8 selector does not reset these values. Existing stored `false` values also
remain false after upgrading, even if an earlier build wrote them as defaults.
Check each module's installation log: enabled preferences do not bypass guards
or guarantee installation. Disabling a switch restores its corresponding
native/current-mod path.
