# PostHog .NET SDK

This is a client SDK for the PostHog API written in C#. This is the core implementation of PostHog.

> [!WARNING]  
> This package is currently in a pre-release stage. We're making it available publicly to solicit
> feedback. While we always strive to maintain a high level of quality, use this package at your own
> risk. There *will* be many breaking changes until we reach a stable release.

## Goals

The goal of this package is to be usable in multiple .NET environments. At this moment, we are far short of that goal. We only support ASP.NET Core via [PostHog.AspNetCore](../PostHog.AspNetCore/README.md).

## Docs

More detailed docs for using this library can be found at [PostHog Docs for the .NET Client SDK](https://posthog.com/docs/libraries/dotnet).

## Usage

To use this package, you need to create an instance of `PostHogClient` and call the appropriate methods. Here's an example:

```csharp
using PostHog;

var client = new PostHogClient(new PostHogOptions { ProjectApiKey = "YOUR_PROJECT_API_KEY" });
client.Capture("user-123", "Test Event");
```
