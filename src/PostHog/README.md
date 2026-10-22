# PostHog .NET SDK

Please see the main [PostHog docs](https://posthog.com/docs).

SDK usage examples and code snippets live in the official documentation so they stay up to date.

## Event property serialization

Numeric `float` and `double` NaN and infinity values are sent as JSON `null`, including in nested objects and arrays. Finite values remain JSON numbers and literal strings are unchanged.

The SDK's float/double converters do not apply `[JsonNumberHandling]` attributes on user models or their members, including collection elements. For example, `[JsonNumberHandling(WriteAsString)]` does not turn a finite double into a JSON string. Supply a string property value or an explicit `[JsonConverter]` when custom formatting is required.

## Documentation

- [.NET library docs](https://posthog.com/docs/libraries/dotnet)
