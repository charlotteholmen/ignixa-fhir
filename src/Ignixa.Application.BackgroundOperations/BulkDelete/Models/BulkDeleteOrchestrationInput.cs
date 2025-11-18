// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Orchestration input for bulk delete operation.
/// Initiates the entire bulk delete job.
/// </summary>
public record BulkDeleteOrchestrationInput(
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    string JobId,

    /// <summary>
    /// Tenant that requested the delete.
    /// </summary>
    int TenantId,

    /// <summary>
    /// Resource type to delete (null for system-level delete).
    /// </summary>
    string? ResourceType = null,

    /// <summary>
    /// Search query parameters to filter resources for deletion.
    /// </summary>
    string? SearchQuery = null,

    /// <summary>
    /// If true, permanently removes resources.
    /// </summary>
    bool HardDelete = false,

    /// <summary>
    /// If true, deletes version history.
    /// </summary>
    bool PurgeHistory = false,

    /// <summary>
    /// Resource types to exclude from deletion (system-level only).
    /// </summary>
    IReadOnlyCollection<string>? ExcludedResourceTypes = null,

    /// <summary>
    /// If true, replaces deleted resource references.
    /// </summary>
    bool RemoveReferences = false,

    /// <summary>
    /// Targets resources unreferenced by specified resource types.
    /// </summary>
    IReadOnlyCollection<string>? NotReferencedBy = null);
