// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkPatch.Models;

/// <summary>
/// Input for the GetBulkPatchRanges activity.
/// Determines which resources should be patched based on type and query filters.
/// </summary>
public record GetBulkPatchRangesInput
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
    /// If null, queries all resource types.
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Optional FHIR search query for filtering resources.
    /// If null, includes all resources of the specified type.
    /// </summary>
    public string? SearchQuery { get; init; }
}
