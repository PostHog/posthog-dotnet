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

## Pull requests

Please follow existing conventions and include tests for your change when practical.

For release instructions, see [RELEASING.md](RELEASING.md).
