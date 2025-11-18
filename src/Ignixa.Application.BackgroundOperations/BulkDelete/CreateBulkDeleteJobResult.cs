// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Result of creating a bulk delete job.
/// </summary>
public class CreateBulkDeleteJobResult
{
    /// <summary>
    /// Unique job identifier for polling status.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Initial job status (typically "Queued").
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// DurableTask orchestration instance ID.
    /// </summary>
    public required string OrchestrationInstanceId { get; init; }

    /// <summary>
    /// Job creation timestamp.
    /// </summary>
    public DateTimeOffset CreateDate { get; init; }
}
