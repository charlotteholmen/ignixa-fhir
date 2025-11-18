// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Output from GetResourcesToDeleteActivity.
/// Contains grouped resources ready for deletion.
/// </summary>
public record GetResourcesToDeleteOutput(
    /// <summary>
    /// Dictionary mapping resource type to list of resource IDs to delete.
    /// Example: { "Patient": ["123", "456"], "Observation": ["789"] }
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> ResourcesByType,

    /// <summary>
    /// Total count of resources to delete.
    /// </summary>
    long TotalCount);
