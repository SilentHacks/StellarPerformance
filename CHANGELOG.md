# Changelog

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
