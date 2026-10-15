# Development rules

- This repository contains Stellar Performance only. Workshop is a separate sibling project.
- Build the shipping mod for net472. Offline tests compile the same sources for net9.0; they must never install Harmony/MonoMod runtime detours on CoreCLR. Use emitted method copies for contract tests.
- Resolve game and loader assemblies from `GameDirectory`; never commit or package those assemblies. Optional mod tests require `-p:IncludeOptionalModTests=true`.
- Keep inspected module guards, bounded storage, nested exception cleanup, native state events, save schema and network format intact. Bind private game members only inside a guarded module installation.
- Private inspection, runtime drivers, logs, screenshots, saves and backup receipts belong in the sibling `private` directory, outside this repository.
- Never deploy or edit game configuration while the game is running. Build and package commands must not deploy, launch, stop or attach to the game.
- Package only the shipping DLL and the root `mod.json`. MIT and attribution notices are embedded in the DLL. Use `scripts/Package.ps1` to validate versions and archive contents.
- Incremental rooms and mesh batching are experimental and default off. Do not claim full building, multiplayer validation or new performance gains from offline tests.
- Diagnostics must default off. Feature diagnostic counters run only during an explicitly started capture; ordinary feature paths must not initialize diagnostic hooks.
- Publishing, pushing, creating GitHub releases and submitting the mod require a separate user instruction.
