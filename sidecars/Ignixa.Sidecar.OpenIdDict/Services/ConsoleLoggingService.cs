// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Grpc.Core;
using Ignixa.Sidecar.Grpc;
using GrpcLogLevel = Ignixa.Sidecar.Grpc.LogLevel;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Ignixa.Sidecar.OpenIdDict.Services;

/// <summary>
/// gRPC logging service that writes log entries to console/structured logging.
/// </summary>
public class ConsoleLoggingService : LoggingService.LoggingServiceBase
{
    private readonly ILogger<ConsoleLoggingService> _logger;

    public ConsoleLoggingService(ILogger<ConsoleLoggingService> logger)
    {
        _logger = logger;
    }

    public override Task<LogResponse> Log(LogEntry request, ServerCallContext context)
    {
        LogEntryInternal(request);

        return Task.FromResult(new LogResponse
        {
            Success = true,
            EntriesProcessed = 1
        });
    }

    public override Task<LogResponse> LogBatch(LogBatchRequest request, ServerCallContext context)
    {
        foreach (var entry in request.Entries)
        {
            LogEntryInternal(entry);
        }

        return Task.FromResult(new LogResponse
        {
            Success = true,
            EntriesProcessed = request.Entries.Count
        });
    }

    private void LogEntryInternal(LogEntry entry)
    {
        var logLevel = entry.Level switch
        {
            GrpcLogLevel.Trace => MsLogLevel.Trace,
            GrpcLogLevel.Debug => MsLogLevel.Debug,
            GrpcLogLevel.Information => MsLogLevel.Information,
            GrpcLogLevel.Warning => MsLogLevel.Warning,
            GrpcLogLevel.Error => MsLogLevel.Error,
            GrpcLogLevel.Critical => MsLogLevel.Critical,
            _ => MsLogLevel.Information
        };

        _logger.Log(
            logLevel,
            "[{Category}] {Message} - Tenant={TenantId}, CorrelationId={CorrelationId}",
            entry.Category,
            entry.Message,
            entry.TenantId,
            entry.CorrelationId);
    }
}
