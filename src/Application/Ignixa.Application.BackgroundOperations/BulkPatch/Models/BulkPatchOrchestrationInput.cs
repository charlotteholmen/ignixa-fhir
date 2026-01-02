// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.BulkPatch.Models;

/// <summary>
/// Input for bulk patch orchestration.
/// Defines the scope and parameters for the bulk patch operation.
/// </summary>
public record BulkPatchOrchestrationInput
{
    /// <summary>
    /// Unique identifier for the bulk patch job.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Tenant ID for multi-tenancy isolation.
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Resource type to patch (e.g., "Patient", "Observation").
    /// If null, applies to all resource types matching the search query.
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Optional FHIR search query for filtering resources to patch.
    /// If null, applies to all resources of the specified type.
    /// </summary>
    public string? SearchQuery { get; init; }

    /// <summary>
    /// List of patch operations to apply to matching resources.
    /// </summary>
    public required IReadOnlyList<BulkPatchOperationDefinition> Operations { get; init; }

    /// <summary>
    /// Number of resources to process in each worker batch (default: 1000).
    /// Larger batches reduce overhead but increase memory usage.
    /// </summary>
    public int BatchSize { get; init; } = 1000;
}
