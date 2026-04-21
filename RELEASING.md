# Releasing

Releases are usually driven by PR labels. When a PR with the right labels is merged to `main`, a GitHub Actions workflow handles version bumping, tagging, creating a GitHub Release (with auto-generated notes), and publishing to NuGet. You can also trigger the `Release` workflow manually from GitHub Actions and choose the bump type.

## Release Process

1. Either:
   - add the `release` label and exactly one of `bump-patch`, `bump-minor`, or `bump-major` to your PR, then merge it to `main`, or
   - open the `Release` workflow in GitHub Actions, click **Run workflow**, and choose `patch`, `minor`, or `major`
2. Approve the release in the GitHub Environment gate (the workflow pauses for maintainer approval)
3. The workflow bumps the version in `Directory.Build.props`, commits to `main`, creates a git tag, and creates a GitHub Release
4. The GitHub Release triggers the [`main.yaml`](.github/workflows/main.yaml) workflow, which builds and publishes the packages to NuGet
