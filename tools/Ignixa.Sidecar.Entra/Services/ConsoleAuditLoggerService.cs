// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Grpc.Core;
using Ignixa.Sidecar.Grpc;

namespace Ignixa.Sidecar.Entra.Services;

/// <summary>
/// gRPC audit logger service that writes audit events to console/structured logging.
/// In production, this can be extended to write to Azure Monitor, Log Analytics, etc.
/// </summary>
public class ConsoleAuditLoggerService : AuditLoggerService.AuditLoggerServiceBase
{
    private readonly ILogger<ConsoleAuditLoggerService> _logger;

    public ConsoleAuditLoggerService(ILogger<ConsoleAuditLoggerService> logger)
    {
        _logger = logger;
    }

    public override Task<AuditResponse> LogAuditEvent(AuditEvent request, ServerCallContext context)
    {
        LogAuditEventInternal(request);

        return Task.FromResult(new AuditResponse
        {
            Success = true,
            EventsProcessed = 1
        });
    }

    public override Task<AuditResponse> LogAuditEventBatch(AuditEventBatch request, ServerCallContext context)
    {
        foreach (var auditEvent in request.Events)
        {
            LogAuditEventInternal(auditEvent);
        }

        return Task.FromResult(new AuditResponse
        {
            Success = true,
            EventsProcessed = request.Events.Count
        });
    }

    private void LogAuditEventInternal(AuditEvent auditEvent)
    {
        if (auditEvent.Authorized)
        {
            _logger.LogInformation(
                "AUDIT: {Action} {Outcome} - User={UserId}, Tenant={TenantId}, Resource={Resource}, CorrelationId={CorrelationId}",
                auditEvent.Action,
                auditEvent.Outcome,
                auditEvent.UserId,
                auditEvent.TenantId,
                auditEvent.Resource,
                auditEvent.CorrelationId);
        }
        else
        {
            _logger.LogWarning(
                "AUDIT: {Action} DENIED - User={UserId}, Tenant={TenantId}, Resource={Resource}, CorrelationId={CorrelationId}",
                auditEvent.Action,
                auditEvent.UserId,
                auditEvent.TenantId,
                auditEvent.Resource,
                auditEvent.CorrelationId);
        }
    }
}
