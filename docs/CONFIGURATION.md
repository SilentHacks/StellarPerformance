# Configuration and compatibility

Version 1.0.1 targets the inspected Windows StellarDrive Demo 0.8.2 / Unity
6000.3.24f1 assemblies and MelonLoader 0.7.3. SDModKit support is optional;
fluid topology checks SDModKit's inspected assembly identity when present and permits its known connection hook.

Settings are in `[StellarPerformance]` in `UserData/MelonPreferences.cfg`. Close the
game before editing; restart to apply changes. Existing values for retained keys are
respected. Old keys may remain in the file but have no implementation behind them.
In particular, `PartSimulationDivisor`, `PickingCameraDivisor`, `StableSignals`,
`CableVisualHz`, UI/distance/terrain limits and all removed feature flags are ignored.
Native factory and presentation rates apply regardless of those obsolete values.

| Setting | Default | Purpose |
| --- | --- | --- |
| `ShipGridBounds` | `true` | Cache exact bounds; invalidate on native grid edits. |
| `FluidCableTopology` | `true` | Cache visual connectivity; recompute live flows and native visuals every frame. |
| `IncrementalRooms` | `true` | Reconstruct edited regions locally. Small gas rounding differences remain. |
| `SinglePlayerRooms` | `true` | Forward single-player room queries to the authority; restore the local mirror for peers/debug views. |
| `ShipPartBatching` | `true` | Batch supported fixed opaque meshes; restore native renderers on interaction or topology changes. |
| `BatchingContinuousValidation` | `false` | Inspect every batched source each frame, in addition to retained event hooks. Useful when diagnosing compatibility; costs work. |

Each module logs installed, disabled or skipped in `MelonLoader/Latest.log`.
Assembly identities and patch ownership are checked before installation. Unknown
body replacements fall back to the previous/native implementation. Batching excludes
uninspected scripts, unsupported materials and transforms, and retains native
interaction restoration. A module being enabled in settings does not prove it installed;
check the log when comparing performance.

Changing patches after initialization is outside the supported contract, except the
room dirty-update ownership recheck. Guards do not establish compatibility with arbitrary
mods or future game builds. See [current validation and measurements](BENCHMARKS.md).

The old integrated capture hotkeys, counters and loader reflection patch are removed.
Current measurements use a separate private native frame recorder, absent from the shipping DLL.
