# Sharing a session via multi-clod:// deeplinks

Clicking a `multi-clod://open-session-log?url=<source>` link launches (or activates an already
running copy of) the app and imports a shared Claude Code session transcript. `<source>` (URL
percent-encoded) can be an `http://`/`https://` URL, a UNC path, or a local file path pointing at a
zip.

The zip should contain one or more top-level `<sessionId>.jsonl` transcripts, each with an optional
`<sessionId>/subagents/*.jsonl` folder alongside it. Anything else in the zip - other `.json`,
`.jsonl`, `.txt`, `.log`, `.md`, or unrecognized files - shows up in a separate "Other files" tab in
the viewer instead.

The import opens read-only in its own window, entirely separate from the app's own session tree and
`~/.claude/projects` data - nothing gets written there. Extracted files live under
`~/.multi-clod/deeplink-imports/` and are cleared on the next app launch. The download starts
immediately with no confirmation prompt, so only click links from sources you trust.

The `multi-clod://` protocol registers itself (`HKCU\Software\Classes\multi-clod`) automatically the
first time the app runs.

# Publishing a release

Releases are built and published automatically by `.github/workflows/release.yml`. Pushing a
`vX.Y.Z` tag to `main` triggers the workflow, which publishes the app, packs it with `vpk`, and
uploads the packages to a GitHub Release on this repo - the running app checks that same repo
(via Velopack's `GithubSource`) for updates.
