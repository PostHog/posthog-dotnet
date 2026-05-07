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

## Platform

The core [PostHog](./src/PostHog/README.md) package targets `netstandard2.1` and `net8.0` for broad compatibility. The [PostHog.AspNetCore](src/PostHog.AspNetCore/README.md) package targets `net8.0`. The [PostHog.AI](src/PostHog.AI/README.md) package targets `netstandard2.1` and `net8.0` for broad compatibility.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build, sample, and test instructions.

## Docs

More detailed docs for using this library can be found at [PostHog Docs for the .NET Client SDK](https://posthog.com/docs/libraries/dotnet).

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
