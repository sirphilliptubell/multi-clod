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
shortcut - but always state the computed version bump before doing anything irreversible.

**Never run `git push` for anything other than the release tag itself as part of a deploy** (no
pushing commits, no force-push, etc.) - but the tag is the one exception: creating and pushing
`vX.Y.Z` is part of the shortcut, not left for the user to do manually.

1. Ignore git/commit state entirely - do not check whether the working tree is clean, staged,
   committed, or pushed, and do not stop to ask about it. Deploy packages and publishes whatever is
   currently on disk, regardless of commit status.
2. Find the latest release tag: `git describe --tags --abbrev=0 --match "v*"`. If none exists yet,
   treat the current version as `v0.0.0`.
3. Compute the next version:
   - `deploy feat` -> bump minor, reset patch to 0 (e.g. `v1.3.2` -> `v1.4.0`)
   - `deploy fix`  -> bump patch (e.g. `v1.3.2` -> `v1.3.3`)
4. Tell the user the old -> new version before doing anything irreversible.
5. Confirm `MULTICLOD_DEPLOY_PATH` is set in the current shell - `scripts\Publish-MultiClodApp.ps1`
   errors out immediately if it isn't (it's intentionally never committed to this repo).
6. From the repo root, run:
   ```powershell
   .\scripts\Publish-MultiClodApp.ps1 -Version X.Y.Z
   ```
   (no `v` prefix here - that's the plain semver the script and `vpk` expect).
7. Only once that publish succeeds, tag and push: `git tag vX.Y.Z` then `git push origin vX.Y.Z`.
   Tagging after a successful publish (not before) means a failed publish never leaves a tag
   pointing at a version that was never actually made available on the update feed.
8. Report success or failure back to the user. If `vpk pack` or the `robocopy` publish step fails,
   say so explicitly, and skip step 7 - there's nothing to tag.
