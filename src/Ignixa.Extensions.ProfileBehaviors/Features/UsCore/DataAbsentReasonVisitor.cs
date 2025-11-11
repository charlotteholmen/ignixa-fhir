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
/// - Uses metadata (IsRequired, Types, element name patterns) to determine eligibility
/// - Checks for coded elements with likely required bindings
/// - Uses "unknown" code by default
/// </para>
/// </remarks>
public sealed class DataAbsentReasonVisitor : IResourcePropertyVisitor
{
    private const string DataAbsentReasonUrl = "http://hl7.org/fhir/StructureDefinition/data-absent-reason";
    private const string DefaultCode = "unknown";

    // System/mandatory fields that should never have data-absent-reason
    private static readonly HashSet<string> SystemFields = new(StringComparer.Ordinal)
    {
        "resourceType",
        "id",
        "meta"
    };

    // Element name patterns that typically indicate required bindings
    // These cannot have data-absent-reason as the binding doesn't include "unknown"
    private static readonly HashSet<string> RequiredBindingPatterns = new(StringComparer.Ordinal)
    {
        "status",           // Most status fields (Observation.status, Condition.clinicalStatus, etc.)
        "clinicalStatus",   // Condition.clinicalStatus (required binding)
        "verificationStatus", // AllergyIntolerance.verificationStatus
        "intent",           // Request resources (MedicationRequest.intent)
        "priority"          // Some priority fields have required bindings
    };

    // FHIR types that indicate coded elements (may have binding restrictions)
    private static readonly HashSet<string> CodedTypes = new(StringComparer.Ordinal)
    {
        "code",
        "Coding",
        "CodeableConcept"
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

        // Check if element is eligible for data-absent-reason injection
        if (!CanHaveDataAbsentReason(propertyName, metadata))
        {
            return PropertyVisitResult.Skip();
        }

        // Inject data-absent-reason
        return PropertyVisitResult.Inject(() => CreateDataAbsentReasonElement(metadata));
    }

    /// <summary>
    /// Determines if an element can have a data-absent-reason extension.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>FHIR Rules</strong>:
    /// - Element must be mandatory (IsRequired = true)
    /// - Element must not be a system field (resourceType, id, meta)
    /// - Element must not have a required binding without "unknown" code
    /// </para>
    /// <para>
    /// <strong>Binding Strength Detection</strong>:
    /// Primary: Direct check of metadata.BindingStrength (from IExtendedElementMetadata)
    /// - Required → Cannot have data-absent-reason (no "unknown" code in binding)
    /// - Extensible/Preferred/Example → Can have data-absent-reason
    /// Fallback: Heuristics based on element name and type patterns
    /// - Element name matches known required binding patterns (e.g., "status")
    /// - Element is coded type with name suggesting required binding
    /// </para>
    /// </remarks>
    private static bool CanHaveDataAbsentReason(string propertyName, ElementMetadata metadata)
    {
        // Must be mandatory
        if (!metadata.IsRequired)
        {
            return false;
        }

        // Skip system fields
        if (SystemFields.Contains(propertyName))
        {
            return false;
        }

        // Check binding strength directly if available (most reliable method)
        if (!string.IsNullOrEmpty(metadata.BindingStrength))
        {
            // Required bindings without "unknown" code cannot have data-absent-reason
            // The US Core guidance states: "For required bindings without unknown code,
            // return HTTP 404 for read or exclude from search results"
            if (metadata.BindingStrength.Equals("Required", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Extensible, Preferred, Example bindings CAN have data-absent-reason
            // because they allow codes outside the ValueSet or can use text-only
            return true;
        }

        // Fallback to heuristics when binding strength not available
        // Check if element name matches known required binding patterns
        if (RequiredBindingPatterns.Contains(propertyName))
        {
            return false;
        }

        // Check for coded elements with likely required bindings using heuristics
        if (IsCodedElement(metadata))
        {
            // Additional heuristics for coded elements
            // If element name suggests a required binding, skip it
            if (propertyName.EndsWith("Status", StringComparison.Ordinal) ||
                propertyName.EndsWith("Intent", StringComparison.Ordinal) ||
                propertyName.Equals("code", StringComparison.Ordinal))
            {
                // Conservative: Skip coded elements with these patterns
                // They often have required bindings
                return false;
            }
        }

        // Element is eligible for data-absent-reason
        return true;
    }

    /// <summary>
    /// Checks if an element is a coded element (code, Coding, CodeableConcept).
    /// </summary>
    private static bool IsCodedElement(ElementMetadata metadata)
    {
        if (metadata.Types == null || metadata.Types.Count == 0)
        {
            return false;
        }

        // Check if any type is a coded type
        return metadata.Types.Any(t => CodedTypes.Contains(t));
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
