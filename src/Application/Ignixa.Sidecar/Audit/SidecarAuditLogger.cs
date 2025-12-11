// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Ignixa.Domain.Abstractions;
using Ignixa.Sidecar.Configuration;
using Ignixa.Sidecar.Grpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Sidecar.Audit;

/// <summary>
/// Sidecar-based audit logger that delegates audit events to an external
/// sidecar container via gRPC.
/// </summary>
public class SidecarAuditLogger : IAuditLogger, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly AuditLoggerService.AuditLoggerServiceClient _client;
    private readonly SidecarOptions _options;
    private readonly ILogger<SidecarAuditLogger> _logger;
    private readonly IAuditLogger? _fallbackLogger;
    private bool _disposed;

    public SidecarAuditLogger(
        IOptions<SidecarOptions> options,
        ILogger<SidecarAuditLogger> logger,
        IAuditLogger? fallbackLogger = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fallbackLogger = fallbackLogger;

        _channel = GrpcChannel.ForAddress(_options.Endpoint);
        _client = new AuditLoggerService.AuditLoggerServiceClient(_channel);

        _logger.LogInformation(
            "Sidecar audit logger initialized with endpoint: {Endpoint}",
            _options.Endpoint);
    }

    /// <inheritdoc />
    public void LogTenantAccess(
        string userId,
        int tenantId,
        string operation,
        string resourceType,
        string? resourceId,
        bool authorized)
    {
        var auditEvent = new Domain.Abstractions.AuditEvent
        {
            UserId = userId,
            TenantId = tenantId,
            Action = operation,
            ResourceType = resourceType,
            Resource = string.IsNullOrEmpty(resourceId) ? resourceType : $"{resourceType}/{resourceId}",
            Authorized = authorized,
            Outcome = authorized ? "success" : "denied"
        };

        // Fire and forget - don't block the request
        _ = LogAsync(auditEvent);
    }

    /// <inheritdoc />
    public async Task LogAsync(Domain.Abstractions.AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        try
        {
            var grpcEvent = new Grpc.AuditEvent
            {
                UserId = auditEvent.UserId,
                Action = auditEvent.Action,
                Resource = auditEvent.Resource,
                Timestamp = Timestamp.FromDateTimeOffset(auditEvent.Timestamp),
                CorrelationId = auditEvent.CorrelationId ?? string.Empty,
                TenantId = auditEvent.TenantId,
                ResourceType = auditEvent.ResourceType ?? string.Empty,
                Authorized = auditEvent.Authorized,
                Outcome = auditEvent.Outcome
            };

            foreach (var kvp in auditEvent.Metadata)
            {
                grpcEvent.Metadata[kvp.Key] = kvp.Value;
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(_options.TimeoutMs);
            var callOptions = new CallOptions(deadline: deadline, cancellationToken: cancellationToken);

            var response = await _client.LogAuditEventAsync(grpcEvent, callOptions);

            if (!response.Success)
            {
                _logger.LogWarning(
                    "Sidecar audit logging failed: {ErrorMessage}",
                    response.ErrorMessage);

                // Fall back to local logging
                await FallbackLogAsync(auditEvent, cancellationToken);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable ||
                                       ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            _logger.LogWarning(
                ex,
                "Sidecar audit service unavailable, falling back to local logging");

            await FallbackLogAsync(auditEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling sidecar audit service");
            await FallbackLogAsync(auditEvent, cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task LogAsync(
        string userId,
        string action,
        string resource,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var auditEvent = new Domain.Abstractions.AuditEvent
        {
            UserId = userId,
            Action = action,
            Resource = resource,
            Metadata = metadata ?? new Dictionary<string, string>()
        };

        return LogAsync(auditEvent, cancellationToken);
    }

    private Task FallbackLogAsync(Domain.Abstractions.AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (_fallbackLogger != null)
        {
            return _fallbackLogger.LogAsync(auditEvent, cancellationToken);
        }

        // If no fallback logger, just log to our own logger
        _logger.LogInformation(
            "AUDIT (fallback): {Action} - User={UserId}, Resource={Resource}",
            auditEvent.Action,
            auditEvent.UserId,
            auditEvent.Resource);

        return Task.CompletedTask;
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
