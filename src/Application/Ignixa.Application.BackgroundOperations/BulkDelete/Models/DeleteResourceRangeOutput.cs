// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Output from DeleteResourceRangeActivity.
/// Reports the result of deleting resources in a surrogate ID range.
/// </summary>
public record DeleteResourceRangeOutput(
    /// <summary>
    /// Resource type that was processed.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// Start of surrogate ID range (inclusive).
    /// </summary>
    long StartSurrogateId,

    /// <summary>
    /// End of surrogate ID range (inclusive).
    /// </summary>
    long EndSurrogateId,

    /// <summary>
    /// Number of resources successfully deleted in this range.
    /// </summary>
    long DeletedCount,

    /// <summary>
    /// Number of resources that failed to delete.
    /// </summary>
    long FailedCount,

    /// <summary>
    /// Sample error messages (max 10) for failed deletions.
    /// </summary>
    IReadOnlyList<string>? Errors = null);
