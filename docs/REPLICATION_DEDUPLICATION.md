# Replication deduplication (2.0.0)

The native queue already rejects record-equal states. This module retains that
native equality call exactly once and additionally rejects consecutive unequal
records only when their inspected network payloads are identical. It does not
intercept state assignment, production, server callbacks, fluid/circuit solving,
physics, visual update rates or initial synchronization. There is no per-player
baseline, serialization buffer, timer, precision tolerance or retained state cache.

## Supported states

| State | Extra work avoided when encoded fields are unchanged |
| --- | --- |
| Ethanol distiller | Fractional biomass bookkeeping and equal-content inventory arrays. |
| Electrolyzer | Fractional ice consumption. |
| Wood generator / wood thruster | Fractional wood consumption. |
| Gyroscope | Input tick and internal strength changes with the same animation target. |
| Pilot seat | Input tick/internal gyro bookkeeping with unchanged encoded controls and animation. |
| Velocity sensor | Previous-position/space bookkeeping with unchanged encoded velocity. |
| Unary signal processor | Previous-input bookkeeping with unchanged operation and output. |
| Signal transmitter | Internal signal changes with unchanged encoded channel. |
| Warp drive | Startup timer changes with unchanged encoded drive state. |
| Storage | Replacement arrays with identical encoded contents across all ten slots. |
| Gas sensor | Double changes that already encode to the same native float bits. |

Gas intakes, tanks, ordinary thrusters, doors, math gates, unknown/derived states
and other unsupported types retain native queueing. Changes to interactive-use
and pickup/holding fields remain native even where the writer omits those fields.
All encoded changes that the native queue would send, including returns to an
earlier value, retain their native queue position. Native record equality still
has precedence, including its existing signed-zero/NaN behavior. Full snapshots,
additions and removals are unchanged.

Floating-point comparisons use exact transmitted bits. Pilot-seat scalar inputs
and gas-sensor outputs use the float conversion already present in the native
writer; this adds no new quantization. Machine inventory slots compare all fields
written by the generated native slot writer, including fields in empty slots.
Distiller/storage slots follow their separate occupied-slot encoding.

## Settings and guards

`DistillerReplication=true` retains the existing distiller option.
`PartReplicationDeduplication=true` independently enables the additional types.
Disable both for native replication. The installed module is logged as
`replication-deduplication`. Repeated client notifications can be omitted; mods
using them as a clock may require these switches disabled. Server callbacks remain.

Installation checks native assembly identities, queue/serializer/property patch
ownership and the actual generated inventory-slot writer delegate. A single
inspected native equality call must match the transpiler contract. Unknown
implementations retain native/previous behavior. Later serializer/delegate or
patch changes are outside this inspected installation contract.

## Correctness evidence

The 64-case offline suite and 15 frame-analysis cases pass. The added cases compare
actual native serialized payloads, encoded transitions, interaction/holding fields,
all slot fields, vector components, float bits, existing wire precision, malformed
states, unknown/derived types and native equality call counts. No CoreCLR runtime
detours are installed.

The native Unity/Mono validation uses the installed shipping hook, an emitted
untouched queue oracle and the actual native writer and reader:

- 24,000 constructed transitions across all 12 supported types retained every
  authoritative assignment/callback, immediate encoded changes and receiver
  payload equality after each step. Periodic full snapshots also matched.
- 80,000 native machine steps across distillers, electrolyzers, wood generators
  and wood thrusters compared all intermediate states, applicable power/output
  values and callback counts against separate instances using native queueing.
  Every compared result and decoded receiver payload matched. Cases cover steady
  production, brief outages/zero demand, exhaustion and small output capacity
  where applicable; these are isolated machines, not arbitrary production chains.
- A separate 15-second observation of the saved world's actual state/queue calls
  found no lost or altered genuine payloads. That interval exercised the existing
  distiller saving but found no additional suppression from the new types. It is
  not evidence of a general new bandwidth or FPS gain.
- A graphics-enabled native host and two successive separate clients over
  loopback IP passed seven controlled phases on 11 saved-world part types:
  initial synchronization, 50 duplicate updates per part, changed values, returns,
  two distinct same-frame pulse/return notifications, storage removal/re-addition,
  and disconnect/rejoin snapshots. The world contains no electrolyzer; its native
  machine and serializer fixtures supply that type's coverage. The test holds
  selected server part states while publishing controlled values; it is not a
  manual-input or arbitrary production-network playthrough.

The first headless peer run passed these transport/state checks but emitted
headless compute-shader errors. The graphics-enabled follow-up also passed and is
the primary peer receipt. Its logs retain a shader warning also present in the
frame-test baseline and planet-texture-baker coroutine exceptions around teardown.
No error-free graphics/teardown claim follows from the peer checks. Neither run
establishes WAN/Steam multiplayer, all content or arbitrary mod compatibility.

## Targeted replication savings

Steady native fixtures ran for 5,000 steps at 50 Hz per machine:

| Machine | Native serialized update bytes | Filtered bytes | Reduction |
| --- | ---: | ---: | ---: |
| Electrolyzer | 75,000 | 1,515 | 97.98% |
| Wood generator | 75,000 | 150 | 99.80% |
| Wood thruster | 80,000 | 96 | 99.88% |

These totals include the native change record with the fixture's IDs, exclude
transport overhead and measure only that machine's updates. They are not total
multiplayer traffic, packet counts or frame-rate gains. Genuine output, inventory
and starting-state transitions remained immediate. Distiller savings are existing
behavior, not credited as an incremental improvement from this expansion.

## Incremental frame measurements

Three fresh-process pairs compared the previous canvas-culling build with this
expansion, retaining the same 37-ship seed, stationary view and other installed
mods. Order was baseline/candidate, candidate/baseline, baseline/candidate. Each
run used 35 seconds of focused warmup, a screenshot, three seconds settling and
30 seconds of raw frame intervals. All six traces retained focus, recorded no save
write and used the native 50 Hz tick configuration. Correctness observers and
network diagnostics were absent from the frame captures.

| Median per-run statistic | Previous build | Expanded deduplication |
| --- | ---: | ---: |
| Average FPS | 64.83 | 64.93 |
| Mean frame time | 15.424 ms | 15.401 ms |
| p95 frame time | 18.676 ms | 18.882 ms |
| p99 frame time | 24.570 ms | 24.627 ms |
| 1% low FPS | 36.21 | 35.19 |

Equal-weight paired log ratios give +0.107% FPS / -0.107% mean frame time.
Individual pair FPS changes were -0.914%, +1.091% and +0.154%. This establishes
neither a dependable new FPS benefit nor a frame-tail improvement. The targeted
machine replication savings above are the demonstrated benefit; this saved-world
interval did not exercise additional suppression from the new types. No general
confidence interval or hardware/world guarantee is claimed from three pairs.

Hardware: Ryzen 7 3800X, RTX 2070 SUPER, D3D11, 2560x1080, quality level 0,
VSync off and uncapped FPS. Reviewed baseline/candidate screenshots retain the same
view and visible readouts; live values and water differ, so pixel identity is not
claimed. These results are incremental and must not be added to earlier slices.

The final native-tested and frame-tested DLL SHA-256 is
`B4F9A5598733815684476A6F7340E41B63102364CD0D7546AB920F8135ED25FF`.
The control was
`F7F5D8C9F23569814C410C8231FA905D4D385C7ABCAE67E8A21D37F92F7A9024`.
All 157 original files, including 135 existing world files, were restored and
hash-verified after testing. The peer instances used separate loader directories.
The compact [evidence record](replication-deduplication-evidence.json) contains
per-run summaries, paired effects, native machine cases and receipt hashes.

Raw native drivers, traces, disposable worlds and restoration receipts remain in
the sibling private replication-expansion directory. No package, publication or
permanent installation is part of this work.
