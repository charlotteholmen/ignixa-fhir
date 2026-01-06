/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * LazyResolvedElement provides lightweight parsing for resolve() function.
 * Parses reference strings ("Patient/123") for type checks without DB lookup.
 * Triggers optional ElementResolver delegate only when accessing properties beyond id/resourceType.
 */

using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation.Functions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Lazy implementation of IElement for resolve() function.
/// Parses reference strings for lightweight type checks, triggers full resolution only when needed.
/// </summary>
internal class LazyResolvedElement : IElement
{
    private static readonly Regex ReferenceRegex = new(
        @"(?:(?<baseUrl>.+)/)?(?<resourceType>[A-Z][a-zA-Z]+)/(?<resourceId>[A-Za-z0-9\-\.]{1,64})(?:/_history/[A-Za-z0-9\-\.]{1,64})?",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture);

    private readonly string _reference;
    private readonly Func<string, IElement?>? _fullResolver;
    private IElement? _fullElement;
    private bool _resolverInvoked;
    private string? _resourceType;
    private string? _resourceId;
    private bool _parsed;

    public LazyResolvedElement(string reference, Func<string, IElement?>? fullResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        _reference = reference;
        _fullResolver = fullResolver;
    }

    public string Name => string.Empty;

    public string InstanceType
    {
        get
        {
            EnsureParsed();
            return _resourceType ?? "Resource";
        }
    }

    public object? Value => null;

    public string Location => _reference;

    public IType? Type => null;

    public IReadOnlyList<IElement> Children(string? name = null)
    {
        if (name == "id")
        {
            EnsureParsed();
            if (_resourceId != null)
            {
                return [new PrimitiveElement(_resourceId, "id")];
            }
            return [];
        }

        if (name == "resourceType")
        {
            EnsureParsed();
            if (_resourceType != null)
            {
                return [new PrimitiveElement(_resourceType, "code")];
            }
            return [];
        }

        if (name == null)
        {
            var fullElement = GetFullElement();
            return fullElement?.Children(null) ?? [];
        }

        var element = GetFullElement();
        return element?.Children(name) ?? [];
    }

    public T? Meta<T>() where T : class => null;

    private void EnsureParsed()
    {
        if (_parsed) return;

        var match = ReferenceRegex.Match(_reference);
        if (match.Success)
        {
            _resourceType = match.Groups["resourceType"].Value;
            _resourceId = match.Groups["resourceId"].Value;
        }

        _parsed = true;
    }

    private IElement? GetFullElement()
    {
        if (_resolverInvoked)
        {
            return _fullElement;
        }

        _resolverInvoked = true;

        if (_fullResolver != null)
        {
            _fullElement = _fullResolver(_reference);
        }

        return _fullElement;
    }

    private class PrimitiveElement(object value, string instanceType) : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => instanceType;
        public object Value => value;
        public string Location => string.Empty;
        public IType? Type => null;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
