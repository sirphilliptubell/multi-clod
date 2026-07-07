# Publishing a release

Before running the publish script, set the `MULTICLOD_DEPLOY_PATH` environment variable in your
shell (persist it with `setx` so new sessions pick it up automatically). Its value is
intentionally not documented here - it points at the network share this app is deployed to, and
that path is deliberately kept out of this repo.

To ship a release:

1. Pick a version number (there's no automated versioning in this repo - just choose the next
   sensible semver bump yourself).
2. From the repo root, run:

   ```powershell
   .\scripts\Publish-MultiClodApp.ps1 -Version 1.0.1
   ```

   This publishes the app, packs it with `vpk`, and copies the release feed to
   `MULTICLOD_DEPLOY_PATH`.
