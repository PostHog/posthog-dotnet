namespace PostHog.Json;

/// <summary>
/// Custom JsonStringEnumMemberName attribute that matches the .NET 9+ built-in attribute exactly.
/// This provides .NET 8 compatibility and is completely future-proof.
/// When upgrading to .NET 9+, this custom attribute and converter can be removed seamlessly.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class JsonStringEnumMemberNameAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the JsonStringEnumMemberNameAttribute class.
    /// </summary>
    /// <param name="name">The name to use for JSON serialization/deserialization.</param>
    public JsonStringEnumMemberNameAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Gets the name to use for JSON serialization/deserialization.
    /// </summary>
    public string Name { get; }
}
