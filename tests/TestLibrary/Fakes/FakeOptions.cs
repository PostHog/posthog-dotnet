using Microsoft.Extensions.Options;

/// <summary>
/// Used to fake out an IOptions{T} instance for testing.
/// </summary>
/// <param name="Value">The options value.</param>
/// <typeparam name="T">The options type.</typeparam>
public record FakeOptions<T>(T Value) : IOptions<T> where T : class, new();