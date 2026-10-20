# Release notes

## 1.0.1

Declares SDModKit and Sandbox Menu dependencies and improves the mod-manager description, release notes and README. Optimisation behaviour and settings are unchanged from 1.0.0. No new performance measurements were taken.

The root RELEASE_NOTES.md is the plain-text body used for GitHub releases and the mod manager. It uses short paragraphs and bullet characters because the manager displays release bodies without Markdown rendering.

Requires Windows, StellarDrive Demo 0.8.2, MelonLoader 0.7.3, SDModKit and Sandbox Menu.

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
