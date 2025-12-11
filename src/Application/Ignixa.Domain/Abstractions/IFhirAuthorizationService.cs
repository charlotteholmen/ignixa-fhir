// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// FHIR-specific authorization service for delegating authorization decisions.
/// Implementations may use local policies, external sidecars, or other authorization systems.
/// </summary>
public interface IFhirAuthorizationService
{
    /// <summary>
    /// Authorizes access to a FHIR resource.
    /// </summary>
    /// <param name="user">The user's claims principal.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient").</param>
    /// <param name="resourceId">The resource ID (if applicable).</param>
    /// <param name="action">The action being performed (e.g., "read", "write", "delete").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization result.</returns>
    Task<FhirAuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        int tenantId,
        string resourceType,
        string? resourceId,
        string action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes access based on a named policy.
    /// </summary>
    /// <param name="user">The user's claims principal.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="policyName">The policy name to evaluate.</param>
    /// <param name="resource">Optional resource context for the policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization result.</returns>
    Task<FhirAuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        int tenantId,
        string policyName,
        object? resource = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a FHIR authorization decision.
/// </summary>
public record FhirAuthorizationResult
{
    /// <summary>
    /// Whether the request is authorized.
    /// </summary>
    public bool IsAuthorized { get; init; }

    /// <summary>
    /// Reason for the decision (useful for audit/debugging).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Additional context returned by the authorization service.
    /// </summary>
    public IDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Creates a successful authorization result.
    /// </summary>
    public static FhirAuthorizationResult Success(string? reason = null) =>
        new() { IsAuthorized = true, Reason = reason ?? "Authorized" };

    /// <summary>
    /// Creates a failed authorization result.
    /// </summary>
    public static FhirAuthorizationResult Failure(string reason) =>
        new() { IsAuthorized = false, Reason = reason };
}
