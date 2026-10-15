# Credits and provenance

Stellar Performance is maintained by [SilentHacks](https://github.com/SilentHacks).

The incremental room reconstruction work introduced in 0.6.0 was inspired by
[tidic84's StellarDrive-ShipPerformanceFix](https://github.com/tidic84/StellarDrive-ShipPerformanceFix).
That project highlighted the cost of recomputing air regions after hull edits and
motivated our investigation of local room reconstruction. Our runtime code and
tests were independently implemented against the inspected game contracts; no
upstream source was copied into this mod.

Thanks to CuriousOwlGames for StellarDrive, and the MelonLoader, Harmony and
SDModKit projects for the modding infrastructure this mod uses. Those projects
and the game retain their own ownership and licences. Their binaries and the
private inspection material are not redistributed here.

Stellar Performance's own source is licensed under MIT. The shipping DLL embeds
this file as `StellarPerformance.CREDITS` and the full licence notice as
`StellarPerformance.LICENSE`, so both accompany the minimal mod archive.
