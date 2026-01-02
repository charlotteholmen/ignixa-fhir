// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkUpdate;

/// <summary>
/// Result of creating a new bulk update job.
/// Contains job identifier and initial status information.
/// </summary>
public record CreateBulkUpdateJobResult
{
    /// <summary>
    /// Unique job identifier for tracking.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Initial job status (typically "Queued").
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Orchestration instance ID from DurableTask.
    /// </summary>
    public string? OrchestrationInstanceId { get; init; }

    /// <summary>
    /// Job creation timestamp.
    /// </summary>
    public required DateTimeOffset CreateDate { get; init; }
}
