// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Represents a single patch operation to apply to FHIR resources.
/// </summary>
public record BulkUpdateOperationDefinition
{
    /// <summary>
    /// Type of patch operation: "replace" or "upsert".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// FHIRPath expression identifying the element(s) to modify.
    /// Examples: "Patient.meta.tag", "Observation.status", "Condition.category".
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Optional name for the element being added (used with upsert operations).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The value to set at the specified path.
    /// Type depends on FHIR element type (string, integer, CodeableConcept, etc.).
    /// </summary>
    public object? Value { get; init; }
}
