# PartFinder releases (automated)

## How versioning works

| File | Purpose |
|------|---------|
| `.github/.release-please-manifest.json` | Current semver (Release Please source of truth) |
| `version.json` | Human-readable app version (synced on CI build) |
| `PartFinder/PartFinder/Package.appxmanifest` | MSIX `Identity Version` (4-part, synced on CI build) |
| `CHANGELOG.md` | Updated by Release Please |

## Automatic flow (recommended)

1. Push conventional commits to **`main`** (e.g. `feat: …`, `fix: …`).
2. **Release Please** opens a PR titled *chore(main): release x.y.z* with version + changelog bumps.
3. **Merge that PR** → GitHub **Release** + tag `vX.Y.Z` is created.
4. **Build MSIX and attach to Release** runs on `release: published`, builds x64 MSIX, uploads `PartFinder_X.Y.Z_x64.msix`.

Repo: [jayandra06/Partfinder-new](https://github.com/jayandra06/Partfinder-new)

## Manual one-off release

Actions → **Build MSIX and attach to Release** → **Run workflow** → enter semver (e.g. `1.0.1`).

This creates the tag/release (if missing) and attaches the MSIX.

## Local build

```powershell
powershell -ExecutionPolicy Bypass -File .\PartFinder\build-msix.ps1
```

## Microsoft Store

Store submissions need Partner Center signing and publisher **Euroasiann Group**; GitHub MSIX builds use a CI dev cert (`CN=DELL`) for sideload/testing only.
