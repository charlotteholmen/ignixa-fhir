// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.BulkUpdate.Models;

/// <summary>
/// Input for a single bulk update worker activity.
/// Processes one range of resources in parallel with other workers.
/// </summary>
public record BulkUpdateWorkerInput
{
    /// <summary>
    /// Unique identifier for the bulk update job.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Tenant ID for multi-tenancy isolation.
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// FHIR resource type to process (e.g., "Patient", "Observation").
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// Starting surrogate ID (inclusive) for this worker's range.
    /// </summary>
    public required long StartSurrogateId { get; init; }

    /// <summary>
    /// Ending surrogate ID (inclusive) for this worker's range.
    /// </summary>
    public required long EndSurrogateId { get; init; }

    /// <summary>
    /// Optional FHIR search query for additional filtering within the range.
    /// </summary>
    public string? SearchQuery { get; init; }

    /// <summary>
    /// List of patch operations to apply to each resource.
    /// </summary>
    public required IReadOnlyList<BulkUpdateOperationDefinition> Operations { get; init; }

    /// <summary>
    /// Number of resources to process per database batch (default: 1000).
    /// </summary>
    public int BatchSize { get; init; } = 1000;
}
