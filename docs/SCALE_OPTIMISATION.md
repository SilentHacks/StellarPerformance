# Large-build optimisation (2.0.0)

The distiller-only implementation and measurements below describe the earlier
slice. The subsequent [replication expansion](REPLICATION_DEDUPLICATION.md) adds
other inspected types and records its own validation and incremental measurements.

This work derives from the inspected native game, local profiling and native
serializer tests. No Performance+ implementation is used in these changes.

## Measured combined result

Three fresh-process pairs used the same 37-ship save, 40-second warmup and
30-second captures, alternating order against the previous batch-recovery build.
All traces stayed focused, had no save write, used the native 50 Hz tick rate and
retained the same 4,119 batched sources / 258 batches. Profiling and network
diagnostics were off during these captures.

| Metric (median of runs) | Batch-recovery control | New candidate |
| --- | ---: | ---: |
| Average FPS | 43.95 | 47.69 |
| Mean frame time | 22.75 ms | 20.97 ms |
| 1% low FPS | 22.90 | 26.41 |
| P99 frame time | 40.41 ms | 33.11 ms |

Per-pair FPS gains were 8.67%, 8.50% and 8.86%: median **8.67%**, with a median
7.98% frame-time reduction. These are combined results for distiller replication,
signal scan reuse and retry enabled without remote peers. They do not isolate each
feature's FPS contribution. Retry had no congested remote-player workload here.
This is stationary gameplay in one large save, not building/moving or multiplayer
FPS evidence. Hardware was Ryzen 7 3800X / RTX 2070 SUPER, D3D11, 2560×1080,
quality level 0 and uncapped FPS. Other worlds/hardware can differ.

The signal cache now defaults on; reliable retry remains experimental and off.
Machine-readable measurements and hashes are in
[the evidence record](scale-optimisation-evidence.json). Raw captures, runtime
drivers, failed trials and restoration receipts remain outside the repository in
`private/scale-optimisation-20261021`.

The final DLL differs from the measured candidate only in the signal default
(already enabled explicitly in the measurements), clearer diagnostic CSV column
names, and checking the cooperative deadline while closing expired queues. The
last change has an offline regression test. The final DLL passed the 53-case suite
and a fresh native validation including real Steam congestion; FPS was not rerun
for these default/diagnostic/expired-queue changes. The evidence records both DLL
hashes and the three changed method bodies.

## Distiller replication

`DistillerReplication` compares consecutive states at the native MasterSync
notification boundary. It suppresses a notification only when every field written
by the native distiller serializer is identical: interaction state, occupied slot,
item ID, quantity and production-rate float bits. Empty-slot unused fields and
`PartialBiomass` are absent from that payload. Unknown/derived states and malformed
slot arrays retain the native path. There is no persistent state or per-player
baseline cache; transitions back to an earlier value remain immediate.

Production, inventory assignments, partial biomass, power requests, fluid output
and authoritative callbacks continue at the native rate. Client callbacks for
identical payloads occur less often; mods using those notifications as a clock may
need this setting disabled. Full initial synchronization, removals and other state
types retain their native paths and formats.

The native Mono differential test uses 12 isolated distillers for 5,000 steps in
each of four scenarios: steady production with stacks of 255, interrupted power,
exhaustion with stacks of one, and constrained output. Production, inventory,
fractional biomass, power and server callback counts agree at every step. Native
serialized update bytes fall by 97.3–99.6%; the receiver's encoded state agrees
at every step. This measures distiller updates, not total multiplayer traffic or
an FPS improvement. It does not establish the cause of a previous Steam failure.

## Reliable Steam congestion recovery

`ReliableTransport` is experimental and defaults off pending real remote-player
acceptance. It retains reliable packets rejected with `k_EResultLimitExceeded`,
and appends later reliable packets to the same peer's queue. Retries preserve
packet boundaries, channel bytes and order. Unreliable packets retain native
behavior. The native source segment is captured before the stock send method
appends its channel byte; retained packets own their buffers.

Limits are 2 MiB per peer, 8 MiB globally, 64 queued peers, 2,048 packets per peer
and 30 seconds of age. Capacity accounting includes spare channel storage. Retries
rotate between peers, send at most four packets per peer per pass, and back off
from 10 to 250 ms after refusals. Each update has a cooperative 1 ms deadline,
64-attempt limit and 128 KiB payload budget; an indivisible packet may cross a byte
or time boundary. Persistent overload or terminal retry failure closes the
affected native connection. Queues clear on native disconnect/removal and quit.
This can add latency while congestion clears and cannot create bandwidth.

An actual Steam network-loopback test constrained one socket's native send buffer
to 4,096 bytes and rate to 65,536 bytes/s. All 160 packets arrived byte-for-byte in
order across two reliable channel IDs after real refusals, with source buffers
overwritten immediately after each send. A separate injected-refusal test passed
20 packets. These are one-machine socket tests, not WAN gameplay or join/rejoin
validation. Real hosting with remote players remains required before enabling the
feature by default.

## Signal connection discovery

`SignalConnectionScan` reuses the exact native connection union only inside signal
endpoint discovery. Every query still runs the native socket classification and
reads live part state; arithmetic, execution order, derivatives, feedback, callbacks
and presentation rates are unchanged. It preserves native union order.

Validation compares socket dictionary order, keys, set identities and counts, then
checks copied HashSet enumerators for set mutations. This also detects direct set
edits and replacement without relying on cable-event invalidation. Changes rebuild
the native union. Storage has 1,024-entry and 8 MiB accounting limits; groups with
over 4,096 sockets remain native. Weak keys and scene-unload cleanup bound lifetime.
Unknown patches changing the native discovery body or union getter disable
installation. Later patch changes remain outside the supported contract.

The native Mono comparison covered 476,010 ordered connection-union and endpoint
queries across 792 live server/client groups and isolated graph copies. It included
200 delete/split/reconnect/merge cycles per copy. Every result matched the untouched
native query. The same installed build passed 192,000 circuit-state comparisons
with matching callback counts, 37,504 fluid comparisons with zero measured flow
error, 12,000 electrolyzer steps and twin PhysX checks with zero measured position
or rotation error. These tests have specific fixtures, not universal coverage.
Native overlapping/nested hover, physics-ray acquisition/leave, 49 visual additions,
12 removals and unload also passed. Twelve recoveries preserved raw authoritative
fingerprints with zero mismatches. Interaction method bodies are unchanged in the
final DLL. Desktop capture failed twice, so further mouse-driven acceptance is not
claimed. All 157 original files, including 135 world files, were restored and
hash-verified after the final run.

## Other targets

The new profile justified connection-scan reuse before a more complicated cached
endpoint classifier. Native classification remains live, supporting dynamic and
modded socket roles. No signal sleeping, slower factory steps or reduced visual
update rates were added. Earlier graphics/billboard experiments did not establish
a substantial current-stack gain and are not included.

Further rendering, collision and save/streaming changes remain separate profiling
targets. The current evidence does not justify adding speculative approximations,
new physics behavior or background access to live Unity save objects. Existing
batch recovery and cooperative mesh construction remain in place; their hitch
measurements are in [batch recovery](BATCH_RECOVERY.md).

## Explicit diagnostics

`NetworkCaptureSeconds = 0` installs no diagnostic observers or counters. Set a
positive duration before restarting to capture up to 600 seconds. The mod writes
a timestamped CSV and `.peers.csv` in `UserData`, reporting distiller notifications,
suppression, transport attempts, real limit errors, retained packets, retries,
queue high-water and closed peers. Peer samples contain peak pending/unacknowledged
reliable bytes, queue time and ping, at most once per second for 64 handles.
Payloads, player names and addresses are not recorded. Capture overhead makes these
sessions unsuitable for FPS comparisons. Restore zero after collecting evidence.

## Acceptance still required

Real remote-player joining, disconnect/rejoin and sustained multiplayer production
remain unverified. Full mouse-driven cable dragging/rewiring and pickup acceptance
also remain separate from native scripted graph and interaction checks. No claim
of unrestricted gameplay or arbitrary mod compatibility follows from these tests.
