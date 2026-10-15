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

Core tests need only the inspected game and loader. Four optional adapter cases are
explicitly skipped by default. To run them, install the inspected SDModKit 1.3.3 and
Item Lines 0.1.2, then add `-p:IncludeOptionalModTests=true` to the test command. No runtime
tests install Harmony detours on CoreCLR: they execute emitted method copies and stubs.
A separate test inspects the actual net472 DLL, its loader metadata and embedded notices.

For packaging commands, archive contents and publication checks, see [releases and packaging](RELEASE.md).
