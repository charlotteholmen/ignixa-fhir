// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Final result of a bulk delete job.
/// Stored as JSON in BackgroundJob.Result field.
/// </summary>
public class BulkDeleteJobResult
{
    /// <summary>
    /// Total number of resources deleted.
    /// </summary>
    public long TotalResourcesDeleted { get; set; }

    /// <summary>
    /// Dictionary mapping resource type to count of deleted resources.
    /// Example: { "Patient": 150, "Observation": 1200 }
    /// </summary>
    public Dictionary<string, long> DeletedResourcesByType { get; } = new();

    /// <summary>
    /// Any error messages from the operation.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
