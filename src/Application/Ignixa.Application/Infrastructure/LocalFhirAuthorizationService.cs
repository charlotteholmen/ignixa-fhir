// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Local authorization service that permits all requests.
/// This is the default implementation used in Local provider mode for development.
/// </summary>
public class LocalFhirAuthorizationService : IFhirAuthorizationService
{
    private readonly ILogger<LocalFhirAuthorizationService> _logger;

    public LocalFhirAuthorizationService(ILogger<LocalFhirAuthorizationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<FhirAuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        int tenantId,
        string resourceType,
        string? resourceId,
        string action,
        CancellationToken cancellationToken = default)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? "anonymous";

        _logger.LogDebug(
            "Local authorization: Allowing {Action} on {ResourceType}/{ResourceId} for user {UserId} in tenant {TenantId}",
            action,
            resourceType,
            resourceId ?? "(none)",
            userId,
            tenantId);

        return Task.FromResult(FhirAuthorizationResult.Success("Local mode - all requests permitted"));
    }

    /// <inheritdoc />
    public Task<FhirAuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        int tenantId,
        string policyName,
        object? resource = null,
        CancellationToken cancellationToken = default)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? "anonymous";

        _logger.LogDebug(
            "Local authorization: Allowing policy {PolicyName} for user {UserId} in tenant {TenantId}",
            policyName,
            userId,
            tenantId);

        return Task.FromResult(FhirAuthorizationResult.Success($"Local mode - policy '{policyName}' permitted"));
    }
}
