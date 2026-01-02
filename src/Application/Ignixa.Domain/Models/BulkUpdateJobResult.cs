// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Represents the final result of a bulk update job.
/// </summary>
public record BulkUpdateJobResult
{
    /// <summary>
    /// Count of updated resources by resource type.
    /// </summary>
    public required Dictionary<string, int> UpdatedCounts { get; init; }

    /// <summary>
    /// Count of ignored resources (no changes needed) by resource type.
    /// </summary>
    public required Dictionary<string, int> IgnoredCounts { get; init; }

    /// <summary>
    /// Count of failed resources by resource type.
    /// </summary>
    public required Dictionary<string, int> FailedCounts { get; init; }

    /// <summary>
    /// Details about resources that failed to update.
    /// </summary>
    public required IReadOnlyList<BulkUpdateIssue> Issues { get; init; }
}
