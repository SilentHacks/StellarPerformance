# Changelog

## 0.9.0

Includes the unpublished 0.8.0 work; upgrades directly from 0.7.0.

- Keep stable-signal wiring across periodic value refreshes; invalidate it on native
  cable/part/ship topology changes and stagger initial verification deadlines.
- Avoid duplicate client room geometry in single-player hosts. Forward position
  queries without exposing the authoritative mutable tree; restore native mirrors
  when leaving single-player or enabling a room debug visualizer.
- Allow more small signal networks within the existing 4 MiB connection-cache budget,
  avoiding repeated native unions when the former 128-entry limit was reached.
- Skip unchanged native signal processors and cable groups in native operation order.
  Input/configuration changes wake processing in the same factory step; derivatives run
  on each factory step and stable observations refresh every 250 ms by default.
- Bound stable-signal caches, invalidate on topology changes and exceptions, and preserve
  native execution for unsupported states. Coalesce unchanged callbacks on sleeping
  signal steps; keep global physics/network rates and save/network formats native.
- Add reversible settings, capture-only skip counters, emitted native signal-loop tests
  and private Unity/Mono simulation validation.
- Reuse terrain LOD traversals for small observer movements and skip completed empty
  mesh-delta checks between traversals; refresh at least every 15 frames.
- Recompute cosmetic cable values at 20 Hz while retaining per-frame animation, and
  render the object-picking camera every third frame. Hover feedback can arrive later.
- Advance host factory systems every second network tick with a doubled step. Piloting,
  lever use and ship repositioning retain every-tick processing. Larger steps can change
  discrete production, fluid and circuit behavior; global physics/network ticks stay native.
- Cache MelonLoader's inspected sibling-order reflection path and suppress clipping on
  hidden registered world-space part UI. Restore suppressed presentation when needed.
- Compare cable topology fields without nested value-type boxing.
- Remove the idle-machinery sleeping candidate (`SleepingMachinery`,
  `IdleMachineProbeSeconds`). Sampled profiles showed native machine updates cost about
  0.04 ms per frame in the measured world and sleeping raised that to about 0.11 ms.
  Old saved keys are ignored.
- Enable measured incremental room reconstruction, fixed part mesh batching and signal
  query reuse by default; accept the documented room gas rounding differences.
- Expand opaque body batching to distillers, electrolyzers, numeric constants, gas
  meters/exhausts, wood thrusters and strictly screened generic parts; keep moving
  machinery, liquids, screens and effects native.
- Add bounded distance detail for supported opaque fixed part meshes and batch replacements:
  retain full geometry within 20 m, omit meshes below 3 projected pixels at distance and
  stop part shadow casting beyond 40 m, with hysteresis and native restoration.
- Coalesce numeric signal/gas readouts at 10 Hz and default part UI distance to 100 m;
  focused input, control changes and re-entry refresh immediately.
- Preserve existing saved configuration choices; document native-detail overrides.

## 0.7.0 — 2026-10-15

- Separate Performance source, tests and packaging from portable-manager and Workshop files.
- Remove cable, signal and terrain temporary-collection experiments and their settings.
- Defer private diagnostic bindings until guarded probe installation and enforce module
  initialization after compatibility checks.
- Keep profiling off by default and collect feature diagnostic counters only during
  explicitly started captures.
- Prune inactive physics spaces and retained object snapshots.
- Reject conflicting whole-body replacements, argument rewrites and patches on bypassed helpers.
- Separate scene assignment, cable material presentation, liquid visuals and diagnostic hooks.
- Split mixed target tests, make historical report versions explicit, and make optional
  adapter tests opt-in with visible skips.
- Inspect the actual net472 artifact and package only an explicit, verified file list.
- Default incremental rooms off until broader construction/save/reload coverage is complete;
  batching remains off. Existing saved preferences are preserved.
- Add complete settings/build/validation documentation, MIT licensing under SilentHacks,
  and credit to tidic84's ShipPerformanceFix for inspiration for the independent room work.

## 0.6.0 — private local build

Added independent incremental room reconstruction, synthetic native room validation,
bounded searches and saved-coordinate repairs. Retained native gas interpolation with
documented local aggregate rounding differences. This historical local build is preserved
privately; it was not a GitHub release from this repository.
