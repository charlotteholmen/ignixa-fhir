// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkUpdate.Models;

/// <summary>
/// Input for updating bulk update job progress.
/// Used to report incremental progress to the background job repository.
/// </summary>
public record UpdateBulkUpdateProgressInput
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
    /// Total number of resources processed so far.
    /// </summary>
    public int ProcessedResources { get; init; }

    /// <summary>
    /// Total number of resources updated so far.
    /// </summary>
    public int UpdatedResources { get; init; }

    /// <summary>
    /// Total number of resources ignored so far.
    /// </summary>
    public int IgnoredResources { get; init; }

    /// <summary>
    /// Total number of resources that failed so far.
    /// </summary>
    public int FailedResources { get; init; }

    /// <summary>
    /// Total estimated number of resources to process.
    /// </summary>
    public int TotalEstimated { get; init; }

    /// <summary>
    /// Current resource type being processed.
    /// </summary>
    public string? CurrentResourceType { get; init; }
}
