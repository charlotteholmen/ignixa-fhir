// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Text;
using Ignixa.Abstractions;

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

public static class GraphQlNamingHelper
{
    private static readonly FrozenSet<string> _reservedKeywords = FrozenSet.Create(StringComparer.Ordinal,
        "for", "if", "else", "return", "true", "false", "null",
        "query", "mutation", "subscription", "fragment", "on",
        "type", "interface", "union", "enum", "input", "scalar",
        "schema", "directive", "extend", "implements", "repeatable");

    public static string ToPascalCase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var dotIndex = name.LastIndexOf('.');
        var segment = dotIndex >= 0 ? name[(dotIndex + 1)..] : name;

        if (segment.Length == 0)
            return segment;

        return char.ToUpperInvariant(segment[0]) + segment[1..];
    }

    public static string ToCamelCase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
            return name;

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static string ToBackboneTypeName(string resourceType, string elementPath)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(elementPath);

        var segments = elementPath.Split('.');
        var sb = new StringBuilder(resourceType);

        foreach (var segment in segments)
        {
            if (!string.Equals(segment, resourceType, StringComparison.Ordinal) && segment.Length > 0)
                sb.Append(char.ToUpperInvariant(segment[0])).Append(segment[1..]);
        }

        return sb.ToString();
    }

    public static string ToUnionTypeName(string elementName)
    {
        ArgumentNullException.ThrowIfNull(elementName);

        var pascal = ToPascalCase(elementName);
        return pascal.EndsWith('X') ? pascal : pascal + "X";
    }

    public static string ToConnectionTypeName(string resourceType)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        return resourceType + "Connection";
    }

    public static string SanitizeFieldName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
            return "_";

        var sb = new StringBuilder(name.Length);

        var first = name[0];
        sb.Append(char.IsLetter(first) || first == '_' ? first : '_');

        foreach (var c in name.AsSpan(1))
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

        var result = sb.ToString();

        return _reservedKeywords.Contains(result) ? "_" + result : result;
    }

    public static string GetSchemaName(FhirVersion version)
    {
#pragma warning disable CA1308 // Schema names use lowercase by GraphQL convention, not security normalization
        return $"fhir-{version.ToString().ToLowerInvariant()}";
#pragma warning restore CA1308
    }
}
