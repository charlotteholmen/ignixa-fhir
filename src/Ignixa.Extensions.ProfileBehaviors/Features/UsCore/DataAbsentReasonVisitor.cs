// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Extensions.ProfileBehaviors.Abstractions;

namespace Ignixa.Extensions.ProfileBehaviors.Features.UsCore;

/// <summary>
/// Visitor implementation that injects data-absent-reason extensions for missing mandatory elements.
/// Implements US Core requirement: "For mandatory elements with missing data and unknown reason,
/// include the element with a data-absent-reason extension using code 'unknown'."
/// </summary>
/// <remarks>
/// <para>
/// <strong>US Core Guidance</strong>:
/// - Optional elements (min=0): Omit entirely if missing
/// - Mandatory elements (min>0): Add element with data-absent-reason extension
/// - Required bindings without unknown code (e.g., status): Cannot use data-absent-reason
/// </para>
/// <para>
/// <strong>Implementation Strategy</strong>:
/// - Only injects at root level (depth = 0) to avoid complexity
/// - Skips elements with required binding (status fields)
/// - Uses "unknown" code by default
/// </para>
/// </remarks>
public sealed class DataAbsentReasonVisitor : IResourcePropertyVisitor
{
    private const string DataAbsentReasonUrl = "http://hl7.org/fhir/StructureDefinition/data-absent-reason";
    private const string DefaultCode = "unknown";

    // Elements that CANNOT have data-absent-reason (required binding without unknown code)
    private static readonly HashSet<string> ExcludedElements = new(StringComparer.Ordinal)
    {
        "status", // Most status fields have required binding
        "resourceType",
        "id",
        "meta"
    };

    /// <summary>
    /// Visit existing property - pass through unchanged for US Core.
    /// </summary>
    public PropertyVisitResult VisitProperty(
        string propertyName,
        ElementMetadata? metadata,
        int depth,
        WalkingContext context)
    {
        // US Core visitor doesn't modify existing properties
        return PropertyVisitResult.Include();
    }

    /// <summary>
    /// Visit missing mandatory property - inject data-absent-reason if appropriate.
    /// </summary>
    public PropertyVisitResult VisitMissingProperty(
        string propertyName,
        ElementMetadata metadata,
        int depth,
        WalkingContext context)
    {
        // Only inject at root level
        if (depth > 0)
        {
            return PropertyVisitResult.Skip();
        }

        // Skip excluded elements (required binding, system fields)
        if (ExcludedElements.Contains(propertyName))
        {
            return PropertyVisitResult.Skip();
        }

        // Check if element is truly mandatory
        if (!metadata.IsRequired)
        {
            return PropertyVisitResult.Skip();
        }

        // TODO: Check binding strength - skip if required binding
        // For now, we skip known problematic elements via ExcludedElements

        // Inject data-absent-reason
        return PropertyVisitResult.Inject(() => CreateDataAbsentReasonElement(metadata));
    }

    /// <summary>
    /// Creates a FHIR element with data-absent-reason extension.
    /// </summary>
    private static JsonNode CreateDataAbsentReasonElement(ElementMetadata metadata)
    {
        if (metadata.IsCollection)
        {
            // Array element: name: [{ extension: [...] }]
            var arrayNode = new JsonArray();
            var elementWithExtension = CreateElementWithExtension();
            arrayNode.Add(elementWithExtension);
            return arrayNode;
        }
        else
        {
            // Single element: name: { extension: [...] }
            return CreateElementWithExtension();
        }
    }

    /// <summary>
    /// Creates a JSON object with data-absent-reason extension.
    /// </summary>
    private static JsonObject CreateElementWithExtension()
    {
        var element = new JsonObject();
        var extension = new JsonArray
        {
            new JsonObject
            {
                ["url"] = DataAbsentReasonUrl,
                ["valueCode"] = DefaultCode
            }
        };
        element["extension"] = extension;
        return element;
    }
}
