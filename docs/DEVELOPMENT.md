# Development

Install .NET SDK 9 (tested with 9.0.101). The mod targets .NET Framework 4.7.2; offline
tests target .NET 9. Local game references are required and are never downloaded or
redistributed by this project. From the repository root:

```powershell
dotnet build StellarPerformance.sln -c Release
dotnet test tests/StellarPerformance.Tests/StellarPerformance.Tests.csproj -c Release
```

The default game directory is the usual Steam installation. Override it on either command
with `-p:GameDirectory="D:\Games\StellarDrive Demo"`, or set `STELLARDRIVE_GAME_DIRECTORY`.
An ignored `Directory.Build.local.props` can also set `GameDirectory` for this checkout.
Shipping output is `src/StellarPerformance/bin/Release/net472/`.

The 2.0.0 build has 83 offline cases covering retained room, bounds,
fluid topology, batching recovery, native replication serialization, reliable queue
ownership/order/capacity, signal union mutation/order, conservative canvas envelopes,
infinite-far camera projections, preset policies, visual cadence, atmosphere/cloud
depth scheduling and shipping metadata contracts. The depth regression executes
copies of the inspected URP input-summary and depth-copy scheduler for all four
affected passes: reduced contours must copy depth after opaques, before the sky
effects, without requesting normals or a geometry prepass. Repeated transitions
back to native must remove the added dependency. Retired source and its old
counterexamples are preserved in the private audit snapshot. The optional-mod test
property remains available for local references; removed adapter tests are no longer
part of the current suite. Tests execute emitted native method copies and stubs;
they never install Harmony detours on CoreCLR. Building and testing never deploy,
launch, stop or attach to the game.
Replication tests use the native generated inventory-slot writer and compare
complete native payloads, including float bits, existing wire precision, every
slot/vector field, unknown/malformed types and interaction/pickup exceptions.
Native integration and frame evidence are separate from these offline passes;
see [replication validation](REPLICATION_DEDUPLICATION.md).

The 2.1.0 CPU changes add 30 cases (113 total): bitwise signal
projections and ordered native reductions, power-list mutation/observer fallback
contracts, and exact save JSON, resolver-policy, exception and storage contracts.
Spatial tests compare emitted original native graph code against the actual
shipping bridge, including exact BFS/center order, threshold rounding, custom
equality fallback and scratch bounds. Runtime ownership guards remain separate
from pure metadata binding, so these tests never query Harmony patch state.
The five CPU switches default on for new or missing preferences across every
tier, including Faithful and Custom; stored `false` overrides survive upgrades
and live F8 changes. These switches remain startup-only, with all compatibility
guards intact. The optional empty-warp adapter also has native paired
wrapper/state/callback acceptance against its actual shipping helper. The
default promotion changes the DLL hash without changing these CPU algorithms;
keep measured hashes attached to the original explicit-switch runs rather than
claiming new performance measurements. Native evidence and the replacement-save
writer skip, including the inspected Foundations/Workshop async pipeline, are
documented in [CPU research](CPU_RESEARCH.md).

The standard-library raw-frame analysis tests run separately:

```powershell
python -m unittest discover -s tests -p 'test_*traces.py'
```

`CompareFrameTraces.py <manifest> --minimal` validates the five-feature N/M protocol.
The historical N/S/D and feature-ablation analyzers remain for older audit data.
Raw runtime drivers, logs, screenshots, saves and restoration receipts belong in
sibling `private/`. [BENCHMARKS.md](BENCHMARKS.md) records the exact current DLL hash,
run protocol and evidence scope. Passing offline tests does not establish unrestricted
gameplay equivalence or a performance gain.

The shipping project disables implicit source-link/Git queries and maps source paths so
the same SDK and local references reproduce the DLL from a Git checkout or a source ZIP.
PDBs remain local build output; the minimal mod ZIP omits them. Packaging always rebuilds
before testing and archiving, and records the source commit separately in its receipt.

For packaging commands, archive contents and publication checks, see [releases and packaging](RELEASE.md).
