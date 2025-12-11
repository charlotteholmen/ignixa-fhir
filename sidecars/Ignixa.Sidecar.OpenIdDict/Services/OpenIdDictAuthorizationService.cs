// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Grpc.Core;
using Ignixa.Sidecar.Grpc;
using Microsoft.Extensions.Options;

namespace Ignixa.Sidecar.OpenIdDict.Services;

/// <summary>
/// gRPC authorization service for local development and testing.
/// Validates requests based on OAuth scopes from OpenIdDict tokens.
/// </summary>
public class OpenIdDictAuthorizationService : AuthorizationService.AuthorizationServiceBase
{
    private readonly OpenIdDictAuthorizationOptions _options;
    private readonly ILogger<OpenIdDictAuthorizationService> _logger;

    public OpenIdDictAuthorizationService(
        IOptions<OpenIdDictAuthorizationOptions> options,
        ILogger<OpenIdDictAuthorizationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public override Task<AuthorizationResult> Authorize(
        AuthorizationRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "Authorization request: User={UserId}, Resource={Resource}, Action={Action}, TenantId={TenantId}",
            request.UserId,
            request.Resource,
            request.Action,
            request.TenantId);

        // Extract scopes from the request claims
        var userScopes = GetUserScopes(request.Claims);

        // Check required scopes (if configured)
        if (_options.RequiredScopes.Count > 0)
        {
            var hasRequiredScope = _options.RequiredScopes.Any(s => userScopes.Contains(s, StringComparer.OrdinalIgnoreCase));
            if (!hasRequiredScope)
            {
                _logger.LogWarning(
                    "User {UserId} denied: missing required scope. Has: [{UserScopes}], Required: [{RequiredScopes}]",
                    request.UserId,
                    string.Join(", ", userScopes),
                    string.Join(", ", _options.RequiredScopes));

                return Task.FromResult(new AuthorizationResult
                {
                    IsAuthorized = false,
                    Reason = $"Missing required scope. Required one of: {string.Join(", ", _options.RequiredScopes)}"
                });
            }
        }

        // Check action-based scope mapping
        if (_options.ActionScopeMapping.TryGetValue(request.Action, out var actionScopes) && actionScopes.Count > 0)
        {
            var hasActionScope = actionScopes.Any(s => userScopes.Contains(s, StringComparer.OrdinalIgnoreCase));
            if (!hasActionScope)
            {
                _logger.LogWarning(
                    "User {UserId} denied: missing scope for action {Action}",
                    request.UserId,
                    request.Action);

                return Task.FromResult(new AuthorizationResult
                {
                    IsAuthorized = false,
                    Reason = $"Missing scope for action '{request.Action}'. Required one of: {string.Join(", ", actionScopes)}"
                });
            }
        }

        // If we get here and AllowAuthenticatedByDefault is true, authorize
        if (_options.AllowAuthenticatedByDefault)
        {
            _logger.LogDebug("User {UserId} authorized (authenticated user default)", request.UserId);
            return Task.FromResult(new AuthorizationResult
            {
                IsAuthorized = true,
                Reason = "Authenticated user authorized"
            });
        }

        // Deny by default if no rules matched and AllowAuthenticatedByDefault is false
        _logger.LogWarning("User {UserId} denied: no authorization rules matched", request.UserId);
        return Task.FromResult(new AuthorizationResult
        {
            IsAuthorized = false,
            Reason = "No authorization rules matched"
        });
    }

    private static List<string> GetUserScopes(IDictionary<string, string> claims)
    {
        var scopes = new List<string>();

        // Check for scope claim (OAuth standard)
        if (claims.TryGetValue("scope", out var scopeValue) && !string.IsNullOrEmpty(scopeValue))
        {
            scopes.AddRange(scopeValue.Split(' ').Select(s => s.Trim()));
        }

        // Also check scp claim (common in JWT tokens)
        if (claims.TryGetValue("scp", out var scpValue) && !string.IsNullOrEmpty(scpValue))
        {
            scopes.AddRange(scpValue.Split(' ').Select(s => s.Trim()));
        }

        return scopes.Distinct().ToList();
    }
}
