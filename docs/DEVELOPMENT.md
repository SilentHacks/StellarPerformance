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

The reduced 1.0.0 has 40 offline cases covering retained room, bounds,
fluid topology, batching and shipping metadata contracts. Retired source and its old
counterexamples are preserved in the private audit snapshot. The optional-mod test
property remains available for local references; removed adapter tests are no longer
part of the current suite. Tests execute emitted native method copies and stubs;
they never install Harmony detours on CoreCLR. Building and testing never deploy,
launch, stop or attach to the game.

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
