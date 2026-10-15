# Release preparation

Prepared identity: **SilentHacks / StellarPerformance**, version **0.7.0**,
intended tag **v0.7.0** and asset **StellarPerformance.zip**. The root `mod.json`
is the canonical package manifest; the submission's manifest-folder field will be empty.

The local preparation includes source on `main`, a reviewed commit, matching version
metadata, documentation, credits, MIT licence, test results and a verified package receipt.
Artifacts are ignored by Git. No GitHub repository, remote, public release or submission
is created by the preparation or packaging commands.

Before publication, review `VALIDATION.md` and decide whether the documented experimental
support level is appropriate. The candidate has no newly established FPS improvement claim.
Existing preferences can enable experiments despite the new-install defaults.

When separately authorized to publish:

1. Create the public `SilentHacks/StellarPerformance` repository and upload the reviewed
   `main` commit. Do not include the sibling private directory or generated game references.
2. Confirm `mod.json` is present on `main` and all four required fields are populated.
3. Create the GitHub release from the prepared `v0.7.0` source tag. Use the matching
   changelog and describe the validation limits and experimental defaults.
4. Attach `artifacts/0.7.0/StellarPerformance.zip`. Its name must match the actual repository;
   if the repository name changes, rerun `scripts/Package.ps1 -RepositoryName <actual-name>`.
   GitHub's automatic source ZIP is not the installable mod asset.
5. Download the attached asset, compare its SHA-256 with the local receipt, inspect the two
   allowlisted entries, and verify installation before submitting.
6. Submit the username, repository and empty manifest-folder field through the
   [official mod submission form](https://voxar.dev/submit/).

These external steps have not been performed. Do not push or submit as part of local cleanup.
