# Stellar Performance

Five focused optimisations for StellarDrive Demo. Version 1.0.1 updates mod-manager dependencies and presentation; optimisation behaviour is unchanged from 1.0.0.

## Features

- Ship-grid bounds caching.
- Fluid cable topology caching, with live visual updates every frame.
- Local room rebuilding after edits.
- Single-player reuse of authoritative room queries.
- Fixed-part mesh batching with compatibility guards.

Simulation, physics, networking, picking and visual refresh rates stay native. Room gas rounding can differ slightly. Settings are reversible, and unsupported modules retain native paths.

## Install

Requires Windows, StellarDrive Demo 0.8.2, MelonLoader 0.7.3. SDModKit is optional; Sandbox Menu is not required.

Install through Stellar Mod Manager. The mod has no required mod dependencies.

For manual installation, close the game and install the requirements separately. Download StellarPerformance.zip from the [1.0.1 release](https://github.com/SilentHacks/StellarPerformance/releases/tag/v1.0.1) and extract it into the game folder. The DLL belongs at Mods/StellarPerformance.dll.

To uninstall, close the game and remove that DLL.

## Settings

Edit the StellarPerformance section in UserData/MelonPreferences.cfg, then restart the game. Diagnostics default off.

See [settings and compatibility](docs/CONFIGURATION.md) for the five feature switches and the batching compatibility option. Removed pre-1.0 settings are ignored.

## Performance and limits

The 1.0.0 build measured 47.98% lower mean frame time across six paired runs in the recorded 20-ship world. Median per-run FPS rose from 36.78 to 70.56 on the tested PC and mod stack.

These results describe that workload. Version 1.0.1 has no new performance measurements.

See the [benchmark evidence](docs/BENCHMARKS.md) and [validation limits](docs/VALIDATION.md).

## More information

- [Release notes](docs/RELEASE_NOTES.md)
- [Changelog](CHANGELOG.md)
- [Build from source](docs/DEVELOPMENT.md)
- [Credits](CREDITS.md) and [MIT license](LICENSE)
