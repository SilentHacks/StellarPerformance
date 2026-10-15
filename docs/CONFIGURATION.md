# Configuration and compatibility

## Supported versions

- Windows; inspected StellarDrive Demo 0.8.2 / Unity 6000.3.24f1 assemblies.
- MelonLoader 0.7.3 Open-Beta (the tested Mono/net472 loader).
- SDModKit is optional. Its adapters target the inspected 1.3.3 build. Workshop is
  optional; its existing membership and hull caches take precedence over duplicates.

Start the game and inspect the Stellar Performance messages in `MelonLoader/Latest.log`.
Each module reports installed, disabled, or skipped. Modules whose inspected assembly
identities differ are skipped. Conflicting body replacements are skipped; observer
hooks are allowed where the original callbacks remain in use. Changing patches after
initialization is outside the supported contract, except the room module's dirty-update
owner recheck. Compatibility checks cannot guarantee support for arbitrary other mods.

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

Profiling support remains available for troubleshooting, but `Diagnostics = false`
installs no profiling hooks and performs no captures. Feature diagnostic counters are
collected only during an explicit capture, including when diagnostics are enabled.
`DiagnosticMode` and `CaptureSeconds` are dormant while diagnostics are disabled.
Existing saved `Diagnostics = true` is preserved on upgrade; set it to `false` and
restart to disable diagnostics on an existing installation. Normal startup and
compatibility warning messages remain available.

Batching supports readable single-submesh opaque meshes, matching materials and vertex
layouts, and uniform positive transforms. It supports stock URP shaders and the inspected
`Custom/PlanetObject` asset fingerprint. Screens, unknown custom shaders, animated
assemblies, unsupported scripts, mirrored/nonuniform transforms and lightmapped geometry
retain native rendering. Interaction restores affected batches. This is an opt-in prototype.
