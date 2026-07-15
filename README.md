# Publishing a release

Releases are built and published automatically by `.github/workflows/release.yml`. Pushing a
`vX.Y.Z` tag to `main` triggers the workflow, which publishes the app, packs it with `vpk`, and
uploads the packages to a GitHub Release on this repo - the running app checks that same repo
(via Velopack's `GithubSource`) for updates.
