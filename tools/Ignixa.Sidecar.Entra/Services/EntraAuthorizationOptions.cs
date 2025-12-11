// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;

namespace Ignixa.Sidecar.Entra.Services;

/// <summary>
/// Configuration options for Entra ID authorization.
/// </summary>
public class EntraAuthorizationOptions
{
    /// <summary>
    /// The Entra ID tenant ID.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The Entra ID client (application) ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Required roles for FHIR access. If empty, any authenticated user is authorized.
    /// </summary>
    public Collection<string> RequiredRoles { get; } = new();

    /// <summary>
    /// Required scopes for FHIR access. If empty, any authenticated user is authorized.
    /// </summary>
    public Collection<string> RequiredScopes { get; } = new();

    /// <summary>
    /// Mapping of FHIR actions to Entra ID roles.
    /// Key: action (read, write, delete, search)
    /// Value: list of roles that can perform the action
    /// </summary>
    public Dictionary<string, Collection<string>> ActionRoleMapping { get; } = new();

    /// <summary>
    /// Mapping of FHIR resource types to Entra ID roles.
    /// Key: resource type (Patient, Observation, etc.)
    /// Value: list of roles that can access the resource type
    /// </summary>
    public Dictionary<string, Collection<string>> ResourceTypeRoleMapping { get; } = new();

    /// <summary>
    /// Whether to allow all authenticated users when no specific authorization rules match.
    /// Default: true (for backwards compatibility with simple setups).
    /// </summary>
    public bool AllowAuthenticatedByDefault { get; set; } = true;
}
