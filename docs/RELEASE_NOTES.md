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

Performance varies by world. Extended building, travel and multiplayer coverage remains
incomplete. See [settings and native-detail options](https://github.com/SilentHacks/StellarPerformance/blob/v0.9.0/docs/CONFIGURATION.md)
and [validation details](https://github.com/SilentHacks/StellarPerformance/blob/v0.9.0/docs/VALIDATION.md).
