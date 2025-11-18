// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Input for GetResourcesToDeleteActivity.
/// Identifies which resources should be deleted.
/// </summary>
public record GetResourcesToDeleteInput(
    /// <summary>
    /// Tenant ID for multi-tenancy isolation.
    /// </summary>
    int TenantId,

    /// <summary>
    /// Resource type to query (null for all types).
    /// </summary>
    string? ResourceType,

    /// <summary>
    /// Search query parameters to filter resources.
    /// </summary>
    string? SearchQuery,

    /// <summary>
    /// Resource types to exclude (system-level only).
    /// </summary>
    IReadOnlyCollection<string>? ExcludedResourceTypes,

    /// <summary>
    /// Targets resources unreferenced by specified resource types.
    /// </summary>
    IReadOnlyCollection<string>? NotReferencedBy);
