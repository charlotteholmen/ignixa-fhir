// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Represents the current progress of a bulk patch job.
/// </summary>
public record BulkPatchJobProgress
{
    /// <summary>
    /// Total number of resources processed so far.
    /// </summary>
    public required int ProcessedResources { get; init; }

    /// <summary>
    /// Number of resources successfully updated.
    /// </summary>
    public required int UpdatedResources { get; init; }

    /// <summary>
    /// Number of resources skipped (no changes needed).
    /// </summary>
    public required int IgnoredResources { get; init; }

    /// <summary>
    /// Number of resources that failed to patch.
    /// </summary>
    public required int FailedResources { get; init; }

    /// <summary>
    /// Progress percentage (0.0 to 100.0).
    /// </summary>
    public required double ProgressPercentage { get; init; }

    /// <summary>
    /// Resource type currently being processed.
    /// </summary>
    public string? CurrentResourceType { get; init; }
}
