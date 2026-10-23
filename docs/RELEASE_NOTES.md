# Release notes

## 2.1.0

- Add guarded CPU optimisations for power-cable scans, live signal reduction, exact native save serialization, large surface-space clustering and empty warp-drive bookkeeping.
- Enable all five for new or missing preferences in every preset. Preserve stored `false` overrides; these switches require a restart and remain independent of F8 changes.
- Preserve native values, ordering, simulation rates and save/network formats. Unsupported inputs and conflicting mods retain their existing paths. The inspected Foundations/Workshop async writer receives no additional serialization gain.
- Ordinary-world FPS results are modest and variable. The largest measured gains apply to a constructed dense-world stress test; see [CPU research](CPU_RESEARCH.md) for measured artifact hashes, native acceptance and limits. The release/version and default-policy rebuild is not a new FPS measurement.
- Add 30 offline preservation cases (113 total). Multiplayer certification and arbitrary-mod compatibility remain outside the recorded acceptance.

Requires Windows, StellarDrive Demo 0.8.2 and MelonLoader 0.7.3.

## 2.0.0

- Add an F8/menu preset selector with Faithful, Balanced, Performance, Aggressive and Custom levels. Apply and save changes during play without restarting, reloading or reconnecting. Balanced keeps the current development defaults.
- Performance and Aggressive keep the whole scene visible. They use silhouette-only or no scene contours, shorter shadows, reduced vegetation range and less frequent cosmetic cable and still-camera terrain-detail updates. Both measured about 11% above Balanced in the stationary reference scene; Aggressive did not add FPS there, and gains on graphics-limited PCs are unmeasured. Simulation stays native.
- Declare the atmosphere/cloud depth dependency when reducing contours, fixing sky effects drawing over terrain, trees and wall tops.
- Update the manager description, AI disclosure (`vibecoded`), active development status and tags.
- Custom also offers readout refresh, small-label and part-label limits, shadow filtering/resolution and exhaust density. Faithful selects native room reconstruction; changing level retains existing gas amounts. Host and clients can select different levels.
- Hide inspected offscreen ship-part UI canvases, checking visibility before rendering and keeping focused controls and native readout updates active. Unknown UI retains native drawing.
- Restore mesh batching after hover and highlight interactions, keeping more of the rendering performance benefit as you interact with your build.
- Reuse existing combined meshes and spread batch construction across frames to reduce rebuild hitches.
- Reduce repeated signal-network scanning on large circuit builds while keeping native circuit calculations and update timing.
- Reduce redundant replication for distillers, electrolyzers, wood generators/thrusters and other inspected parts while preserving native calculations, inventory, power, fluid output and genuine encoded changes. Repeated client notifications can occur less often.
- Add experimental recovery for reliable Steam packets rejected during temporary congestion. Disabled by default; persistent overload can disconnect the affected peer.
- Add optional network diagnostics for investigating congestion and large multiplayer builds. Disabled by default.

No new simulation approximations. Save and network formats are unchanged. The existing room-gas rounding tradeoff remains.

Requires Windows, StellarDrive Demo 0.8.2 and MelonLoader 0.7.3.

## 1.0.1

Fixed unnecessary dependency prompts in Stellar Mod Manager.

Requires Windows, StellarDrive Demo 0.8.2 and MelonLoader 0.7.3.

## 1.0.0

Five features remain: exact grid bounds caching, fluid visual topology caching,
incremental rooms, authoritative single-player room forwarding and guarded fixed-part
mesh batching. Factory, circuit, picking, physics, UI and presentation rates are native.
Fluid visuals read live state and update every frame. Room gas rounding can differ slightly.

All other optimisation implementations and their settings were removed. Existing saved
keys for removed features are ignored. Retained settings remain reversible; see
[configuration](CONFIGURATION.md). See [benchmarks](BENCHMARKS.md) for the measured build,
workload, results and behaviour limits. The release retains the measured build and its documented limits.

Requires Windows, StellarDrive Demo 0.8.2 and MelonLoader 0.7.3. Close the game before
installing the built DLL. SDModKit is optional.

---

## Historical release notes

# Stellar Performance 0.9.0

- Skips unchanged signal work and reuses cable connections to reduce repeated processing
  and allocations.
- Reduces single-player memory use by reusing the host's room-position queries.
- Enables faster local room rebuilding and expanded fixed-part mesh batching by default.
- Reduces distant tiny meshes and shadows. Numeric part readouts refresh at 10 Hz;
  focused controls remain responsive.
- Reuses terrain detail selections, refreshes cosmetic cable values at 20 Hz and renders
  object picking every third frame. Hover feedback can arrive a few frames later.

Factory systems now advance every second network tick with a doubled step. This can
change production, fluid and circuit behavior. Set `PartSimulationDivisor = 1` for
native factory timing; also set `StableSignals = false` to restore unchanged signal
callbacks. Piloting, lever use and ship repositioning retain every-tick processing.
Global physics/network ticks and save formats stay native. Local room gas rounding can differ.

Requires **StellarDrive Demo 0.8.2** and **MelonLoader 0.7.3** on Windows. SDModKit is
optional. Existing settings are preserved on upgrade; diagnostics default off.

For manual installation, close the game and extract **StellarPerformance.zip** into
the game directory.

Performance varies by world. See [settings and native-detail options](https://github.com/SilentHacks/StellarPerformance/blob/v0.9.0/docs/CONFIGURATION.md)
and [validation details](https://github.com/SilentHacks/StellarPerformance/blob/v0.9.0/docs/VALIDATION.md).
