# Stellar Performance 0.9.0

Opinionated performance improvements for StellarDrive Demo 0.8.2 and MelonLoader 0.7.3
on Windows. SDModKit is optional. This release includes the unpublished 0.8.0 changes
and upgrades directly from 0.7.0.

## Changes

- Skip unchanged native signal operations, retain wiring across value refreshes and wake
  on live input/configuration changes in native operation order on each factory step.
- Reuse authoritative room-position queries in single-player, avoiding duplicate client
  geometry. Reconstruct native mirrors when leaving single-player or using room-debug views.
- Enable incremental room reconstruction, expanded fixed opaque mesh batching and signal
  query reuse by default. Bound caches and retain native fallback for unsupported cases.
- Reduce distant tiny meshes and part shadows; refresh numeric readouts at 10 Hz and limit
  part UI to 100 m. Focused controls and UI re-entry refresh immediately.
- Reuse terrain LOD selection for small movements, update cosmetic cable values at 20 Hz,
  render object picking every third frame, and remove repeated loader reflection work.
- Advance factory simulation every second network tick with a doubled step. Piloting,
  lever use and ship repositioning restore every-tick processing while active.
- Remove the idle-machinery sleeping experiment after profiling showed a net cost.

## Install and upgrade

Download the attached **StellarPerformance.zip**, close the game, and extract into the
game directory. The archive contains only `Mods/StellarPerformance.dll` and `mod.json`.
MIT and attribution notices are embedded in the DLL. GitHub's automatic source archives
are for development and are not the installable asset.

Existing saved preferences are retained. For the new profile and native-detail overrides,
see [configuration](https://github.com/SilentHacks/StellarPerformance/blob/v0.9.0/docs/CONFIGURATION.md). Diagnostics default off; an existing saved
`Diagnostics = true` remains enabled until changed. Removed machine-sleeping keys are ignored.

## Tradeoffs and validation

The default factory divisor changes simulation scheduling: larger steps can change
discrete production, fluid transfers, derivatives and circuit feedback. Set
`PartSimulationDivisor = 1` to restore every-tick factory processing. Set `StableSignals = false`
as well to restore unchanged signal callbacks. Global physics/network ticks and
save/network formats remain native. Local room gas rounding can differ. Distant detail
can pop, and picking can delay hover/interaction feedback by a few frames.

Release build: zero warnings/errors. Offline checks: 280 passing with optional adapters;
276 passing and four explicit skips without them. After source line-ending normalization,
the shipping DLL's method bodies, embedded resources and runtime metadata except its
module identity match the latest isolated Unity/Mono validation artifact. The rebuild
has not been separately launched in Unity/Mono. In that scene, single-player room reuse removed
about 226 MB of room structures and 3.68 million objects; 519,973 room query samples
matched. These are workload-specific memory results. No whole-game FPS or 1% low
improvement is established for the final follow-up.

Full construction, extended travel, long-running factory accuracy with larger steps,
and real remote-player join coverage remain incomplete. See [validation and limits](https://github.com/SilentHacks/StellarPerformance/blob/v0.9.0/docs/VALIDATION.md)
and the [changelog](https://github.com/SilentHacks/StellarPerformance/blob/v0.9.0/CHANGELOG.md) before selecting the profile.
