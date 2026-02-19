// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Validation.Schema;

/// <summary>
/// Parses FHIR StructureDefinition JSON and builds an <see cref="ITypeExtended"/> tree.
/// Uses <c>snapshot.element</c> when available, falling back to <c>differential.element</c>.
/// Leverages <see cref="StructureDefinitionJsonNode"/> for structured property access.
/// </summary>
public class StructureDefinitionTypeBuilder
{
    private static readonly IReadOnlyList<string> AbstractTypes = new[] { "Resource", "DomainResource", "Element", "BackboneElement", "DataType", "PrimitiveType" };

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StructureDefinitionTypeBuilder"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public StructureDefinitionTypeBuilder(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Builds an <see cref="ITypeExtended"/> tree from a pre-parsed <see cref="StructureDefinitionJsonNode"/>.
    /// </summary>
    /// <param name="sdNode">The pre-parsed StructureDefinition node.</param>
    /// <returns>The root <see cref="ITypeExtended"/> or null if building fails.</returns>
    public ITypeExtended? Build(StructureDefinitionJsonNode sdNode)
    {
        if (sdNode == null)
        {
            _logger.LogWarning("Build called with null StructureDefinitionJsonNode");
            return null;
        }

        // Prefer snapshot over differential
        var elements = sdNode.GetSnapshotElements() ?? sdNode.GetDifferentialElements();

        if (elements is null || elements.Count == 0)
        {
            _logger.LogWarning("StructureDefinition has no snapshot or differential elements");
            return null;
        }

        var kind = sdNode.Kind;
        var isAbstractDef = sdNode.IsAbstract;
        var typeName = sdNode.Type ?? sdNode.Name ?? "Unknown";

        // Build flat list of typed definitions keyed by path
        var definitions = new Dictionary<string, StructureDefinitionTypeDefinition>(StringComparer.Ordinal);
        int order = 0;

        foreach (var elementNode in elements)
        {
            if (elementNode is not JsonObject element)
            {
                continue;
            }

            var path = element["path"]?.GetValue<string>();
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            var def = BuildElement(element, path, order++, kind, isAbstractDef, typeName);
            definitions[path] = def;
        }

        if (definitions.Count == 0)
        {
            _logger.LogWarning("No valid elements found in StructureDefinition");
            return null;
        }

        // Build the tree from flat path list
        BuildTree(definitions);

        // Return root (first element, which is the type itself)
        return definitions.Values.First();
    }

    /// <summary>
    /// Convenience overload: parses a JSON string into a <see cref="StructureDefinitionJsonNode"/>, then builds.
    /// </summary>
    /// <param name="json">The StructureDefinition JSON string.</param>
    /// <returns>The root <see cref="ITypeExtended"/> or null if parsing or building fails.</returns>
    public ITypeExtended? Build(string json)
    {
        var sdNode = StructureDefinitionJsonNode.Parse(json, _logger);
        if (sdNode == null)
        {
            return null;
        }

        return Build(sdNode);
    }

    private StructureDefinitionTypeDefinition BuildElement(
        JsonObject element,
        string path,
        int order,
        string? kind,
        bool isAbstractDef,
        string rootTypeName)
    {
        int min = element["min"]?.GetValue<int>() ?? 0;
        string max = element["max"]?.GetValue<string>() ?? "*";
        bool isCollection = max != "0" && max != "1";
        bool isRequired = min > 0;
        bool inSummary = element["isSummary"]?.GetValue<bool>() ?? false;
        bool isModifier = element["isModifier"]?.GetValue<bool>() ?? false;

        // Extract element name (last segment after dot)
        string name = GetElementName(path);

        // Determine if this is the root element
        bool isRoot = !path.Contains('.', StringComparison.Ordinal);

        // Parse type references
        var types = ParseTypeReferences(element);

        // Determine if choice element
        bool isChoiceElement = name.EndsWith("[x]", StringComparison.Ordinal);

        // Determine primitive type
        FhirPrimitive primitive = FhirPrimitive.None;
        if (isRoot)
        {
            primitive = FhirPrimitiveExtensions.FromTypeString(rootTypeName);
        }
        else if (types.Count == 1)
        {
            primitive = FhirPrimitiveExtensions.FromTypeString(types[0].Code);
        }

        // Determine if resource
        bool isResource = isRoot && kind == "resource";

        // Determine if abstract
        bool isAbstract = isRoot && (isAbstractDef || AbstractTypes.Contains(rootTypeName));

        var typeInfo = new TypeInfo(
            name: name,
            primitive: primitive,
            isResource: isResource,
            isAbstract: isAbstract,
            isChoiceElement: isChoiceElement,
            isModifier: isModifier);

        // Parse constraints
        var constraints = ParseConstraints(element);

        // Parse binding
        var binding = ParseBinding(element);

        // Parse fixed/pattern values
        var fixedValue = ParseFixedValue(element);
        var patternValue = ParsePatternValue(element);

        // Parse reference targets
        var referenceTargets = ParseReferenceTargets(types, element);

        // Parse content reference
        var contentReference = element["contentReference"]?.GetValue<string>();

        return new StructureDefinitionTypeDefinition(
            info: typeInfo,
            isCollection: isCollection,
            isRequired: isRequired,
            inSummary: inSummary,
            order: order,
            min: min,
            max: max,
            constraints: constraints.Count > 0 ? constraints : null,
            binding: binding,
            fixedValue: fixedValue,
            patternValue: patternValue,
            types: types.Count > 0 ? types : null,
            referenceTargets: referenceTargets.Count > 0 ? referenceTargets : null,
            contentReference: contentReference);
    }

    private static List<ITypeReference> ParseTypeReferences(JsonObject element)
    {
        var result = new List<ITypeReference>();
        var typeArray = element["type"]?.AsArray();
        if (typeArray is null)
        {
            return result;
        }

        foreach (var typeNode in typeArray)
        {
            if (typeNode is not JsonObject typeObj)
            {
                continue;
            }

            var code = typeObj["code"]?.GetValue<string>();
            if (string.IsNullOrEmpty(code))
            {
                continue;
            }

            // Profile is an array in FHIR, take first
            var profile = typeObj["profile"]?.AsArray()?.FirstOrDefault()?.GetValue<string>();

            // TargetProfile is an array, take first for the reference
            var targetProfile = typeObj["targetProfile"]?.AsArray()?.FirstOrDefault()?.GetValue<string>();

            // Aggregation
            IReadOnlyList<string>? aggregation = null;
            var aggArray = typeObj["aggregation"]?.AsArray();
            if (aggArray is not null && aggArray.Count > 0)
            {
                aggregation = aggArray
                    .Where(a => a is not null)
                    .Select(a => a!.GetValue<string>())
                    .ToList();
            }

            var versioning = typeObj["versioning"]?.GetValue<string>();

            result.Add(new TypeReferenceDefinition(code, profile, targetProfile, aggregation, versioning));
        }

        return result;
    }

    private static List<IConstraint> ParseConstraints(JsonObject element)
    {
        var result = new List<IConstraint>();
        var constraintArray = element["constraint"]?.AsArray();
        if (constraintArray is null)
        {
            return result;
        }

        foreach (var constraintNode in constraintArray)
        {
            if (constraintNode is not JsonObject constraintObj)
            {
                continue;
            }

            var key = constraintObj["key"]?.GetValue<string>();
            var expression = constraintObj["expression"]?.GetValue<string>();
            var severity = constraintObj["severity"]?.GetValue<string>();

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(expression) || string.IsNullOrEmpty(severity))
            {
                continue;
            }

            result.Add(new ConstraintDefinition
            {
                Key = key,
                Expression = expression,
                Human = constraintObj["human"]?.GetValue<string>(),
                Severity = severity,
                Xpath = constraintObj["xpath"]?.GetValue<string>(),
            });
        }

        return result;
    }

    private static IBinding? ParseBinding(JsonObject element)
    {
        var bindingObj = element["binding"]?.AsObject();
        if (bindingObj is null)
        {
            return null;
        }

        var strength = bindingObj["strength"]?.GetValue<string>();
        if (string.IsNullOrEmpty(strength))
        {
            return null;
        }

        var valueSet = bindingObj["valueSet"]?.GetValue<string>();
        var description = bindingObj["description"]?.GetValue<string>();

        return new BindingMetadata(valueSet, strength, description);
    }

    private static object? ParseFixedValue(JsonObject element)
    {
        return ExtractPolymorphicValue(element, "fixed");
    }

    private static object? ParsePatternValue(JsonObject element)
    {
        return ExtractPolymorphicValue(element, "pattern");
    }

    /// <summary>
    /// Extracts a polymorphic FHIR value (fixed[x] or pattern[x]) from the element.
    /// Returns the raw JsonNode for complex types, or the primitive value for simple types.
    /// </summary>
    private static object? ExtractPolymorphicValue(JsonObject element, string prefix)
    {
        foreach (var prop in element)
        {
            if (!prop.Key.StartsWith(prefix, StringComparison.Ordinal) || prop.Key.Length <= prefix.Length)
            {
                continue;
            }

            // Match properties like fixedString, fixedCode, patternCodeableConcept, etc.
            char firstCharAfterPrefix = prop.Key[prefix.Length];
            if (!char.IsUpper(firstCharAfterPrefix))
            {
                continue;
            }

            if (prop.Value is JsonValue jsonValue)
            {
                // Return primitive value
                if (jsonValue.TryGetValue<string>(out var strVal))
                {
                    return strVal;
                }

                if (jsonValue.TryGetValue<bool>(out var boolVal))
                {
                    return boolVal;
                }

                if (jsonValue.TryGetValue<int>(out var intVal))
                {
                    return intVal;
                }

                if (jsonValue.TryGetValue<decimal>(out var decVal))
                {
                    return decVal;
                }
            }

            // For complex types, return the JSON string representation
            return prop.Value?.ToJsonString();
        }

        return null;
    }

    private static List<string> ParseReferenceTargets(List<ITypeReference> types, JsonObject element)
    {
        var targets = new List<string>();

        // Collect all targetProfiles from the raw JSON type array, since ITypeReference.TargetProfile
        // only holds the first one but FHIR allows multiple targetProfiles per type entry.
        var typeArray = element["type"]?.AsArray();
        if (typeArray is null)
        {
            return targets;
        }

        foreach (var typeNode in typeArray)
        {
            if (typeNode is not JsonObject typeObj)
            {
                continue;
            }

            var targetProfileArray = typeObj["targetProfile"]?.AsArray();
            if (targetProfileArray is null)
            {
                continue;
            }

            foreach (var tp in targetProfileArray)
            {
                var canonical = tp?.GetValue<string>();
                if (!string.IsNullOrEmpty(canonical))
                {
                    var resourceName = ExtractResourceNameFromCanonical(canonical);
                    if (!string.IsNullOrEmpty(resourceName))
                    {
                        targets.Add(resourceName);
                    }
                }
            }
        }

        return targets;
    }

    private static string ExtractResourceNameFromCanonical(string canonical)
    {
        // e.g., "http://hl7.org/fhir/StructureDefinition/Patient" → "Patient"
        int lastSlash = canonical.LastIndexOf('/');
        return lastSlash >= 0 && lastSlash < canonical.Length - 1
            ? canonical[(lastSlash + 1)..]
            : canonical;
    }

    private static string GetElementName(string path)
    {
        int lastDot = path.LastIndexOf('.');
        return lastDot >= 0 ? path[(lastDot + 1)..] : path;
    }

    private static void BuildTree(Dictionary<string, StructureDefinitionTypeDefinition> definitions)
    {
        foreach (var kvp in definitions)
        {
            string path = kvp.Key;
            var def = kvp.Value;

            int lastDot = path.LastIndexOf('.');
            if (lastDot < 0)
            {
                // Root element, no parent
                continue;
            }

            string parentPath = path[..lastDot];
            if (definitions.TryGetValue(parentPath, out var parent))
            {
                parent.AddChild(def);
            }
        }
    }
}
