# PostHog.AspNetCore

This is a client SDK for the PostHog API written in C#. This package depends on the PostHog package and provides 
additional functionality for ASP.NET Core projects.

> [!WARNING]  
> This package is currently in a pre-release stage. We're making it available publicly to solicit
> feedback. While we always strive to maintain a high level of quality, use this package at your own
> risk. There *will* be many breaking changes until we reach a stable release.

## Installation

Use the `dotnet` CLI to add the package to your project:

```bash
$ dotnet add package PostHog.AspNetCore
```

## Configuration

Register your PostHog instance in `Program.cs` (or `Startup.cs` depending on how you roll):

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddPostHog();
```

Set your project API key using user secrets:

```bash
$ dotnet user-secrets set PostHog:ProjectApiKey YOUR_API_KEY
```

In most cases, that's all you need to configure!

If you're using the EU hosted instance of PostHog or a self-hosted instance, you can configure the `HostUrl` setting in 
`appsettings.json`:

```json
{
  ...
  "PostHog": {
    "HostUrl": "https://eu.i.posthog.com"
  }
}
```

There are some more settings you can configure if you want to tweak the behavior of the client, but the defaults 
should work in most cases.

The available options are:

| Option          | Description                                                       | Default                  |
|-----------------|-------------------------------------------------------------------|--------------------------|
| `HostUrl`       | The URL of the PostHog instance.                                  | https://us.i.posthog.com |
| `MaxBatchSize`  | The maximum number of events to send in a single batch.           | `100`                    |
| `MaxQueueSize`  | The maximum number of events to store in the queue at any time.   | `1000`                   |
| `FlushAt`       | The number of events to enqueue before sending events to PostHog. | `20`                     |
| `FlushInterval` | The interval in milliseconds between periodic flushes.            | `30` seconds             |

> [!NOTE]
> The client will attempt to send events to PostHog in the background. It sends it every `FlushInterval` or when 
> `FlushAt` events have been enqueued. However, if the network is down or if there's a spike in events, the queue 
> could grow without restriction. The `MaxQueueSize` setting is there to prevent the queue from growing too large. 
> When that number is reached, the client will start dropping older events. `MaxBatchSize` ensures that the `/batch` 
> request doesn't get too large.

## Docs

More detailed docs for using this library can be found at [PostHog Docs for the .NET Client SDK](https://posthog.com/docs/libraries/dotnet).

## Usage

Inject the `IPostHogClient` interface into your controller or page:

```csharp
public class HomeController(IPostHogClient posthog) : Controller
{
    public IActionResult SignUpComplete()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        posthog.Capture(userId, "user signed up", new() { ["plan"] = "pro" });
        return View();
    }
}
```

```csharp
public class IndexModel(IPostHogClient posthog) : PageModel
{
    public void OnGet()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        posthog.CapturePageView(userId, Request.Path.Value ?? "Unknown");
    }
}
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
var sessionId = Request.Cookies["session_id"]; // Used for anonymous actions.
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value; // Now we know who they are.
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

## Feature Management

`PostHog.AspNetCore` supports .NET Feature Management. This allows you to use the &lt;feature /&gt; tag helper and 
the `FeatureGateAttribute` in your ASP.NET Core applications to gate access to certain features using PostHog 
feature flags.

### Setup

To use feature flags with the .NET Feature Management library, you'll need to implement the 
`IPostHogFeatureFlagContextProvider` interface. The quickest way to do that is to inherit from the 
`PostHogFeatureFlagContextProvider` class and override the `GetDistinctId` and `GetFeatureFlagOptionsAsync` methods.

```csharp
public class MyFeatureFlagContextProvider(IHttpContextAccessor httpContextAccessor)
    : PostHogFeatureFlagContextProvider
{
    protected override string? GetDistinctId() => httpContextAccessor.HttpContext?.User.Identity?.Name;
    
    protected override ValueTask<FeatureFlagOptions> GetFeatureFlagOptionsAsync()
    {
        // In a real app, you might get this information from a database or other source for the current user.
        return ValueTask.FromResult(
            new FeatureFlagOptions
            {
                PersonProperties = new Dictionary<string, object?>
                {
                    ["email"] = "some-test@example.com"
                },
                OnlyEvaluateLocally = true
            });
    }
}
```

Then, register your implementation in `Program.cs` (or `Startup.cs`):

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddPostHog(options => {
    options.UseFeatureManagement<MyFeatureFlagContextProvider>();
});
```

### Usage

You can now use `feature` tag helpers in your Razor views:

```html
<feature name="awesome-new-feature">
    <p>This is the new feature!</p>
</feature>
<feature name="awesome-new-feature" negate="true">
    <p>Sorry, no awesome new feature for you.</p>
</feature>
```

Multivariate feature flags are also supported:

```html
<feature name="awesome-new-feature" value="variant-a">
    <p>This is the new feature variant A!</p>
</feature>
<feature name="awesome-new-feature" value="variant-b">
    <p>This is the new feature variant B!</p>
</feature>
```

You can also use the `FeatureGateAttribute` to gate access to controllers or actions:

```csharp
[FeatureGate("awesome-new-feature")]
public class NewFeatureController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
```
