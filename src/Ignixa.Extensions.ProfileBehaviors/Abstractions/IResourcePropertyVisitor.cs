// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Extensions.ProfileBehaviors.Abstractions;

/// <summary>
/// Visitor pattern for walking FHIR resource properties with schema metadata.
/// Enables extensible transformations: filtering, mutation, injection, etc.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Use Cases</strong>:
/// - Element filtering (ResourceElementsSerializer): Skip non-requested elements
/// - Data-absent-reason injection (US Core): Add extensions for missing mandatory elements
/// - Custom transformations: Redaction, encryption, normalization
/// </para>
/// <para>
/// <strong>Architecture</strong>:
/// Two walker implementations:
/// - ExtensibleStreamingSerializer: For byte-level transformations (Utf8JsonReader/Writer)
/// - ExtensibleJsonNodeWalker: For tree mutation (JsonNode.MutableNode)
/// </para>
/// </remarks>
public interface IResourcePropertyVisitor
{
    /// <summary>
    /// Called when a property is encountered during walking.
    /// </summary>
    /// <param name="propertyName">The JSON property name (e.g., "name", "birthDate").</param>
    /// <param name="metadata">Schema metadata for this element (cardinality, type, etc.).</param>
    /// <param name="depth">Nesting depth (0 = root level, 1 = nested, etc.).</param>
    /// <param name="context">Walking context (resource type, options, etc.).</param>
    /// <returns>Action to take: Include, Skip, Mutate.</returns>
    PropertyVisitResult VisitProperty(
        string propertyName,
        ElementMetadata? metadata,
        int depth,
        WalkingContext context);

    /// <summary>
    /// Called when a mandatory property is missing during walking.
    /// Only invoked for elements with min > 0 that are not present in the resource.
    /// </summary>
    /// <param name="propertyName">The missing property name.</param>
    /// <param name="metadata">Schema metadata for this element.</param>
    /// <param name="depth">Nesting depth (0 = root level).</param>
    /// <param name="context">Walking context.</param>
    /// <returns>Action to take: Skip (do nothing) or Inject (add property).</returns>
    PropertyVisitResult VisitMissingProperty(
        string propertyName,
        ElementMetadata metadata,
        int depth,
        WalkingContext context);
}

/// <summary>
/// Result of visiting a property: what action should the walker take?
/// </summary>
public sealed class PropertyVisitResult
{
    /// <summary>
    /// The action type.
    /// </summary>
    public PropertyAction Action { get; }

    /// <summary>
    /// For Mutate action: function to transform the property value.
    /// </summary>
    public Func<System.Text.Json.Nodes.JsonNode?, System.Text.Json.Nodes.JsonNode?>? MutationFunc { get; }

    /// <summary>
    /// For Inject action: function to create the missing property value.
    /// </summary>
    public Func<System.Text.Json.Nodes.JsonNode>? InjectionFunc { get; }

    private PropertyVisitResult(
        PropertyAction action,
        Func<System.Text.Json.Nodes.JsonNode?, System.Text.Json.Nodes.JsonNode?>? mutationFunc = null,
        Func<System.Text.Json.Nodes.JsonNode>? injectionFunc = null)
    {
        Action = action;
        MutationFunc = mutationFunc;
        InjectionFunc = injectionFunc;
    }

    /// <summary>
    /// Include the property as-is (pass through unchanged).
    /// </summary>
    public static PropertyVisitResult Include() => new(PropertyAction.Include);

    /// <summary>
    /// Skip the property (do not include in output).
    /// </summary>
    public static PropertyVisitResult Skip() => new(PropertyAction.Skip);

    /// <summary>
    /// Mutate the property value using the provided function.
    /// </summary>
    /// <param name="mutationFunc">Function to transform the property value.</param>
    public static PropertyVisitResult Mutate(Func<System.Text.Json.Nodes.JsonNode?, System.Text.Json.Nodes.JsonNode?> mutationFunc)
        => new(PropertyAction.Mutate, mutationFunc: mutationFunc);

    /// <summary>
    /// Inject a new property (for missing mandatory elements).
    /// </summary>
    /// <param name="injectionFunc">Function to create the property value.</param>
    public static PropertyVisitResult Inject(Func<System.Text.Json.Nodes.JsonNode> injectionFunc)
        => new(PropertyAction.Inject, injectionFunc: injectionFunc);
}

/// <summary>
/// Actions the walker can take for a property.
/// </summary>
public enum PropertyAction
{
    /// <summary>
    /// Include the property unchanged.
    /// </summary>
    Include,

    /// <summary>
    /// Skip the property (exclude from output).
    /// </summary>
    Skip,

    /// <summary>
    /// Mutate the property value.
    /// </summary>
    Mutate,

    /// <summary>
    /// Inject a new property (for missing elements).
    /// </summary>
    Inject
}

/// <summary>
/// Metadata about a FHIR element from the schema.
/// Lightweight wrapper around IElementDefinitionSummary.
/// </summary>
public sealed class ElementMetadata
{
    /// <summary>
    /// Element name (e.g., "name", "birthDate").
    /// </summary>
    public required string ElementName { get; init; }

    /// <summary>
    /// Is this a collection (array)?
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    /// Is this element required (min > 0)?
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Is this a choice type (e.g., value[x])?
    /// </summary>
    public bool IsChoiceElement { get; init; }

    /// <summary>
    /// Element types (e.g., "string", "Reference(Patient)").
    /// </summary>
    public IReadOnlyList<string> Types { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Binding strength (for coded elements): required, extensible, preferred, example.
    /// </summary>
    public string? BindingStrength { get; init; }

    /// <summary>
    /// Creates ElementMetadata from IElementDefinitionSummary.
    /// </summary>
    public static ElementMetadata FromElementDefinition(IElementDefinitionSummary element)
    {
        return new ElementMetadata
        {
            ElementName = element.ElementName,
            IsCollection = element.IsCollection,
            IsRequired = element.IsRequired,
            IsChoiceElement = element.IsChoiceElement,
            Types = element.Type?.Select(t => t.GetTypeName()).ToList() ?? Array.Empty<string>()
        };
    }
}

/// <summary>
/// Context information for the walker.
/// </summary>
public sealed class WalkingContext
{
    /// <summary>
    /// The FHIR resource type being walked (e.g., "Patient").
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// FHIR version (R4, R4B, R5).
    /// </summary>
    public required FhirSpecification FhirVersion { get; init; }

    /// <summary>
    /// Maximum depth to walk (0 = root only, -1 = unlimited).
    /// </summary>
    public int MaxDepth { get; init; } = -1;

    /// <summary>
    /// Custom options for visitor implementations.
    /// </summary>
    public Dictionary<string, object> Options { get; init; } = new();

    /// <summary>
    /// Gets a typed option value.
    /// </summary>
    public T? GetOption<T>(string key) where T : class
    {
        return Options.TryGetValue(key, out var value) ? value as T : null;
    }
}
