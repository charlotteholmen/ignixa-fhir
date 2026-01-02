// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.BulkPatch.Models;

/// <summary>
/// Output from bulk patch orchestration.
/// Contains aggregated results from all worker activities.
/// </summary>
public record BulkPatchOrchestrationOutput
{
    /// <summary>
    /// Unique identifier for the bulk patch job.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Final status of the orchestration ("Completed" or "Failed").
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Error message if the orchestration failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Total number of resources processed across all workers.
    /// </summary>
    public int TotalProcessed { get; init; }

    /// <summary>
    /// Total number of resources successfully updated.
    /// </summary>
    public int TotalUpdated { get; init; }

    /// <summary>
    /// Total number of resources ignored (no changes needed).
    /// </summary>
    public int TotalIgnored { get; init; }

    /// <summary>
    /// Total number of resources that failed to update.
    /// </summary>
    public int TotalFailed { get; init; }

    /// <summary>
    /// Count of updated resources by resource type.
    /// </summary>
    public Dictionary<string, int>? UpdatedCounts { get; init; }

    /// <summary>
    /// Count of ignored resources by resource type.
    /// </summary>
    public Dictionary<string, int>? IgnoredCounts { get; init; }

    /// <summary>
    /// Count of failed resources by resource type.
    /// </summary>
    public Dictionary<string, int>? FailedCounts { get; init; }

    /// <summary>
    /// Issues from failed resource updates (capped at 1000 total).
    /// </summary>
    public IReadOnlyList<BulkPatchIssue>? Issues { get; init; }
}
