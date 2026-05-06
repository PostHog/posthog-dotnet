# PostHog .NET SDK

This is a client SDK for the PostHog API written in C#. This is the core implementation of PostHog.

## Goals

The goal of this package is to be usable in multiple .NET environments. At this moment, we are far short of that goal. We only support ASP.NET Core via [PostHog.AspNetCore](../PostHog.AspNetCore/README.md).

## Docs

More detailed docs for using this library can be found at [PostHog Docs for the .NET Client SDK](https://posthog.com/docs/libraries/dotnet).

## Usage

To use this package, create an instance of `PostHogClient` and call the appropriate methods. Here's an example:

```csharp
using PostHog;

var client = new PostHogClient(new PostHogOptions { ProjectToken = "YOUR_PROJECT_TOKEN" });
client.Capture("user-123", "Test Event");
```

For console apps, scripts, or other places where passing a client instance around is inconvenient, you can configure a process-wide default client and use the `PostHogSdk` facade:

```csharp
using PostHog;

PostHogSdk.Init(new PostHogOptions { ProjectToken = "YOUR_PROJECT_TOKEN" });
PostHogSdk.Capture("user-123", "Test Event");

await PostHogSdk.ShutdownAsync();
```

You can also assign an existing client:

```csharp
using PostHog;

var client = new PostHogClient(new PostHogOptions { ProjectToken = "YOUR_PROJECT_TOKEN" });
PostHogSdk.DefaultClient = client;

PostHogSdk.Capture("user-123", "Test Event");
```

If no default client is configured, `PostHogSdk` methods are no-ops and emit a warning once.
