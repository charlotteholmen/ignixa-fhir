// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.BulkUpdate.Models;

/// <summary>
/// Output from a single bulk update worker activity.
/// Contains processing statistics for one resource range.
/// </summary>
/// <param name="ResourceType">FHIR resource type processed.</param>
/// <param name="ProcessedCount">Number of resources processed.</param>
/// <param name="UpdatedCount">Number of resources successfully updated.</param>
/// <param name="IgnoredCount">Number of resources ignored (no changes).</param>
/// <param name="FailedCount">Number of resources that failed to update.</param>
/// <param name="Issues">List of issues encountered during processing.</param>
public record BulkUpdateWorkerOutput(
    string ResourceType,
    int ProcessedCount,
    int UpdatedCount,
    int IgnoredCount,
    int FailedCount,
    IReadOnlyList<BulkUpdateIssue> Issues);
