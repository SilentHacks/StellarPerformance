# Releases and packaging

## Prepared 0.9.0

0.9.0 includes the unpublished 0.8.0 work and is prepared for a direct upgrade from
0.7.0. [Release notes](RELEASE_NOTES.md) are ready for the GitHub release body; they
describe the new defaults, factory-cadence approximation, reversible settings and
validation limits. Preparation does not publish a tag, push source or change the
installed game. The last published release recorded here remains 0.7.0.

From a clean, reviewed source commit, run:

```powershell
.\scripts\Package.ps1 -RepositoryName StellarPerformance -IncludeOptionalModTests
```

Omit `-IncludeOptionalModTests` when the inspected optional dependencies are unavailable.
The output is `artifacts/0.9.0/StellarPerformance.zip`, its `.sha256` sidecar and
`verification.json`. The receipt identifies the source commit and intended `v0.9.0`
tag. Use that exact commit when creating the annotated tag; attach the verified ZIP.
The ZIP must contain only `Mods/StellarPerformance.dll` and `mod.json`.

Before publication, review [validation](VALIDATION.md), especially factory scheduling
and incomplete remote-player coverage. After publication, download the attached ZIP
anonymously, compare its SHA-256 with the receipt, and verify its two entries and
manifest version. Record the published tag, commit and asset hash here. Mod-listing
submission remains a separate step.

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
   The script builds, tests, verifies versions and ZIP entries, and writes the ZIP,
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
