# PostHog DotNet Client SDK ![Build status](https://github.com/PostHog/posthog-dotnet/actions/workflows/main.yaml/badge.svg?branch=main)

This repository contains a set of packages for interacting with the PostHog API in .NET applications. 
This README is for those who wish to contribute to these packages.

For documentation on the specific packages, see the README files in the respective package directories.

## Packages

| Package | Version | Description
|---------|---------| -----------
| [PostHog.AspNetCore](src/PostHog.AspNetCore/README.md) | [![NuGet version (PostHog.AspNetCore)](https://img.shields.io/nuget/v/PostHog.AspNetCore.svg?style=flat-square)](https://www.nuget.org/packages/PostHog.AspNetCore/) | For use in ASP.NET Core projects.
| [PostHog](src/PostHog/README.md) | [![NuGet version (PostHog)](https://img.shields.io/nuget/v/PostHog.svg?style=flat-square)](https://www.nuget.org/packages/PostHog/)                                  | The core library. Over time, this will support client environments such as Unit, Xamarin, etc.
| [PostHog.AI](src/PostHog.AI/README.md) | [![NuGet version (PostHog.AI)](https://img.shields.io/nuget/v/PostHog.AI.svg?style=flat-square)](https://www.nuget.org/packages/PostHog.AI/) | AI Observability for OpenAI and other LLM providers.

> [!WARNING]  
> These packages are currently in a pre-release stage. We're making them available publicly to solicit 
> feedback. While we always strive to maintain a high level of quality, use these packages at your own 
> risk. There *will* be many breaking changes until we reach a stable release.

## Platform

The core [PostHog](./src/PostHog/README.md) package targets `netstandard2.1` and `net8.0` for broad compatibility. The [PostHog.AspNetCore](src/PostHog.AspNetCore/README.md) package targets `net8.0`. The [PostHog.AI](src/PostHog.AI/README.md) package targets `netstandard2.1` and `net8.0` for broad compatibility.

## Building

To build the solution, run the following commands in the root of the repository:

```bash
$ dotnet restore
$ dotnet build
```

## Docs

More detailed docs for using this library can be found at [PostHog Docs for the .NET Client SDK](https://posthog.com/docs/libraries/dotnet).

## Samples

Sample projects are located in the `samples` directory.

To run the samples, you'll need to set your PostHog project API key. From the repository root you can run:

```bash
bin/user-secrets set PostHog:ProjectApiKey YOUR_API_KEY
```

The main ASP.NET Core sample app can be run with the following command:

```bash
$ bin/start
```

You can also run it from your favorite IDE or editor.

## Testing

To run the tests, run the following command in the root of the repository:

```bash
$ dotnet test
```

### Test Target Frameworks

The test projects target both `net8.0` and `netcoreapp3.1`. While .NET Core 3.1 reached end-of-life in December 2022, we continue to test against it because:

- It was the first runtime to fully support .NET Standard 2.1
- It serves as our minimum test baseline to ensure the `netstandard2.1` library works correctly on older runtimes
- It helps catch compatibility issues that might not surface on newer runtimes

This testing approach ensures broad compatibility without requiring users to install legacy runtimes in production.

## Publishing Releases

To create a release, use the `bin/release` script which automates the version bumping and release preparation process.

### Release Process

1. **Prepare the release**: Run the release script with the type of version bump you want:

   ```bash
   # For a patch release (1.0.6 -> 1.0.7)
   ./bin/release patch
   
   # For a minor release (1.0.6 -> 1.1.0)
   ./bin/release minor
   
   # For a major release (1.0.6 -> 2.0.0)
   ./bin/release major
   ```

   This script will:
   - Calculate the new version based on the current version in [`Directory.Build.props`](Directory.Build.props)
   - Create a new release branch (`release-{version}`)
   - Update the version in `Directory.Build.props`
   - Build and test the solution
   - Commit the version change and generated files
   - Create and push the git tag

2. **Create a Pull Request**: Push the release branch and create a PR to merge it into `main`:

   ```bash
   git push origin release-{version}
   ```

   Then create a pull request on GitHub to merge the release branch into `main`.

3. **Create GitHub Release**: Once the PR is merged, go to GitHub to [Draft a new Release](https://github.com/Posthog/posthog-dotnet/releases/new), select the tag that was already created, and click "Auto-generate release notes". Edit the notes as needed and publish the release.

When you create the Release, the [`main.yml`](.github/workflows/main.yml) workflow builds and publishes the package to NuGet.

> [!IMPORTANT]
> When creating a release, it's important to create and publish it in one go. If you save a draft of the release and
> then later publish it, the workflow will not run. If you find yourself in that position, you can [manually trigger the workflow run](https://github.com/PostHog/posthog-dotnet/actions/workflows/main.yaml)
> and select the tag to publish.

## Installation

For ASP.NET Core projects, install the `PostHog.AspNetCore` package:

```bash
$ dotnet add package PostHog.AspNetCore
```

And register the PostHog services in `Program.cs` (or `Startup.cs`) file by calling the `AddPostHog` extension 
method on `IHostApplicationBuilder` like so:

```csharp
using PostHog;
var builder = WebApplication.CreateBuilder(args);
builder.AddPostHog();
```

For other .NET projects, install the `PostHog` package:

```bash
$ dotnet add package PostHog
```

And if your project supports dependency injection, register the PostHog services in `Program.cs` (or `Startup.cs`) 
file by calling the `AddPostHog` extension method on `IServiceCollection`. Here's an example for a console app:

```csharp
using PostHog;
var services = new ServiceCollection();
services.AddPostHog();
var serviceProvider = services.BuildServiceProvider();
var posthog = serviceProvider.GetRequiredService<IPostHogClient>();
```

For a console app (or apps not using dependency injection), you can also use the `PostHogClient` directly, just make 
sure it's a singleton:

```csharp
using System;
using PostHog;

var posthog = new PostHogClient(Environment.GetEnvironmentVariable("PostHog__PersonalApiKey"));
```

The `AddPostHog` methods accept an optional `Action<PostHogOptions>` parameter that you can use to configure the 
client. For examples, check out the [HogTied.Web sample project](../samples/HogTied.Web/Program.cs) and the unit tests.

## Usage

Inject the `IPostHogClient` interface into your controller or page:

```csharp
posthog.Capture(userId, "user signed up", new() { ["plan"] = "pro" });
```

```csharp
client.CapturePageView(userId, Request.Path.Value ?? "Unknown");
```

### Identity

#### Identify a user

See the [Identifying users](https://posthog.com/docs/product-analytics/identify) for more information about identifying users.

Identifying a user typically happens on the front-end. For example, when an authenticated user logs in, you can call `identify` to associate the user with their previously anonymous actions.

When `identify` is called the first-time for a distinct id, PostHog will create a new user profile. If the user already exists, PostHog will update the user profile with the new data. So the typical usage of `IdentifyAsync` here will be to update the person properties that PostHog knows about your user.

```csharp
await posthog.IdentifyAsync(
    userId,
    new() 
    {
        ["email"] = "haacked@posthog.com",
        ["name"] = "Phil Haack",
        ["plan"] = "pro"
    });
```

#### Alias a user

Use the `Alias` method to associate one identity with another. This is useful when a user logs in and you want to associate their anonymous actions with their authenticated actions.

```csharp
await posthog.AliasAsync(sessionId, userId);
```

### Analytics

#### Capture an Event

Note that capturing events is designed to be fast and done in the background. You can configure how often batches are sent to the PostHog API using the `FlushAt` and `FlushInterval` settings.

```csharp
posthog.Capture(userId, "user signed up", new() { ["plan"] = "pro" });
```

#### Capture a Page View

```csharp
posthog.CapturePageView(userId, Request.Path.Value ?? "Unknown");
```

#### Capture a Screen View

```csharp
posthog.CaptureScreen(userId, "Main Screen");
```

### Feature Flags

#### Check if feature flag is enabled

Check if the `awesome-new-feature` feature flag is enabled for the user with the id `userId`.

```csharp
var enabled = await posthog.IsFeatureEnabledAsync(userId, "awesome-new-feature");
```

You can override properties of the user stored on PostHog servers for the purposes of feature flag evaluation. 
For example, suppose you offer a temporary pro-plan for the duration of the user's session. You might do this:

```csharp
if (await posthog.IsFeatureEnabledAsync(
    "pro-feature",
    "some-user-id",
    personProperties: new() { ["plan"] = "pro" }))
{
    // Access to pro feature
}
```

If you have group analytics enabled, you can also override group properties.

```csharp
if (await posthog.IsFeatureEnabledAsync(
        "large-project-feature",
        "some-user-id",
        new FeatureFlagOptions
        {
            Groups = [new Group(groupType: "project", groupKey: "project-group-key") { ["size"] = "large" }]
        }))
{
    // Access large project feature
}
```

> [!NOTE]
> Specifying `PersonProperties` and `GroupProperties` is necessary when using local evaluation of feature flags.

### Get a single Feature Flag

Some feature flags may have associated payloads.

```csharp
if (await posthog.GetFeatureFlagAsync("awesome-new-feature", "some-user-id") is { Payload: {} payload })
{
    // Do something with the payload.
    Console.WriteLine($"The payload is: {payload}");
}
```

### Get All Feature Flags

Using information on the PostHog server.

```csharp
var flags = await posthog.GetAllFeatureFlagsAsync("some-user-id");
```

Overriding the group properties for the current user.

```csharp
var flags = await posthog.GetAllFeatureFlagsAsync(
"some-user-id",
options: new AllFeatureFlagsOptions
{
    Groups =
    [
        new Group("project", "aaaa-bbbb-cccc")
        {
            ["$group_key"] = "aaaa-bbbb-cccc",
            ["size"] = "large"
        }
    ]
});
```
