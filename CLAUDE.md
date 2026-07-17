# multi-clod

A WPF desktop app (`MultiClod.App`) with a rail/panel/canvas layout: a narrow icon rail on the far
left selects between features - Sessions (a panel managing a tree of projects/sessions, with a
canvas hosting embedded Claude Code CLI sessions via a vendored Microsoft terminal control), Skills
(a panel listing personal Claude Code skills from `~/.claude/skills`, with a canvas that renders the
selected skill's `SKILL.md` and can switch to a raw-text editor), and Settings (no tree/list panel -
just a canvas of persisted toggles/fields, e.g. default root folder, git worktree usage, Claude
permission mode, and app theme). Releases are packaged
with Velopack (`vpk`) and published as GitHub Releases on this repo (via
`.github/workflows/release.yml`, triggered by pushing a `vX.Y.Z` tag); running instances auto-update
from there.

## Debug vs Release processes

Debug and Release builds both produce `MultiClod.App.exe`/`MultiClod.App.dll` (same assembly
identity - WPF's pack URI resolver for `ResourceDictionary.Source` merges in `App.xaml`, including
third-party ones like Markdig.Wpf's, breaks if that's ever renamed per-configuration, since
`Application.ResourceAssembly` can't be corrected from code either - it's already locked before any
of `App.Main`'s own statements run). They're distinguished by **output folder** instead:
`src\MultiClod.App\bin\x64\Debug\net8.0-windows\...` (or wherever `dotnet build`/F5 put it) vs. the
installed Release copy at `%LOCALAPPDATA%\MultiClod.App\current\MultiClod.App.exe`. A running
Debug build's window also gets a `" (Debug)"` title suffix and a visible border
(`MainWindow.xaml.cs`, `#if DEBUG`).

**When checking for or stopping MultiClod processes, always filter by full path (or the window
title), never by bare process name** - e.g. `Get-Process | Where-Object { $_.Path -like
'*bin\x64\Debug*' }`, not `Get-Process -Name MultiClod.App` or `Stop-Process -Name MultiClod.App`.
Since both configurations share the same process name, a name-only match can hit a running Release
install (e.g. the one hosting the very Claude Code session doing the checking) at the same time as
a Debug build being tested - killing it out from under yourself.

## Deploy shortcuts

Typing `deploy feat` or `deploy fix` triggers a full release: version bump, tag, and push - the tag
push itself triggers `.github/workflows/release.yml`, which packages and publishes the GitHub
Release. `feat` bumps the minor version; `fix` bumps the patch version. No version is stored in any
project file - git tags (`vX.Y.Z`) on `main` are the single source of truth for the current
released version (see `README.md` for how the release pipeline works).

When triggered, do all of this without stopping to ask at each step - that's the point of the
shortcut - but always state the computed version bump before doing anything irreversible.

**Never run `git push` for anything other than the release tag itself as part of a deploy** (no
pushing commits, no force-push, etc.) - but the tag is the one exception: creating and pushing
`vX.Y.Z` is part of the shortcut, not left for the user to do manually.

1. Ignore git/commit state entirely - do not check whether the working tree is clean, staged,
   committed, or pushed, and do not stop to ask about it. Deploy packages and publishes whatever is
   currently on disk, regardless of commit status.
2. Find the latest release tag: `git describe --tags --abbrev=0 --match "v*"`.
3. Compute the next version:
   - `deploy feat` -> bump minor, reset patch to 0 (e.g. `v1.0.0` -> `v1.1.0`)
   - `deploy fix`  -> bump patch (e.g. `v1.0.0` -> `v1.0.1`)
4. Tell the user the old -> new version before doing anything irreversible.
5. Tag and push: `git tag vX.Y.Z` then `git push origin vX.Y.Z`. Pushing the tag is what triggers
   `.github/workflows/release.yml` - there's no local publish step anymore, so this is the
   irreversible action, not something that follows a successful build.
6. Watch the triggered workflow run to completion, e.g.:
   ```powershell
   gh run watch $(gh run list --workflow=release.yml --limit 1 --json databaseId --jq '.[0].databaseId')
   ```
7. Report success or failure back to the user, including a link to the run and, on success, the
   new GitHub Release.
