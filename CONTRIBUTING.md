# Contributing

Thanks for your interest in improving the PostHog .NET SDK.

## Building

From the repository root, use the same restore, formatting, build, and test flow that CI runs:

```bash
dotnet restore --locked-mode
bin/fmt --check
dotnet build --configuration Release --no-restore --nologo
dotnet test --configuration Release --no-build --nologo
```

## Running samples

Sample projects live in the `samples` directory.

To run the samples, set your PostHog project token from the repository root:

```bash
bin/user-secrets set PostHog:ProjectToken YOUR_PROJECT_TOKEN
```

The main ASP.NET Core sample app can then be started with:

```bash
bin/start
```

You can also run the samples from your preferred IDE or editor.

## Testing

The test projects target both `net8.0` and `netcoreapp3.1`. While .NET Core 3.1 reached end-of-life in December 2022, we continue to test against it because:

- It was the first runtime to fully support .NET Standard 2.1
- It serves as our minimum test baseline to ensure the `netstandard2.1` library works correctly on older runtimes
- It helps catch compatibility issues that might not surface on newer runtimes

This testing approach ensures broad compatibility without requiring users to install legacy runtimes in production.

## Public API changes

Public API is hard to change once it ships, so agree on it before writing the implementation. Our [SDK guidelines](https://posthog.com/handbook/engineering/sdks/guidelines) explain how we design it.

This section is for external contributors. PostHog Client Libraries maintainers agree on API shape in the PR itself, so they don't need a separate issue.

- **Before you start:** if you need something the SDK doesn't support and it would add or change a public option, method, or type, open an issue describing your use case. Wait for a maintainer to agree on the API shape there before you implement it. Context is more useful to us than code at this stage.
- **Already have a PR open?** Don't stop or rewrite it. Call out the public API change at the top of the PR description, and link or open an issue so we can discuss the shape there.
- Check first whether an existing option or hook, such as `BeforeSend`, already covers the use case. We avoid offering two ways to do the same thing.
- If a reviewer suggests a different API on your PR, confirm it with them before re-implementing. Treat it as a question, not an instruction.

PublicApiAnalyzers tracks each project's public API in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`, and the build fails when they're out of date. A diff in those files means your change touches public API.

## Pull requests

Please follow existing conventions and include tests for your change when practical.

For release instructions, see [RELEASING.md](RELEASING.md).
