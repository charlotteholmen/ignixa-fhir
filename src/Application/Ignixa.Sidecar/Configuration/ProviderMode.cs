// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Sidecar.Configuration;

/// <summary>
/// Determines which provider implementations are used for cross-cutting concerns.
/// </summary>
public enum ProviderMode
{
    /// <summary>
    /// Use built-in local implementations that require no external dependencies.
    /// Authorization permits all requests, audit logs to console.
    /// Ideal for local development.
    /// </summary>
    Local,

    /// <summary>
    /// Delegate all cross-cutting concerns to the configured sidecar endpoint.
    /// Requires Sidecar:Endpoint configuration.
    /// </summary>
    Sidecar,

    /// <summary>
    /// Support mixed provider configurations where individual services
    /// can be configured independently (e.g., sidecar for auth, local for logging).
    /// </summary>
    Hybrid
}
