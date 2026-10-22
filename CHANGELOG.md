# Changelog

## 2.0.0

- Update the mod-manager description and add `ai: vibecoded`, `development: active` and descriptive tags, following the current [manifest guidance](https://jollyname.dev/create/).
- Add live Faithful, Balanced, Performance, Aggressive and Custom presets with an F8/menu selector, keyboard access and saved preferences. Preserve the current defaults as Balanced; no restart/reload/reconnect is needed for preset changes.
- Switch native/incremental room reconstruction between complete updates while retaining existing gas state. Add opt-in contour modes, passive signal/oxygen text caps, unreadable-label omission, shadow ceilings, cosmetic exhaust density, inspected part-label distance limits, shadow-map resolution/filtering and visual vegetation range. Keep native simulation, terrain, collisions, resource placement, sound ranges, interaction/picking and genuine replication.
- Fix sky and clouds drawing over terrain, trees and anything above the horizon when scene contours were reduced. The native atmosphere and cloud passes read the depth texture without declaring it, so they relied on the contour prepass. They now declare depth whenever contours are reduced, and only the main-view contours change; build-tool outlines stay native.
- Redesign Performance and Aggressive around measured costs instead of hiding scenery. Add silhouette-only contours (no crease pass or depth-normals prepass) and opt-in cosmetic caps for cable glow/flow targets and still-camera terrain/vegetation detail searches. Remove the 4–12 m label cut-offs, 10% vegetation, 10% exhaust and disabled shadows from the named levels.
- Cull inspected offscreen world-space part canvases before each UI draw, using all rendering cameras and current ship/camera poses. Retain focused controls, native text updates, clipping, billboards, picking and simulation.
- Bound canvas tracking, restore native canvases on edits/unload/failure, and recover loaded visual identities from the native part map. Leave unknown scripts, dynamic layouts and unsupported UI on their native paths.
- Suppress consecutive distiller notifications whose native serialized payloads are identical, retaining native production and server callbacks.
- Extend exact payload deduplication to inspected electrolyzers, wood generators/thrusters, gyro/pilot bookkeeping, velocity/gas sensors, unary processors, signal transmitters, storage and warp timers. Preserve native equality, encoded changes, interaction/pickup transitions and authoritative callbacks; retain independent distiller and other-part switches.
- Add an independently switchable signal connection union cache with exact mutation/order validation. Keep native socket classification and circuit execution.
- Add experimental, bounded reliable Steam congestion recovery (default off), with ordered owned packets, fair retries and explicit disconnect on sustained overload.
- Add explicit startup network captures, default off, reporting replication suppression, Steam congestion, retry/backlog and peer queue statistics.
- Recover fixed-part batches after native hover/highlight interactions end, retaining native rendering for parts whose material or pose changed.
- Reuse combined vertex data during recovery instead of rebuilding whole meshes. Spread construction scans and mesh creation across frames, with bounded mesh sizes and a 16 MiB index-cache ceiling.
- Track overlapping interactions, cancel pending construction work on edits/unload, and fall back to native rendering if recovery cannot safely continue.

## 1.0.1

- Fix unnecessary dependency prompts in Stellar Mod Manager.
- Rewrite the manager description and release notes with short paragraphs and plain-text bullets.
- Simplify the README and update installation requirements. Optimisation behaviour is unchanged.

## 1.0.0

- Reduce the mod to grid bounds, fluid visual topology, incremental rooms, single-player room forwarding and guarded fixed-part mesh batching.
- Remove factory cadence, signal sleeping, picking throttling, UI/distance/terrain approximations and the other secondary caches. Old configuration keys cannot reactivate removed code.
- Restore native per-frame fluid presentation while retaining topology caching.
- Retain native simulation rates, module/patch guards, batching interaction restoration and the small documented room gas rounding tradeoff.
- Replace broad performance claims with measurements of the exact reduced build; see [benchmarks](docs/BENCHMARKS.md).


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
- Make the shipping DLL reproducible from source archives without Git metadata; force
  a fresh Release build before packaging and retain source identity in the receipt.

## 0.7.0 â€” 2026-10-15

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

## 0.6.0 â€” private local build

Added independent incremental room reconstruction, synthetic native room validation,
bounded searches and saved-coordinate repairs. Retained native gas interpolation with
documented local aggregate rounding differences. This historical local build is preserved
privately; it was not a GitHub release from this repository.
