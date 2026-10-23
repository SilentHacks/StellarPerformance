Stellar Performance 2.1.0

• Reduce repeated power-cable scans and signal evaluation bookkeeping while preserving native values and update rates.
• Replace large surface-space adjacency graphs with an exact ordered spatial search to improve dense-world scaling.
• Avoid empty warp-drive bookkeeping on the inspected Stellar Foundations stack; real warp drives retain their current updates.
• Reuse byte-identical hull JSON on the native save path. Replacement save writers, including the inspected Foundations/Workshop async pipeline, retain their own path.

All five CPU switches default on for new or missing preferences across every preset, including Faithful and Custom. Existing false overrides remain respected. These switches require a restart; F8 preset changes do not reset them. Compatibility guards keep unsupported inputs and conflicting mods on their existing paths.

Measured gains depend on the workload. Ordinary-world results were modest and variable; the largest gains came from a constructed dense-world stress test. The version/default-policy rebuild is not a new performance measurement. See the CPU research report for artifact hashes, native checks and validation limits:
https://github.com/SilentHacks/StellarPerformance/blob/v2.1.0/docs/CPU_RESEARCH.md

Simulation and physics retain native rates. Save and network formats are unchanged. The existing incremental-room gas rounding tradeoff remains. Experimental reliable Steam retry and diagnostics remain disabled by default.

Requirements
StellarDrive Demo 0.8.2 and MelonLoader 0.7.3 on Windows.

Install
Use Stellar Mod Manager, or close the game and extract StellarPerformance.zip into the game folder.
