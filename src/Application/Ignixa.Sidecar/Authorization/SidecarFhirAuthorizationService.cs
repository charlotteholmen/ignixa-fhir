// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using Grpc.Core;
using Grpc.Net.Client;
using Ignixa.Domain.Abstractions;
using Ignixa.Sidecar.Configuration;
using Ignixa.Sidecar.Grpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Sidecar.Authorization;

/// <summary>
/// Sidecar-based authorization service that delegates authorization decisions
/// to an external sidecar container via gRPC.
/// </summary>
public class SidecarFhirAuthorizationService : IFhirAuthorizationService, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly AuthorizationService.AuthorizationServiceClient _client;
    private readonly SidecarOptions _options;
    private readonly ILogger<SidecarFhirAuthorizationService> _logger;
    private bool _disposed;

    public SidecarFhirAuthorizationService(
        IOptions<SidecarOptions> options,
        ILogger<SidecarFhirAuthorizationService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _channel = GrpcChannel.ForAddress(_options.Endpoint);
        _client = new AuthorizationService.AuthorizationServiceClient(_channel);

        _logger.LogInformation(
            "Sidecar authorization service initialized with endpoint: {Endpoint}",
            _options.Endpoint);
    }

    /// <inheritdoc />
    public async Task<FhirAuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        int tenantId,
        string resourceType,
        string? resourceId,
        string action,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user);
        var resource = string.IsNullOrEmpty(resourceId)
            ? resourceType
            : $"{resourceType}/{resourceId}";

        var request = new AuthorizationRequest
        {
            UserId = userId,
            TenantId = tenantId,
            Resource = resource,
            Action = action
        };

        // Add user claims to the request
        foreach (var claim in user.Claims)
        {
            if (!request.Claims.ContainsKey(claim.Type))
            {
                request.Claims[claim.Type] = claim.Value;
            }
        }

        return await CallSidecarAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FhirAuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        int tenantId,
        string policyName,
        object? resource = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user);

        var request = new AuthorizationRequest
        {
            UserId = userId,
            TenantId = tenantId,
            PolicyName = policyName,
            Resource = resource?.ToString() ?? string.Empty,
            Action = "policy"
        };

        // Add user claims to the request
        foreach (var claim in user.Claims)
        {
            if (!request.Claims.ContainsKey(claim.Type))
            {
                request.Claims[claim.Type] = claim.Value;
            }
        }

        return await CallSidecarAsync(request, cancellationToken);
    }

    private async Task<FhirAuthorizationResult> CallSidecarAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(_options.TimeoutMs);
            var callOptions = new CallOptions(deadline: deadline, cancellationToken: cancellationToken);

            var response = await _client.AuthorizeAsync(request, callOptions);

            _logger.LogDebug(
                "Sidecar authorization result: Authorized={IsAuthorized}, Reason={Reason}",
                response.IsAuthorized,
                response.Reason);

            var result = new FhirAuthorizationResult
            {
                IsAuthorized = response.IsAuthorized,
                Reason = response.Reason
            };

            foreach (var kvp in response.Context)
            {
                result.Context[kvp.Key] = kvp.Value;
            }

            return result;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable ||
                                       ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            _logger.LogWarning(
                ex,
                "Sidecar authorization service unavailable. FailOpen={FailOpen}",
                _options.FailOpen);

            if (_options.FailOpen)
            {
                return FhirAuthorizationResult.Success("Sidecar unavailable - fail-open mode");
            }

            return FhirAuthorizationResult.Failure($"Authorization service unavailable: {ex.Status.Detail}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling sidecar authorization service");

            if (_options.FailOpen)
            {
                return FhirAuthorizationResult.Success("Sidecar error - fail-open mode");
            }

            return FhirAuthorizationResult.Failure($"Authorization error: {ex.Message}");
        }
    }

    private static string GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("oid")?.Value
            ?? "anonymous";
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _channel.Dispose();
            }
            _disposed = true;
        }
    }
}
