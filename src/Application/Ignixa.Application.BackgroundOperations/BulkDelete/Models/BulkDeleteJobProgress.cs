// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Progress tracking for bulk delete jobs.
/// Stored as JSON in BackgroundJob.Progress field.
/// </summary>
public class BulkDeleteJobProgress
{
    /// <summary>
    /// Number of resources deleted so far.
    /// </summary>
    public long ResourcesDeleted { get; set; }

    /// <summary>
    /// Percentage of resources deleted (0-100).
    /// </summary>
    public double ProgressPercentage { get; set; }

    /// <summary>
    /// Current phase of deletion (e.g., "Identifying", "Deleting").
    /// </summary>
    public string? CurrentPhase { get; set; }
}
