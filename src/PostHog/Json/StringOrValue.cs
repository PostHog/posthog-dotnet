using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostHog.Json;

/// <summary>
/// A type that can be either a string or a value of type <typeparamref name="T"/>.
/// When deserializing from JSON, this type can be used to handle cases where a
/// field can be either a string or a value.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
[JsonConverter(typeof(StringOrValueConverter))]
public readonly struct StringOrValue<T> : IStringOrObject, IEquatable<T>, IEquatable<StringOrValue<T>>
{
    bool IsDefault => !IsValue && !IsString;

    /// <summary>
    /// Initializes a new instance of the <see cref="StringOrValue{T}"/> struct with a value of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="value">The value of type <typeparamref name="T"/>.</param>
    public StringOrValue(T value)
    {
        Value = value;
        IsValue = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StringOrValue{T}"/> struct with a string value.
    /// </summary>
    /// <param name="stringValue">The string value.</param>
    public StringOrValue(string stringValue)
    {
        StringValue = stringValue;
        IsString = true;
    }

    /// <summary>
    /// Gets the string value.
    /// </summary>
    public string? StringValue { get; }

    /// <summary>
    /// Gets the value of type <typeparamref name="T"/>.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Gets the object value.
    /// </summary>
    object? IStringOrObject.ObjectValue => Value;

    /// <summary>
    /// Gets a value indicating whether this instance is a string.
    /// </summary>
    [MemberNotNullWhen(true, nameof(StringValue))]
    public bool IsString { get; }

    /// <summary>
    /// Gets a value indicating whether this instance is a value.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsValue { get; }

    /// <summary>
    /// Implicitly converts a string to a <see cref="StringOrValue{T}"/>.
    /// </summary>
    /// <param name="stringValue">The string value.</param>
    /// <returns>A <see cref="StringOrValue{T}"/> containing <paramref name="stringValue"/>.</returns>
    public static implicit operator StringOrValue<T>(string stringValue) => new(stringValue);

    /// <summary>
    /// Implicitly converts a value of type <typeparamref name="T"/> to a <see cref="StringOrValue{T}"/>.
    /// </summary>
    /// <param name="value">The value of type <typeparamref name="T"/>.</param>
    /// <returns>A <see cref="StringOrValue{T}"/> containing <paramref name="value"/>.</returns>
    public static implicit operator StringOrValue<T>(T value) => new(value);

    /// <summary>
    /// Creates a new instance of <see cref="StringOrValue{T}"/> from a string value.
    /// </summary>
    /// <remarks>
    /// This is here to satisfy CA2225: Operator overloads have named alternates.
    /// </remarks>
    /// <returns>This <see cref="StringOrValue{T}"/> instance.</returns>
    public StringOrValue<T> ToStringOrValue() => this;

    /// <summary>
    /// Returns the string value, the value converted to a string, or an empty string when neither is set.
    /// </summary>
    /// <returns>A string representation of this instance.</returns>
    public override string ToString() => (IsString ? StringValue : Value?.ToString()) ?? string.Empty;

    /// <summary>
    /// Determines whether this instance contains the specified value.
    /// </summary>
    /// <param name="obj">The value to compare with this instance.</param>
    /// <returns><c>true</c> if this instance contains an equal value; otherwise <c>false</c>.</returns>
    public bool Equals(T? obj) => IsValue && obj is not null && EqualityComparer<T>.Default.Equals(Value, obj);

    /// <summary>
    /// Determines whether this instance is equal to another <see cref="StringOrValue{T}"/>.
    /// </summary>
    /// <param name="other">The other instance to compare with this instance.</param>
    /// <returns><c>true</c> if both instances represent the same value or string; otherwise <c>false</c>.</returns>
    public bool Equals(StringOrValue<T> other)
        => (IsDefault && other.IsDefault)
           || other.IsValue
           && IsValue
           && EqualityComparer<T>.Default.Equals(Value, other.Value)
           || other.IsString && IsString && StringComparer.Ordinal.Equals(StringValue, other.StringValue);

    /// <summary>
    /// Determines whether the specified object is equal to this instance.
    /// </summary>
    /// <param name="obj">The object to compare with this instance.</param>
    /// <returns><c>true</c> if the object is equal to this instance; otherwise <c>false</c>.</returns>
    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is StringOrValue<T> value && Equals(value.Value);

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for this instance.</returns>
    public override int GetHashCode() => IsValue
        ? Value?.GetHashCode() ?? 0
        : StringValue?.GetHashCode(StringComparison.Ordinal) ?? 0;

    /// <summary>
    /// Determines whether two <see cref="StringOrValue{T}"/> instances are equal.
    /// </summary>
    /// <param name="left">The left instance to compare.</param>
    /// <param name="right">The right instance to compare.</param>
    /// <returns><c>true</c> if the instances are equal; otherwise <c>false</c>.</returns>
    public static bool operator ==(StringOrValue<T> left, StringOrValue<T> right)
        => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="StringOrValue{T}"/> instances are not equal.
    /// </summary>
    /// <param name="left">The left instance to compare.</param>
    /// <param name="right">The right instance to compare.</param>
    /// <returns><c>true</c> if the instances are not equal; otherwise <c>false</c>.</returns>
    public static bool operator !=(StringOrValue<T> left, StringOrValue<T> right)
        => !left.Equals(right);

    /// <summary>
    /// Determines whether a nullable <see cref="StringOrValue{T}"/> contains the specified value.
    /// </summary>
    /// <param name="left">The instance to compare.</param>
    /// <param name="right">The value to compare with the instance.</param>
    /// <returns><c>true</c> if the instance contains the specified value; otherwise <c>false</c>.</returns>
    public static bool operator ==(StringOrValue<T>? left, T right)
        => left is not null && left.Equals(right);

    /// <summary>
    /// Determines whether a <see cref="StringOrValue{T}"/> contains the specified value.
    /// </summary>
    /// <param name="left">The instance to compare.</param>
    /// <param name="right">The value to compare with the instance.</param>
    /// <returns><c>true</c> if the instance contains the specified value; otherwise <c>false</c>.</returns>
    public static bool operator ==(StringOrValue<T> left, T right)
        => left.Equals(right);

    /// <summary>
    /// Determines whether a nullable <see cref="StringOrValue{T}"/> does not contain the specified value.
    /// </summary>
    /// <param name="left">The instance to compare.</param>
    /// <param name="right">The value to compare with the instance.</param>
    /// <returns><c>true</c> if the instance does not contain the specified value; otherwise <c>false</c>.</returns>
    public static bool operator !=(StringOrValue<T>? left, T right)
        => left is null || !left.Equals(right);

    /// <summary>
    /// Determines whether a <see cref="StringOrValue{T}"/> does not contain the specified value.
    /// </summary>
    /// <param name="left">The instance to compare.</param>
    /// <param name="right">The value to compare with the instance.</param>
    /// <returns><c>true</c> if the instance does not contain the specified value; otherwise <c>false</c>.</returns>
    public static bool operator !=(StringOrValue<T> left, T right)
        => !left.Equals(right);
}

/// <summary>
/// Internal interface for <see cref="StringOrValue{T}"/>.
/// </summary>
/// <remarks>
/// This is here to make serialization and deserialization easy.
/// </remarks>
[JsonConverter(typeof(StringOrValueConverter))]
internal interface IStringOrObject
{
    bool IsString { get; }

    bool IsValue { get; }

    string? StringValue { get; }

    object? ObjectValue { get; }
}

/// <summary>
/// Json converter for <see cref="StringOrValue{T}"/>.
/// </summary>
internal class StringOrValueConverter : JsonConverter<IStringOrObject>
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
           && typeToConvert.GetGenericTypeDefinition() == typeof(StringOrValue<>);

    public override IStringOrObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var targetType = typeToConvert.GetGenericArguments()[0];

        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            return stringValue is null
                ? CreateEmptyInstance(targetType)
                : CreateStringInstance(targetType, stringValue);
        }

        var value = JsonSerializer.Deserialize(ref reader, targetType, options);

        return value is null
            ? CreateEmptyInstance(targetType)
            : CreateValueInstance(targetType, value);
    }

    static ConstructorInfo GetEmptyConstructor(Type targetType)
    {
        return typeof(StringOrValue<>)
                   .MakeGenericType(targetType).
                   GetConstructor([])
               ?? throw new InvalidOperationException($"No constructor found for StringOrValue<{targetType.Name}>.");
    }

    static ConstructorInfo GetConstructor(Type targetType, Type argumentType)
    {
        return typeof(StringOrValue<>)
            .MakeGenericType(targetType).
            GetConstructor([argumentType])
            ?? throw new InvalidOperationException($"No constructor found for StringOrValue<{targetType.Name}>.");
    }

    static IStringOrObject CreateEmptyInstance(Type targetType)
    {
        var ctor = GetEmptyConstructor(targetType);
        return (IStringOrObject)ctor.Invoke([]);
    }

    static IStringOrObject CreateStringInstance(Type targetType, string value)
    {
        var ctor = GetConstructor(targetType, typeof(string));
        return (IStringOrObject)ctor.Invoke([value]);
    }

    static IStringOrObject CreateValueInstance(Type targetType, object value)
    {
        var ctor = GetConstructor(targetType, targetType);
        return (IStringOrObject)ctor.Invoke([value]);
    }

    public override void Write(Utf8JsonWriter writer, IStringOrObject value, JsonSerializerOptions options)
    {
        if (value.IsString)
        {
            writer.WriteStringValue(value.StringValue);
        }
        else if (value.IsValue)
        {
            JsonSerializer.Serialize(writer, value.ObjectValue, options);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
