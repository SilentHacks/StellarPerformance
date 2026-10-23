# Releases and packaging

## 2.1.0 preparation

The 2.1.0 release adds five guarded CPU optimisations, enabled for new or missing
preferences in every tier while preserving stored opt-outs. See
[CPU research](CPU_RESEARCH.md) for measured artifact identities, native acceptance,
workload-specific results and compatibility limits. The release/version and
default-policy rebuild is not an additional FPS measurement.

The net472 Release build passed without warnings; all 113 offline preservation
cases and all 15 frame-analysis cases passed. Packaging uses `scripts/Package.ps1`
and includes only `Mods/StellarPerformance.dll` and root `mod.json`. The root
[release notes](../RELEASE_NOTES.md) describe the defaults and upgrade behavior.

## Published 2.0.0

[Release v2.0.0](https://github.com/SilentHacks/StellarPerformance/releases/tag/v2.0.0)
was published on 2026-10-22 as the latest stable release, with
[StellarPerformance.zip](https://github.com/SilentHacks/StellarPerformance/releases/download/v2.0.0/StellarPerformance.zip)
and its SHA-256 checksum file. The annotated tag identifies source commit
`e8ce086c98a1c18bcfcd71936470e1e34047b92b`. Later publication documentation on
`main` does not change that release source or asset.

Both public assets were downloaded anonymously after publication. Their hashes,
the embedded DLL, the complete manifest and the two allowlisted ZIP entries matched
the prepared package. The listing and packaged manifest both declare version
2.0.0, `ai: vibecoded`, `development: active` and the descriptive tags. Publication
did not deploy to the local game or modify its settings or worlds.

## 2.0.0 preparation checks

The 2.0.0 release candidate includes the live preset selector, corrected
atmosphere/cloud depth scheduling, batching recovery, canvas culling, signal scan
reuse and expanded identical-payload replication suppression. Balanced keeps native
presentation; higher levels use the graded cosmetic settings in [live presets](PRESETS.md).
The redesigned presets were checked in single-player; earlier multiplayer checks
and their limits remain documented separately. No additional graphics-limited or
multiplayer FPS benefit is claimed.

The root [release notes](../RELEASE_NOTES.md) are the prepared GitHub release body.
`scripts/Package.ps1` rebuilds the committed source and produces the installable
`artifacts/2.0.0/StellarPerformance.zip`, checksum and verification receipt. The ZIP
contains only `Mods/StellarPerformance.dll` and root `mod.json`.
Preparation does not publish a release or push source/tags.

Preparation checks passed: a warning-free net472 Release build, all 83 offline
cases, all 15 frame-analysis cases, and an identical DLL rebuilt from a fresh Git
source archive. The release DLL SHA-256 is
`7409CC3C84845531963DEAA6E2B69C94329D2513E25E855AF592ACA65FA48830`;
the ZIP SHA-256 is
`251A2ADC8F307060A1F0EF106A34634723BCD4AA702E0232FE9690F1FDF4D90C`.

Source line endings were normalised to make the source-archive build reproducible.
This changed debug/module identity relative to native-tested DLL `6335A967…`,
which remains the identity recorded in the preset measurements. Metadata inspection
confirmed identical code for all 469 methods, signatures, fields, assembly
references and embedded resources. The release rebuild was not separately launched
in Unity/Mono; no new native or performance result is claimed.

The manifest follows the [mod-manager guidance](https://jollyname.dev/create/):
stable mod ID, matching version/tag, description within 600 characters, existing
repository-relative images, `ai: vibecoded`, `development: active` and up to six
non-reserved tags. The AI label is the maintainer's choice. Packaging validates the
new fields as well as version and archive contents. `needs` stays empty because
this mod has no required companion mod; MelonLoader is a runtime requirement.

## Published 1.0.0

[Release v1.0.0](https://github.com/SilentHacks/StellarPerformance/releases/tag/v1.0.0)
was published on 2026-10-18 as the latest stable release, with
[StellarPerformance.zip](https://github.com/SilentHacks/StellarPerformance/releases/download/v1.0.0/StellarPerformance.zip).
The annotated tag identifies source commit `18d73828b588a32bc97272ac5320690d27854e25`.
Later publication receipts on `main` do not change that release source or asset.

The package rebuilt with zero warnings/errors and passed all 40 offline cases.
The 15 frame-analysis tests also passed. Its DLL SHA-256 matches the exact build used
for the twelve frame captures and three native sessions in [the benchmark report](BENCHMARKS.md).
See [settings](CONFIGURATION.md) and [release notes](RELEASE_NOTES.md) for the five-feature
profile and the documented atmosphere rounding allowance.

The public asset was downloaded anonymously after publication. The complete ZIP,
embedded DLL, manifest bytes/version and two allowlisted entries were verified:

```text
551E7DB173B3CAB3183F18B07CCDD8B2EB668613761D1CE52ABD4216F1A445A6  StellarPerformance.zip
9739430D903E8803C6847CBF6809A12B16B2D4B3EAB1D1311E53F21A589E60C1  Mods/StellarPerformance.dll
```

The ZIP contains only `Mods/StellarPerformance.dll` and `mod.json`. Notices are embedded
in the DLL; GitHub's automatic source archives are not the installable asset.

The same DLL was installed locally with the game closed and its hash verified. The
five features are enabled and continuous batching validation is off. Obsolete settings
were removed from this mod's preference section; other categories were preserved.
The previous DLL and preferences were backed up privately. Mod-listing submission is
separate and was not performed.

## Published 0.9.0

[Release v0.9.0](https://github.com/SilentHacks/StellarPerformance/releases/tag/v0.9.0)
was published on 2026-10-15 as the latest release, with the installable asset
[StellarPerformance.zip](https://github.com/SilentHacks/StellarPerformance/releases/download/v0.9.0/StellarPerformance.zip).
It includes the unpublished 0.8.0 work and upgrades directly from 0.7.0.
The [release notes](RELEASE_NOTES.md) use a concise format for mod-manager readers.

The annotated `v0.9.0` tag identifies source commit
`97f382411978074d50c93f6e550e09fc5ee5fa3c`. Publication documentation on `main`
can follow the tag without changing the released source or asset.

The asset was downloaded anonymously after publication. Its SHA-256 matched the
prepared ZIP; the shipping DLL hash, manifest version and two allowlisted entries
were also verified:

```text
79BC949B9D328F95B4D7A9C8480F6310CBB54E6381E2625ABA38BAC90B4C8217  StellarPerformance.zip
```

The ZIP contains only `Mods/StellarPerformance.dll` and `mod.json`. MIT and attribution
notices are embedded in the DLL. GitHub's automatic source archives are not the installable
asset. See the configuration and validation documents in the v0.9.0 tag for the
factory-cadence tradeoff, upgrade settings and outstanding gameplay coverage.

Publication did not deploy to the local game. Mod-listing submission has not been performed.

## Published 0.7.0

The public repository is [SilentHacks/StellarPerformance](https://github.com/SilentHacks/StellarPerformance),
with `main` as the default branch and `mod.json` at the repository root.
[Release v0.7.0](https://github.com/SilentHacks/StellarPerformance/releases/tag/v0.7.0)
was published on 2026-10-15 with the installable asset
[StellarPerformance.zip](https://github.com/SilentHacks/StellarPerformance/releases/download/v0.7.0/StellarPerformance.zip).

The annotated `v0.7.0` tag identifies source commit
`5daef04109495036fb08be442db23731b880e916`. Publication documentation on `main`
can follow that tag without changing the released source or asset.

The attached asset was downloaded anonymously after publication. Its SHA-256 matched
the prepared archive, and its two allowlisted entries and manifest fields were verified.

```text
123E88605791EF1B7C26E422946F7C79AE025BE319CB26D141ACF7A938A88D50  StellarPerformance.zip
```

The archive contains only `Mods/StellarPerformance.dll` and `mod.json`. MIT and
attribution notices are embedded in the DLL. GitHub's automatic source ZIP is not
the installable mod asset. See [validation and limits](VALIDATION.md) before using
experimental settings. Existing preferences are retained on upgrade.

Publication did not deploy the mod to the local game. Submission to the mod listing
has not been performed. For that separate step, the submission values are username
`SilentHacks`, repository `StellarPerformance`, and an empty manifest-folder field.

## Preparing another release

1. Review the source and validation limits. Update the manifest, project and Melon
   versions together, update the changelog, and commit the reviewed source.
2. Run `scripts/Package.ps1 -RepositoryName StellarPerformance`, adding
   `-IncludeOptionalModTests` when the inspected optional dependencies are available.
   The script rebuilds, tests, verifies versions and ZIP entries, and writes the ZIP,
   checksum and receipt beneath the ignored `artifacts/<version>/` directory.
   It does not deploy, launch the game or publish anything.
3. When separately authorized to publish, push the reviewed source on `main` and
   the matching annotated `v<version>` tag. Publish a release from that existing tag
   and attach the matching `StellarPerformance.zip`, with release notes describing
   defaults and validation limits.
4. Download the attached asset, compare its SHA-256 with the local receipt, and
   verify its entries and installation before submitting it to the mod listing.

Keep private evidence, game references, backups and generated build output outside
the published source and release archive. The root `mod.json` remains the canonical
package manifest; changing the repository name requires packaging with the matching
`-RepositoryName` so that the attached ZIP has the required name.
