# Stellar Performance

Focused optimisations for StellarDrive Demo.

## Features

- Ship-grid bounds caching.
- Fluid cable topology caching, with live visual updates every frame.
- Local room rebuilding after edits.
- Single-player reuse of authoritative room queries.
- Fixed-part mesh batching with compatibility guards and recovery after interaction.
- Suppression of identical native replication payloads for inspected machines and other supported parts.
- Exact signal connection scan reuse.
- Offscreen culling of inspected ship-part canvases, with native readout updates and focused controls.
- Live Faithful, Balanced, Performance, Aggressive and Custom presets, with an in-game F8 selector.
- Experimental Steam congestion recovery, disabled by default.

Simulation, physics and picking retain native rates. Balanced retains native presentation. Higher presets reduce contours, shadow/vegetation range and cosmetic refresh rates while preserving the landscape and native readouts. Supported part notifications can omit identical payloads; incremental-room gas rounding can differ slightly.

## Install

Requires Windows, StellarDrive Demo 0.8.2 and MelonLoader 0.7.3.

Install through Stellar Mod Manager.

For manual installation, close the game and install the requirements separately. Download StellarPerformance.zip from [GitHub releases](https://github.com/SilentHacks/StellarPerformance/releases) and extract it into the game folder. The DLL belongs at Mods/StellarPerformance.dll.

To uninstall, close the game and remove that DLL.

## Settings

In 2.0.0, press F8 or choose Performance in a menu. Preset changes
apply during play and are saved without restarting or reconnecting. Balanced keeps
native presentation. See [live presets](docs/PRESETS.md) for trade-offs and multiplayer rules.

Individual startup feature/compatibility overrides remain in the StellarPerformance
section of UserData/MelonPreferences.cfg. Close the game before editing that file manually.

See [settings and compatibility](docs/CONFIGURATION.md) for feature switches and diagnostics.

## Performance and limits

The 1.0.0 build measured 47.98% lower mean frame time across six paired runs in the recorded 20-ship world. Median per-run FPS rose from 36.78 to 70.56 on the tested PC and mod stack.

Results vary by world and hardware.

See the [benchmark evidence](docs/BENCHMARKS.md) and [validation limits](docs/VALIDATION.md).
The 2.0.0 batching changes and their measurements are documented in
[batch recovery](docs/BATCH_RECOVERY.md).
Networking and signal work is documented in
[large-build optimisation](docs/SCALE_OPTIMISATION.md), including validation limits.
Canvas behavior, camera/build acceptance and incremental FPS measurements are in
[canvas culling](docs/CANVAS_CULLING.md).
The higher-preset trade-offs and measured gains are in
[live presets](docs/PRESETS.md); changes within roughly ±3% FPS are treated as inconclusive.
Supported replication types, preservation rules and validation are documented in
[replication deduplication](docs/REPLICATION_DEDUPLICATION.md).

Five guarded CPU optimisations cover repeated power-cable visits, exact save
serialization, signal evaluation, surface-space clustering and empty warp
bookkeeping. They default on for new or missing preferences across every tier,
including Faithful. Existing `false` overrides stay respected; changing these
startup switches requires a restart, and the F8 selector does not reset them.
Replacement save writers, including the inspected Foundations/Workshop async
pipeline, keep their own path and receive no additional serialization gain.
See [CPU research and native measurements](docs/CPU_RESEARCH.md) for workload
results and compatibility limits.

## More information

- [Release notes](docs/RELEASE_NOTES.md)
- [Changelog](CHANGELOG.md)
- [Build from source](docs/DEVELOPMENT.md)
- [Credits](CREDITS.md) and [MIT license](LICENSE)
