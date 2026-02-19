// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;

namespace Ignixa.Validation.Schema;

/// <summary>
/// Public implementation of <see cref="ITypeExtended"/> constructed from parsed StructureDefinition data.
/// Used by <see cref="StructureDefinitionTypeBuilder"/> to build an IType tree
/// that <c>StructureDefinitionSchemaBuilder.BuildSchema()</c> can traverse.
/// </summary>
public sealed class StructureDefinitionTypeDefinition : ITypeExtended
{
    private readonly List<IType> _children = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StructureDefinitionTypeDefinition"/> class.
    /// </summary>
    /// <param name="info">Strongly-typed type metadata.</param>
    /// <param name="isCollection">Whether the element is a collection (max cardinality > 1).</param>
    /// <param name="isRequired">Whether the element is required (min cardinality > 0).</param>
    /// <param name="inSummary">Whether the element is included in summary responses.</param>
    /// <param name="order">Element serialization order.</param>
    /// <param name="min">Minimum cardinality.</param>
    /// <param name="max">Maximum cardinality string ("1", "*", etc.).</param>
    /// <param name="constraints">FHIRPath constraint invariants.</param>
    /// <param name="binding">Terminology binding metadata.</param>
    /// <param name="fixedValue">Fixed value constraint.</param>
    /// <param name="patternValue">Pattern value constraint.</param>
    /// <param name="types">Type references from ElementDefinition.type.</param>
    /// <param name="defaultTypeName">Default type name for logical models.</param>
    /// <param name="referenceTargets">Target resource types for Reference elements.</param>
    /// <param name="contentReference">Content reference path for recursive structures.</param>
    public StructureDefinitionTypeDefinition(
        TypeInfo info,
        bool isCollection,
        bool isRequired,
        bool inSummary,
        int order,
        int min,
        string max,
        IReadOnlyList<IConstraint>? constraints = null,
        IBinding? binding = null,
        object? fixedValue = null,
        object? patternValue = null,
        IReadOnlyList<ITypeReference>? types = null,
        string? defaultTypeName = null,
        IReadOnlyList<string>? referenceTargets = null,
        string? contentReference = null)
    {
        Info = info;
        IsCollection = isCollection;
        IsRequired = isRequired;
        InSummary = inSummary;
        Order = order;
        Min = min;
        Max = max;
        Constraints = constraints ?? Array.Empty<IConstraint>();
        Binding = binding;
        FixedValue = fixedValue;
        PatternValue = patternValue;
        Types = types ?? Array.Empty<ITypeReference>();
        DefaultTypeName = defaultTypeName;
        ReferenceTargets = referenceTargets ?? Array.Empty<string>();
        ContentReference = contentReference;
    }

    /// <inheritdoc/>
    public TypeInfo Info { get; }

    /// <inheritdoc/>
    public bool IsCollection { get; }

    /// <inheritdoc/>
    public bool IsRequired { get; }

    /// <inheritdoc/>
    public bool InSummary { get; }

    /// <inheritdoc/>
    public int Order { get; }

    /// <inheritdoc/>
    public IReadOnlyList<IType> Children => _children;

    /// <inheritdoc/>
    public int Min { get; }

    /// <inheritdoc/>
    public string Max { get; }

    /// <inheritdoc/>
    public IReadOnlyList<IConstraint> Constraints { get; }

    /// <inheritdoc/>
    public IBinding? Binding { get; }

    /// <inheritdoc/>
    public object? FixedValue { get; }

    /// <inheritdoc/>
    public object? PatternValue { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ITypeReference> Types { get; }

    /// <inheritdoc/>
    public string? DefaultTypeName { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> ReferenceTargets { get; }

    /// <inheritdoc/>
    public string? ContentReference { get; }

    /// <summary>
    /// Adds a child type definition to this element.
    /// Used during tree construction by <see cref="StructureDefinitionTypeBuilder"/>.
    /// </summary>
    /// <param name="child">The child type definition to add.</param>
    internal void AddChild(IType child)
    {
        _children.Add(child);
    }
}
