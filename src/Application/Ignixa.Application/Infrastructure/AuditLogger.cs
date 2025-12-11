// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Local audit logger for tracking tenant access and security events.
/// Uses structured logging to enable querying and alerting on security events.
/// This is the default implementation used in Local provider mode.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(ILogger<AuditLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        if (authorized)
        {
            _logger.LogInformation(
                "AUDIT: Tenant access AUTHORIZED - User={UserId}, Tenant={TenantId}, " +
                "Operation={Operation}, Resource={ResourceType}/{ResourceId}",
                userId,
                tenantId,
                operation,
                resourceType,
                resourceId ?? "(none)");
        }
        else
        {
            _logger.LogWarning(
                "AUDIT: Tenant access DENIED - User={UserId}, Tenant={TenantId}, " +
                "Operation={Operation}, Resource={ResourceType}/{ResourceId}",
                userId,
                tenantId,
                operation,
                resourceType,
                resourceId ?? "(none)");
        }
    }

    /// <inheritdoc />
    public Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        if (auditEvent.Authorized)
        {
            _logger.LogInformation(
                "AUDIT: {Action} {Outcome} - User={UserId}, Tenant={TenantId}, " +
                "Resource={Resource}, CorrelationId={CorrelationId}",
                auditEvent.Action,
                auditEvent.Outcome,
                auditEvent.UserId,
                auditEvent.TenantId,
                auditEvent.Resource,
                auditEvent.CorrelationId ?? "(none)");
        }
        else
        {
            _logger.LogWarning(
                "AUDIT: {Action} DENIED - User={UserId}, Tenant={TenantId}, " +
                "Resource={Resource}, CorrelationId={CorrelationId}",
                auditEvent.Action,
                auditEvent.UserId,
                auditEvent.TenantId,
                auditEvent.Resource,
                auditEvent.CorrelationId ?? "(none)");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LogAsync(
        string userId,
        string action,
        string resource,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent
        {
            UserId = userId,
            Action = action,
            Resource = resource,
            Metadata = metadata ?? new Dictionary<string, string>()
        };

        return LogAsync(auditEvent, cancellationToken);
    }
}
