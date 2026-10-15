<img src="assets/thumbnail.png" alt="Stellar Performance" width="128" align="right">

# Stellar Performance

Performance improvements for StellarDrive Demo. Reduces repeated work in ship systems,
cables and visuals without changing simulation rates or save formats.

## What it does

- Reuses ship queries, cable topology and unchanged part states.
- Avoids unnecessary allocations and repeated transform, material and UI updates.
- Defers hidden part displays and refreshes them when they become visible.

Experimental room reconstruction and mesh batching are available as opt-in features.
They are disabled by default, along with profiling and the experimental signal/fluid
options. Existing saved settings are preserved on upgrade.

## Install

**Windows | StellarDrive Demo 0.8.2 | MelonLoader 0.7.3**

1. Download [StellarPerformance.zip](https://github.com/SilentHacks/StellarPerformance/releases/latest/download/StellarPerformance.zip).
2. Close the game and extract the ZIP into the game directory. The DLL should be at
   `Mods/StellarPerformance.dll`.

SDModKit is optional. To uninstall, close the game and remove the DLL.

## Settings and help

Settings are under `[StellarPerformance]` in `UserData/MelonPreferences.cfg`.
Setting changes require a restart; normal use needs no configuration.

- [Settings and compatibility](docs/CONFIGURATION.md)
- [Validation and known limits](docs/VALIDATION.md)
- [Build and test instructions](docs/DEVELOPMENT.md)
- [Changelog](CHANGELOG.md)

Licensed under [MIT](LICENSE). See [credits](CREDITS.md) for attribution and the
inspiration behind the room reconstruction work.
