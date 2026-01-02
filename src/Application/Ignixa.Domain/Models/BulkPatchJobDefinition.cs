// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;

namespace Ignixa.Domain.Models;

/// <summary>
/// Immutable bulk patch job definition (input parameters) for use with BackgroundJob<BulkPatchJobDefinition>.
/// Represents the configuration of a FHIR bulk patch operation.
/// TenantId is stored here (in the payload), not as a BackgroundJob property.
/// </summary>
public class BulkPatchJobDefinition : IJobDefinition
{
    /// <summary>
    /// Tenant ID for multi-tenancy isolation (stored in definition payload, not schema).
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Resource type to patch. If null, applies to all resource types.
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Optional search query string for filtering resources to patch.
    /// Format: FHIR search query (e.g., "status=active&category=encounter-diagnosis").
    /// If null, applies to all resources of the specified type.
    /// </summary>
    public string? SearchQuery { get; init; }

    /// <summary>
    /// List of patch operations to apply to matching resources.
    /// </summary>
    public required IReadOnlyList<BulkPatchOperationDefinition> Operations { get; init; }
}
