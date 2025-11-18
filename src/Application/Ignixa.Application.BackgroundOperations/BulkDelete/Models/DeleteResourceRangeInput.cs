// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Input for DeleteResourceRangeActivity.
/// Specifies a surrogate ID range to delete for streaming processing.
/// </summary>
public record DeleteResourceRangeInput(
    /// <summary>
    /// Job ID for tracking.
    /// </summary>
    string JobId,

    /// <summary>
    /// Tenant ID for multi-tenancy isolation.
    /// </summary>
    int TenantId,

    /// <summary>
    /// Resource type being deleted.
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
    /// Search query parameters to filter resources within this range.
    /// Example: "status=inactive&amp;date=lt2020-01-01"
    /// </summary>
    string? SearchQuery,

    /// <summary>
    /// If true, permanently removes resources.
    /// </summary>
    bool HardDelete,

    /// <summary>
    /// If true, deletes version history.
    /// </summary>
    bool PurgeHistory,

    /// <summary>
    /// Targets resources unreferenced by specified resource types.
    /// Example: ["Patient", "Encounter"]
    /// </summary>
    IReadOnlyCollection<string> NotReferencedBy);
