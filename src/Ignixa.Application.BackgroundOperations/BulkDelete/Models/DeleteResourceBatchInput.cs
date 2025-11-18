// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Input for DeleteResourceBatchActivity.
/// Specifies a batch of resources to delete.
/// </summary>
public record DeleteResourceBatchInput(
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
    /// List of resource IDs to delete in this batch.
    /// </summary>
    IReadOnlyList<string> ResourceIds,

    /// <summary>
    /// If true, permanently removes resources.
    /// </summary>
    bool HardDelete,

    /// <summary>
    /// If true, deletes version history.
    /// </summary>
    bool PurgeHistory);
