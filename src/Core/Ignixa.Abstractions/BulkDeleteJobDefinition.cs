// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;

namespace Ignixa.Abstractions;

/// <summary>
/// Immutable bulk delete job definition (input parameters) for use with BackgroundJob&lt;BulkDeleteJobDefinition&gt;.
/// Represents the configuration of a FHIR bulk delete operation.
/// TenantId is stored here (in the payload), not as a BackgroundJob property.
/// </summary>
public class BulkDeleteJobDefinition : IJobDefinition
{
    /// <summary>
    /// Tenant ID for multi-tenancy isolation (stored in definition payload, not schema).
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Resource type to delete (null for system-level delete).
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Search query parameters to filter resources for deletion.
    /// Example: "status=inactive&amp;date=lt2020-01-01"
    /// </summary>
    public string? SearchQuery { get; init; }

    /// <summary>
    /// If true, permanently removes resources. If false, historical versions remain.
    /// Default: false
    /// </summary>
    public bool HardDelete { get; init; }

    /// <summary>
    /// If true, deletes version history (not current/soft-deleted resources).
    /// Default: false
    /// </summary>
    public bool PurgeHistory { get; init; }

    /// <summary>
    /// Comma-separated list of resource types to exclude from deletion (system-level only).
    /// </summary>
    public IReadOnlyCollection<string> ExcludedResourceTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// If true, replaces deleted resource references with "Referenced resource deleted".
    /// Default: false
    /// </summary>
    public bool RemoveReferences { get; init; }

    /// <summary>
    /// Targets resources unreferenced by specified resource types.
    /// Example: ["Patient", "Encounter"]
    /// </summary>
    public IReadOnlyCollection<string> NotReferencedBy { get; init; } = Array.Empty<string>();
}
