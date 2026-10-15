# Development rules

- This repository contains Stellar Performance only. Workshop is a separate sibling project.
- Build the shipping mod for net472. Offline tests compile the same sources for net9.0; they must never install Harmony/MonoMod runtime detours on CoreCLR. Use emitted method copies for contract tests.
- Resolve game and loader assemblies from `GameDirectory`; never commit or package those assemblies. Optional mod tests require `-p:IncludeOptionalModTests=true`.
- Keep inspected module guards, bounded storage, nested exception cleanup, save schema and network format intact. Active simulation steps retain native state events; the user authorized stable-circuit sleeping and reasonable simulation approximations on 2026-10-15, including coalescing unchanged callbacks on sleeping steps. Idle-machinery sleeping was removed before 0.9.0 at the user's request after profiling showed a net cost; do not reintroduce it without new evidence. Bind private game members only inside a guarded module installation.
- Private inspection, runtime drivers, logs, screenshots, saves and backup receipts belong in the sibling `private` directory, outside this repository.
- Never deploy or edit game configuration while the game is running. Build and package commands must not deploy, launch, stop or attach to the game.
- Package only the shipping DLL and the root `mod.json`. MIT and attribution notices are embedded in the DLL. Use `scripts/Package.ps1` to validate versions and archive contents.
- The user authorized opinionated defaults on 2026-10-15, including measured incremental rooms and mesh batching and small room gas rounding differences. Keep behavior tradeoffs documented and settings reversible. Do not claim full building, multiplayer validation or new performance gains from offline tests.
- Diagnostics must default off. Feature diagnostic counters run only during an explicitly started capture; ordinary feature paths must not initialize diagnostic hooks.
- Publishing, pushing, creating GitHub releases and submitting the mod require a separate user instruction.
