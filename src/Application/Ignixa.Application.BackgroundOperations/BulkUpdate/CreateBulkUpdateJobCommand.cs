// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Medino;

namespace Ignixa.Application.BackgroundOperations.BulkUpdate;

/// <summary>
/// Command to create and start a bulk update job.
/// Validates input parameters and initiates the bulk update orchestration.
/// </summary>
public record CreateBulkUpdateJobCommand : IRequest<CreateBulkUpdateJobResult>
{
    /// <summary>
    /// Tenant ID for multi-tenancy isolation.
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Resource type to update. If null, applies to all resource types.
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Optional search query string for filtering resources to update.
    /// Format: FHIR search query (e.g., "status=active&amp;category=encounter-diagnosis").
    /// </summary>
    public string? SearchQuery { get; init; }

    /// <summary>
    /// FHIR Parameters resource containing the patch operations.
    /// Must contain at least one 'operation' parameter.
    /// </summary>
    public required ResourceJsonNode PatchParameters { get; init; }
}
