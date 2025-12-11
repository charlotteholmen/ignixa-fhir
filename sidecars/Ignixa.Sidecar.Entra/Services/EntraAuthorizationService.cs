// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Grpc.Core;
using Ignixa.Sidecar.Grpc;
using Microsoft.Extensions.Options;

namespace Ignixa.Sidecar.Entra.Services;

/// <summary>
/// gRPC authorization service that validates requests against Entra ID claims.
/// This service runs as a sidecar and receives authorization requests from the main FHIR server.
/// </summary>
public class EntraAuthorizationService : AuthorizationService.AuthorizationServiceBase
{
    private readonly EntraAuthorizationOptions _options;
    private readonly ILogger<EntraAuthorizationService> _logger;

    public EntraAuthorizationService(
        IOptions<EntraAuthorizationOptions> options,
        ILogger<EntraAuthorizationService> logger)
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

        // Extract claims from the request
        var userRoles = GetUserRoles(request.Claims);
        var userScopes = GetUserScopes(request.Claims);

        // Check required roles (if configured)
        if (_options.RequiredRoles.Count > 0)
        {
            var hasRequiredRole = _options.RequiredRoles.Any(r => userRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
            if (!hasRequiredRole)
            {
                _logger.LogWarning(
                    "User {UserId} denied: missing required role. Has: [{UserRoles}], Required: [{RequiredRoles}]",
                    request.UserId,
                    string.Join(", ", userRoles),
                    string.Join(", ", _options.RequiredRoles));

                return Task.FromResult(new AuthorizationResult
                {
                    IsAuthorized = false,
                    Reason = $"Missing required role. Required one of: {string.Join(", ", _options.RequiredRoles)}"
                });
            }
        }

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

        // Check action-based role mapping
        if (_options.ActionRoleMapping.TryGetValue(request.Action, out var actionRoles) && actionRoles.Count > 0)
        {
            var hasActionRole = actionRoles.Any(r => userRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
            if (!hasActionRole)
            {
                _logger.LogWarning(
                    "User {UserId} denied: missing role for action {Action}",
                    request.UserId,
                    request.Action);

                return Task.FromResult(new AuthorizationResult
                {
                    IsAuthorized = false,
                    Reason = $"Missing role for action '{request.Action}'. Required one of: {string.Join(", ", actionRoles)}"
                });
            }
        }

        // Check resource type-based role mapping
        var resourceType = ExtractResourceType(request.Resource);
        if (!string.IsNullOrEmpty(resourceType) &&
            _options.ResourceTypeRoleMapping.TryGetValue(resourceType, out var resourceRoles) &&
            resourceRoles.Count > 0)
        {
            var hasResourceRole = resourceRoles.Any(r => userRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
            if (!hasResourceRole)
            {
                _logger.LogWarning(
                    "User {UserId} denied: missing role for resource type {ResourceType}",
                    request.UserId,
                    resourceType);

                return Task.FromResult(new AuthorizationResult
                {
                    IsAuthorized = false,
                    Reason = $"Missing role for resource type '{resourceType}'. Required one of: {string.Join(", ", resourceRoles)}"
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

    private static List<string> GetUserRoles(IDictionary<string, string> claims)
    {
        var roles = new List<string>();

        // Check for roles claim (Entra ID uses "roles" for app roles)
        if (claims.TryGetValue("roles", out var rolesValue) && !string.IsNullOrEmpty(rolesValue))
        {
            // Roles may be comma-separated or JSON array
            if (rolesValue.StartsWith('['))
            {
                // Try to parse as JSON array
                try
                {
                    var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(rolesValue);
                    if (parsed != null)
                    {
                        roles.AddRange(parsed);
                    }
                }
                catch
                {
                    // Fall back to splitting
                    roles.AddRange(rolesValue.Trim('[', ']').Split(',').Select(r => r.Trim().Trim('"')));
                }
            }
            else
            {
                roles.AddRange(rolesValue.Split(',').Select(r => r.Trim()));
            }
        }

        // Also check role claim (singular)
        if (claims.TryGetValue("role", out var roleValue) && !string.IsNullOrEmpty(roleValue))
        {
            roles.Add(roleValue);
        }

        // Check for wids claim (Entra ID directory roles)
        if (claims.TryGetValue("wids", out var widsValue) && !string.IsNullOrEmpty(widsValue))
        {
            roles.AddRange(widsValue.Split(',').Select(r => r.Trim()));
        }

        return roles.Distinct().ToList();
    }

    private static List<string> GetUserScopes(IDictionary<string, string> claims)
    {
        var scopes = new List<string>();

        // Check for scp claim (delegated permissions/scopes)
        if (claims.TryGetValue("scp", out var scpValue) && !string.IsNullOrEmpty(scpValue))
        {
            scopes.AddRange(scpValue.Split(' ').Select(s => s.Trim()));
        }

        return scopes.Distinct().ToList();
    }

    private static string ExtractResourceType(string resource)
    {
        if (string.IsNullOrEmpty(resource))
        {
            return string.Empty;
        }

        // Resource format: "ResourceType" or "ResourceType/id"
        var slashIndex = resource.IndexOf('/', StringComparison.Ordinal);
        return slashIndex > 0 ? resource[..slashIndex] : resource;
    }
}
