// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Orchestration output for bulk delete operation.
/// Contains aggregated results from all delete activities.
/// </summary>
public record BulkDeleteOrchestrationOutput(
    /// <summary>
    /// Whether the operation completed successfully.
    /// </summary>
    bool Success,

    /// <summary>
    /// Total number of resources deleted.
    /// </summary>
    long TotalResourcesDeleted,

    /// <summary>
    /// Dictionary of resource type to count of deleted resources.
    /// Example: { "Patient": 150, "Observation": 1200 }
    /// </summary>
    IReadOnlyDictionary<string, long>? DeletedResourcesByType,

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    string? ErrorMessage = null,

    /// <summary>
    /// Which phase failed (Initialization, Execution, Completion).
    /// </summary>
    string? FailurePhase = null);
