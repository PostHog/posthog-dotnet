# PostHog .NET SDK

This is a client SDK for the PostHog API written in C#. This is the core implementation of PostHog.

## Goals

The goal of this package is to be usable in multiple .NET environments. At this moment, we are far short of that goal. We only support ASP.NET Core via [PostHog.AspNetCore](../PostHog.AspNetCore/README.md).

## Docs

More detailed docs for using this library can be found at [PostHog Docs for the .NET Client SDK](https://posthog.com/docs/libraries/dotnet).

## Usage

To use this package, you need to create an instance of `PostHogClient` and call the appropriate methods. Here's an example:

```csharp
using PostHog;

var client = new PostHogClient(new PostHogOptions { ProjectToken = "YOUR_PROJECT_TOKEN" });
client.Capture("user-123", "Test Event");
```
