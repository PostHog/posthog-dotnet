# PostHog ASP.NET Core SDK

Please see the main [PostHog docs](https://posthog.com/docs).

SDK usage examples and code snippets live in the official documentation so they stay up to date.

## Documentation

- [.NET library docs](https://posthog.com/docs/libraries/dotnet)

## Request span context

Disabled PostHog clients add no person or session tags to activities. The client uses the disabled state set when it was created.

`UsePostHogRequestContext()` adds `sessionId` and `posthogDistinctId` tags to the active request `Activity`, including an activity that started before the middleware. It preserves tags that the application already set. Missing IDs stay absent. These headers are client-controlled analytics context. Do not use them for authentication or authorization.

To also enrich child activities, register PostHog with the host builder's `AddPostHog()`, or call `UseAspNetCore()` when configuring PostHog services. The middleware activates a listener that the service provider disposes when the host stops. Child activities receive the request context when they start, so their tags remain available if they stop after the request ends. The listener does not change application sampling or create activities. OpenTelemetry is optional; the application keeps its existing provider, processors, and exporters.

Call the middleware before downstream code creates child activities. Configure forwarded headers before it when using a proxy. For stronger application identity, set `posthogDistinctId` explicitly when creating the activity, or set it with `Activity.SetTag`. Header values never replace explicit tags. New activities started after the request scope ends do not receive the ended scope's IDs. Background jobs need their own explicit context propagation.

This integration targets .NET 8 or later. The core SDK does not provide a native PostHog span API or a trace exporter. See the [.NET setup guide](https://posthog.com/docs/libraries/dotnet) for installation and request middleware setup.
