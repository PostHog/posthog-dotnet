// Copied from https://github.com/dotnet/runtime/blob/be5a9507cd1b1a4cad383533d640046618b7e968/src/libraries/Common/src/Extensions/TypeNameHelper/TypeNameHelper.cs
// Necessary for getting the Logger Category name used for a type.

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Microsoft.Extensions.Internal;

public static class TypeNameHelper
{
    const char DefaultNestedTypeDelimiter = '+';

    static readonly Dictionary<Type, string> BuiltInTypeNames = new()
    {
        { typeof(void), "void" },
        { typeof(bool), "bool" },
        { typeof(byte), "byte" },
        { typeof(char), "char" },
        { typeof(decimal), "decimal" },
        { typeof(double), "double" },
        { typeof(float), "float" },
        { typeof(int), "int" },
        { typeof(long), "long" },
        { typeof(object), "object" },
        { typeof(sbyte), "sbyte" },
        { typeof(short), "short" },
        { typeof(string), "string" },
        { typeof(uint), "uint" },
        { typeof(ulong), "ulong" },
        { typeof(ushort), "ushort" }
    };

    [return: NotNullIfNotNull("item")]
    public static string? GetTypeDisplayName(object? item, bool fullName = true)
    {
        return item == null ? null : GetTypeDisplayName(item.GetType(), fullName);
    }

    /// <summary>
    /// Pretty print a type name.
    /// </summary>
    /// <param name="type">The <see cref="Type"/>.</param>
    /// <param name="fullName"><c>true</c> to print a fully qualified name.</param>
    /// <param name="includeGenericParameterNames"><c>true</c> to include generic parameter names.</param>
    /// <param name="includeGenericParameters"><c>true</c> to include generic parameters.</param>
    /// <param name="nestedTypeDelimiter">Character to use as a delimiter in nested type names</param>
    /// <returns>The pretty printed type name.</returns>
    public static string GetTypeDisplayName(Type type, bool fullName = true, bool includeGenericParameterNames = false, bool includeGenericParameters = true, char nestedTypeDelimiter = DefaultNestedTypeDelimiter)
    {
        var builder = new StringBuilder();
        ProcessType(builder, type, new DisplayNameOptions(fullName, includeGenericParameterNames, includeGenericParameters, nestedTypeDelimiter));
        return builder.ToString();
    }

    static void ProcessType(StringBuilder builder, Type type, in DisplayNameOptions options)
    {
        if (type.IsGenericType)
        {
            Type[] genericArguments = type.GetGenericArguments();
            ProcessGenericType(builder, type, genericArguments, genericArguments.Length, options);
        }
        else if (type.IsArray)
        {
            ProcessArrayType(builder, type, options);
        }
        else if (BuiltInTypeNames.TryGetValue(type, out string? builtInName))
        {
            builder.Append(builtInName);
        }
        else if (type.IsGenericParameter)
        {
            if (options.IncludeGenericParameterNames)
            {
                builder.Append(type.Name);
            }
        }
        else
        {
            string name = options.FullName ? type.FullName! : type.Name;
            builder.Append(name);

            if (options.NestedTypeDelimiter != DefaultNestedTypeDelimiter)
            {
                builder.Replace(DefaultNestedTypeDelimiter, options.NestedTypeDelimiter, builder.Length - name.Length, name.Length);
            }
        }
    }

    static void ProcessArrayType(StringBuilder builder, Type type, in DisplayNameOptions options)
    {
        Type innerType = type;
        while (innerType.IsArray)
        {
            innerType = innerType.GetElementType()!;
        }

        ProcessType(builder, innerType, options);

        while (type.IsArray)
        {
            builder.Append('[');
            builder.Append(',', type.GetArrayRank() - 1);
            builder.Append(']');
            type = type.GetElementType()!;
        }
    }

    static void ProcessGenericType(StringBuilder builder, Type type, Type[] genericArguments, int length, in DisplayNameOptions options)
    {
        int offset = 0;
        if (type.IsNested)
        {
            offset = type.DeclaringType!.GetGenericArguments().Length;
        }

        if (options.FullName)
        {
            if (type.IsNested)
            {
                ProcessGenericType(builder, type.DeclaringType!, genericArguments, offset, options);
                builder.Append(options.NestedTypeDelimiter);
            }
            else if (!string.IsNullOrEmpty(type.Namespace))
            {
                builder.Append(type.Namespace);
                builder.Append('.');
            }
        }

        int genericPartIndex = type.Name.IndexOf('`', StringComparison.Ordinal);
        if (genericPartIndex <= 0)
        {
            builder.Append(type.Name);
            return;
        }

        builder.Append(type.Name, 0, genericPartIndex);

        if (options.IncludeGenericParameters)
        {
            builder.Append('<');
            for (int i = offset; i < length; i++)
            {
                ProcessType(builder, genericArguments[i], options);
                if (i + 1 == length)
                {
                    continue;
                }

                builder.Append(',');
                if (options.IncludeGenericParameterNames || !genericArguments[i + 1].IsGenericParameter)
                {
                    builder.Append(' ');
                }
            }
            builder.Append('>');
        }
    }

    readonly struct DisplayNameOptions
    {
        public DisplayNameOptions(bool fullName, bool includeGenericParameterNames, bool includeGenericParameters, char nestedTypeDelimiter)
        {
            FullName = fullName;
            IncludeGenericParameters = includeGenericParameters;
            IncludeGenericParameterNames = includeGenericParameterNames;
            NestedTypeDelimiter = nestedTypeDelimiter;
        }

        public bool FullName { get; }

        public bool IncludeGenericParameters { get; }

        public bool IncludeGenericParameterNames { get; }

        public char NestedTypeDelimiter { get; }
    }
}
