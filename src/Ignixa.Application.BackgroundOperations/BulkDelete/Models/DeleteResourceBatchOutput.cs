// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Output from DeleteResourceBatchActivity.
/// Reports the result of deleting a batch of resources.
/// </summary>
public record DeleteResourceBatchOutput(
    /// <summary>
    /// Resource type that was deleted.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// Number of resources successfully deleted.
    /// </summary>
    long DeletedCount,

    /// <summary>
    /// Number of resources that failed to delete.
    /// </summary>
    long FailedCount,

    /// <summary>
    /// Error messages for failed deletions.
    /// </summary>
    IReadOnlyList<string>? Errors = null);
