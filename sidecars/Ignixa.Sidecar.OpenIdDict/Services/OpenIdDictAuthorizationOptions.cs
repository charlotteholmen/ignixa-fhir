// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;

namespace Ignixa.Sidecar.OpenIdDict.Services;

/// <summary>
/// Configuration options for OpenIdDict authorization.
/// </summary>
public class OpenIdDictAuthorizationOptions
{
    /// <summary>
    /// Required scopes for FHIR access. If empty, any authenticated user is authorized.
    /// </summary>
    public Collection<string> RequiredScopes { get; } = new();

    /// <summary>
    /// Mapping of FHIR actions to required scopes.
    /// Key: action (read, write, delete, search)
    /// Value: list of scopes that can perform the action
    /// </summary>
    public Dictionary<string, Collection<string>> ActionScopeMapping { get; } = new();

    /// <summary>
    /// Whether to allow all authenticated users when no specific authorization rules match.
    /// Default: true (for local development and testing).
    /// </summary>
    public bool AllowAuthenticatedByDefault { get; set; } = true;
}
