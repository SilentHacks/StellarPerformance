# Stellar Performance

Performance improvements for **StellarDrive Demo 0.8.2**, maintained by **SilentHacks**.
Version **0.7.0** is a locally prepared release candidate. It reduces repeated ship
queries, cable graph construction, native record allocations and redundant visual work.
It does not lower physics, simulation or network update rates or change the save schema.

**Incremental room reconstruction and fixed-part mesh batching are experimental and
disabled by default.** Broader construction and remote multiplayer validation remain
pending. Historical results are not a guarantee of gains in another world or with the
default settings; see [validation and measurement limits](docs/VALIDATION.md).

## Requirements and installation

- Windows; inspected StellarDrive Demo 0.8.2 / Unity 6000.3.24f1 assemblies.
- MelonLoader 0.7.3 Open-Beta (the tested Mono/net472 loader).
- SDModKit is optional. Its adapters target the inspected 1.3.3 build. Workshop is
  optional; its existing membership and hull caches take precedence over duplicates.

Close the game before installing or changing configuration. For manual installation,
extract `StellarPerformance.zip` into the game directory. The result should be
`Mods/StellarPerformance.dll`; `mod.json` is package metadata. Alternatively import
the archive through Stellar Mod Manager. No game assemblies or other mods are bundled.

Start the game and inspect the Stellar Performance messages in `MelonLoader/Latest.log`.
Each module reports installed, disabled, or skipped. Modules whose inspected assembly
identities differ are skipped. Conflicting body replacements are skipped; observer
hooks are allowed where the original callbacks remain in use. Changing patches after
initialization is outside the supported contract, except the room module's dirty-update
owner recheck. Compatibility checks cannot guarantee support for arbitrary other mods.

To uninstall, close the game and remove `Mods/StellarPerformance.dll`. Existing preference
keys can remain. This mod supplies no save replacement or data migration.

## Settings

Settings are in `[StellarPerformance]` in `UserData/MelonPreferences.cfg` and require a
restart, except capture duration is read when a capture starts. Existing installations
retain their saved values: upgrading does **not** reset experimental switches to defaults.
Review `IncrementalRooms`, `ShipPartBatching`, `SignalEvaluationReads` and
`FluidGraphSocketReads` explicitly when upgrading.

| Setting | New-install default | Purpose |
| --- | --- | --- |
| `WorldObjectSnapshots` | `true` | Host: one native world-object snapshot per synchronization pass. |
| `CableMeshBuffers` | `true` | Reuse zeroed, exact-length arrays during synchronous cable mesh uploads; retained storage capped at 6 MiB per thread. |
| `ContentMembership` | `true` | Optional SDModKit adapter: cache membership while resolving definitions and state live. |
| `ScopedHullMeshReads` | `true` | Reuse source mesh reads within one rebuild; defer to Workshop's cache when present. |
| `ShipGridBounds` | `true` | Cache ship bounds with grid-mutation invalidation. |
| `StatefulPartQueries` | `true` | Reuse resolved part collections across native updater queries. |
| `FluidCableTopology` | `true` | Rebuild the visual graph on cable/connection/part changes; read flow state live. |
| `PartVisualUpdates` | `true` | Avoid redundant idle, billboard and liquid presentation work. |
| `ModKitToolRefs` | `true` | Optional SDModKit adapter: resolve currently selected tools without scene scans. |
| `DistillerSlotReuse` | `true` | Preserve equal inventory-slot arrays so native record equality can filter updates. |
| `PhysicsSceneTransforms` | `true` | Avoid exact-equal collider writes and repeated scene-preparation work; prune inactive spaces. |
| `WorldVisualUpdates` | `true` | Reduce repeated hotspot transform and cleanup work. |
| `ClientSignalCables` | `true` | Reuse validated signal membership and per-prefab signal socket reads. |
| `NativeStateReuse` | `true` | Reuse unchanged native records while keeping calculations and state events. |
| `WorldUiVisibility` | `true` | Coalesce hidden display changes and refresh on re-entry. |
| `CullOffscreenWorldUi` | `true` | Suppress registered offscreen Canvases; requires `WorldUiVisibility`. Focused fields remain active. |
| `WorldUiMaximumDistance` | `0.0` | Zero preserves native distance. A positive distance is an optional visual tradeoff, clamped to 0–9000 m. |
| `PacketTrailerHardening` | `true` | Preserve native channel framing while providing safe trailer capacity. No MTU, queue or congestion fix is claimed. |
| `FluidGraphSocketReads` | `false` | Experimental fallback: reuse socket arrays within one fluid update. Assumes prefab hierarchy is stable during that call. |
| `SignalEvaluationReads` | `false` | Experimental host signal membership projection; values, roles and schedules stay live. |
| `IncrementalRooms` | `false` | Experimental local room reconstruction with valid reload coordinates; gas rounding can differ from native. Do not stack room rewriters. |
| `ShipPartBatching` | `false` | Experimental batching of supported fixed opaque part meshes. Unsupported geometry stays native. |
| `BatchingContinuousValidation` | `false` | Inspect batching sources every frame for compatibility. Can add substantial CPU cost. |
| `Diagnostics` | `false` | Enable bounded captures with Ctrl+Shift+F8. |
| `DiagnosticMode` | `"Lean"` | `Frame`: frame/GC bookkeeping. `Lean`: aggregate probes. `Full`: detailed, more expensive probes. |
| `CaptureSeconds` | `30.0` | Capture duration, clamped to 5–120 seconds. |

`CableScratchCollections`, `SignalScratchCollections` and `TerrainScratchCollections`
were removed in 0.7.0. Old saved keys are ignored; they no longer install patches.

Batching supports readable single-submesh opaque meshes, matching materials and vertex
layouts, and uniform positive transforms. It supports stock URP shaders and the inspected
`Custom/PlanetObject` asset fingerprint. Screens, unknown custom shaders, animated
assemblies, unsupported scripts, mirrored/nonuniform transforms and lightmapped geometry
retain native rendering. Interaction restores affected batches. This is an opt-in prototype.

## Building and tests

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

## Preparing a release locally

After reviewing and committing the source:

```powershell
.\scripts\Package.ps1 -RepositoryName StellarPerformance -IncludeOptionalModTests
```

Omit `-IncludeOptionalModTests` on a core-only installation. `-GameDirectory` overrides the
game path. The script builds, tests, checks versions, and verifies every ZIP entry against
its source hash. It writes `artifacts/0.7.0/StellarPerformance.zip`, a SHA-256 file and a
verification receipt. It does not deploy, launch the game or publish anything.

The archive contains exactly `Mods/StellarPerformance.dll` and `mod.json`. The DLL embeds
the full MIT licence and attribution. [Release preparation](docs/RELEASE.md) records the
remaining external steps. [Changes](CHANGELOG.md) describes the cleanup.

## Credits and licence

The room work introduced in 0.6.0 was inspired by
[tidic84's StellarDrive-ShipPerformanceFix](https://github.com/tidic84/StellarDrive-ShipPerformanceFix).
Our implementation and tests were written independently against the inspected game
contracts. See [credits and provenance](CREDITS.md). Stellar Performance's own source is
provided under the [MIT licence](LICENSE).
