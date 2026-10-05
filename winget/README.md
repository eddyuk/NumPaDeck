# winget manifest for `winget install eddyuk.NumPaDeck`

## Status

| Version | Manifest state | winget-pkgs PR |
|---------|---------------|----------------|
| 1.0.0 | ✅ SHA256 from CI, validated (`winget validate` clean) | [#447079](https://github.com/microsoft/winget-pkgs/pull/447079) — all 10 automated checks green, CLA signed; awaiting winget-pkgs moderator review (normal for a first submission) |

The folder below mirrors the [winget-pkgs](https://github.com/microsoft/winget-pkgs)
layout so manifests can be dropped straight into a fork of `winget-pkgs`:

```
winget-pkgs (fork)
└── manifests/e/eddyuk/NumPaDeck/1.0.0/     ← copy `winget/manifests/e/...` here
    ├── eddyuk.NumPaDeck.yaml                (version)
    ├── eddyuk.NumPaDeck.installer.yaml      (installer)
    └── eddyuk.NumPaDeck.locale.en-US.yaml   (default locale)
```

## Submitting a new version (checklist)

1. **Release is live.** Push tag `v<version>`; the CI pipeline builds
   `NumPaDeck-Setup-<version>.exe`, and the GitHub Release exists with the
   `.sha256` assets.
2. **Copy the real digest.** Grab the setup.exe SHA256 from:
   - the CI log line `SETUP SHA256: …`, or
   - the release asset `NumPaDeck-Setup-<version>.exe.sha256`,
   and replace `INSTALLER_SHA256_PLACEHOLDER` in
   `eddyuk.NumPaDeck.installer.yaml`.
3. **Validate locally** (needs winget 1.4+):
   ```powershell
   winget validate -m winget\manifests\e\eddyuk\NumPaDeck\<version>\eddyuk.NumPaDeck.locale.en-US.yaml
   ```
4. **Fork & PR to `winget-pkgs`:**
   - fork `microsoft/winget-pkgs`,
   - copy the version folder into `manifests/e/eddyuk/NumPaDeck/<version>/`,
   - commit only those three files (one package version per PR),
   - open the PR; the winget-pkgs validation pipeline installs the package in a
     sandbox and reports results on the PR.
   - once the PR is merged, `winget install eddyuk.NumPaDeck` works.
5. **Keep this folder in sync** with what was merged, so the next bump is a
   copy-paste + new digest.

Notes:
- The installer is **per-user, no UAC** → `Scope: user`, `UpgradeBehavior: install`.
- Inno silent switches are explicitly declared (`/VERYSILENT` for `--silent`,
  `/SILENT` for the default silent-with-progress mode).
- `ReleaseDate` should be the actual release date when bumping versions.
