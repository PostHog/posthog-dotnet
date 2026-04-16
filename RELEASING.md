# Releasing

Releases are driven by PR labels. When a PR with the right labels is merged to `main`, a GitHub Actions workflow handles version bumping, tagging, creating a GitHub Release (with auto-generated notes), and publishing to NuGet.

## Release Process

1. Add the `release` label and exactly one of `bump-patch`, `bump-minor`, or `bump-major` to your PR
2. Merge the PR to `main`
3. Approve the release in the GitHub Environment gate (the workflow pauses for maintainer approval)
4. The workflow bumps the version in `Directory.Build.props`, commits to `main`, creates a git tag, and creates a GitHub Release
5. The GitHub Release triggers the [`main.yaml`](.github/workflows/main.yaml) workflow, which builds and publishes the packages to NuGet
