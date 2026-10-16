# PartFinder releases (automated)

Repo: [jayandra06/Partfinder-new](https://github.com/jayandra06/Partfinder-new)

## Fix: "Actions is not permitted to create or approve pull requests"

Release Please needs one of these:

1. **Repo setting (recommended)**  
   **Settings → Actions → General → Workflow permissions**  
   - Select **Read and write permissions**  
   - Enable **Allow GitHub Actions to create and approve pull requests**  
   (Org owners may need to allow this for the organization first.)

2. **Personal access token (optional)**  
   Create a classic PAT with `repo` scope, add repository secret **`RELEASE_PLEASE_TOKEN`**, re-run **Release Please**.

If you cannot enable PR creation, use **Release (direct)** below (no PR).

## Release (direct) — works without PR permission

**Actions → Release (direct) → Run workflow** → choose `patch` / `minor` / `major`.

This will:

1. Bump `.github/.release-please-manifest.json`, `version.json`, `CHANGELOG.md`, and `Package.appxmanifest`
2. Commit and push to `main`
3. Create tag `vX.Y.Z` and a **GitHub Release**
4. Trigger **Build MSIX and attach to Release** (uploads `PartFinder_X.Y.Z_x64.msix`)

## Release Please (optional, conventional commits)

1. Use conventional commits on `main`: `feat:`, `fix:`, `feat!:` / `BREAKING CHANGE:`
2. Release Please opens a PR *chore(main): release x.y.z*
3. Merge the PR → tag + release → MSIX workflow runs

Non-conventional commit messages are skipped in the changelog (warnings in the log are normal for older history).

## Version files

| File | Purpose |
|------|---------|
| `.github/.release-please-manifest.json` | Semver source of truth |
| `version.json` | App version (synced on bump / CI) |
| `PartFinder/PartFinder/Package.appxmanifest` | MSIX four-part version |
| `CHANGELOG.md` | Release notes |

## Manual MSIX only

**Actions → Build MSIX and attach to Release** → enter an existing semver (e.g. `1.0.1`).

## Local build

```powershell
powershell -ExecutionPolicy Bypass -File .\PartFinder\build-msix.ps1
```

## Microsoft Store

Use Partner Center with publisher **Euroasiann Group** and Store signing; GitHub MSIX uses a CI dev cert (`CN=DELL`) for sideload/testing only.
