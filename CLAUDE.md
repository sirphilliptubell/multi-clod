# multi-clod

A WPF desktop app (`MultiClod.App`) that manages a tree of projects/sessions in a left-hand pane
and hosts embedded Claude Code CLI sessions (via a vendored Microsoft terminal control) on the
right. Releases are packaged with Velopack (`vpk`) and pushed to a network-share update feed that
running instances auto-update from.

## Deploy shortcuts

Typing `deploy feat` or `deploy fix` triggers a full release: version bump, tag, package, and
publish to the update feed. `feat` bumps the minor version; `fix` bumps the patch version. No
version is stored in any project file - git tags (`vX.Y.Z`) on `main` are the single source of
truth for the current released version (see `README.md` for the manual publish process this
automates).

When triggered, do all of this without stopping to ask at each step - that's the point of the
shortcut - but always state the computed version bump before doing anything irreversible:

1. Confirm the working tree is clean (`git status`) and on `main`. If not, stop and ask - this
   command deploys whatever is currently checked out, so uncommitted or unpushed work needs to be
   resolved first.
2. Find the latest release tag: `git describe --tags --abbrev=0 --match "v*"`. If none exists yet,
   treat the current version as `v0.0.0`.
3. Compute the next version:
   - `deploy feat` -> bump minor, reset patch to 0 (e.g. `v1.3.2` -> `v1.4.0`)
   - `deploy fix`  -> bump patch (e.g. `v1.3.2` -> `v1.3.3`)
4. Tell the user the old -> new version, then:
   - `git tag vX.Y.Z`
   - `git push origin vX.Y.Z`
5. Confirm `MULTICLOD_DEPLOY_PATH` is set in the current shell - `scripts\Publish-MultiClodApp.ps1`
   errors out immediately if it isn't (it's intentionally never committed to this repo).
6. From the repo root, run:
   ```powershell
   .\scripts\Publish-MultiClodApp.ps1 -Version X.Y.Z
   ```
   (no `v` prefix here - that's the plain semver the script and `vpk` expect).
7. Report success or failure back to the user. If `vpk pack` or the `robocopy` publish step fails,
   say so explicitly - the git tag was already pushed at that point, so the tag would be ahead of
   what's actually live on the update feed.
