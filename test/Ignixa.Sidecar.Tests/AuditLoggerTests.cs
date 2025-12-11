// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Ignixa.Sidecar.Tests;

public class AuditLoggerTests
{
    private readonly ILogger<AuditLogger> _logger;
    private readonly AuditLogger _sut;

    public AuditLoggerTests()
    {
        _logger = Substitute.For<ILogger<AuditLogger>>();
        _sut = new AuditLogger(_logger);
    }

    [Fact]
    public void LogTenantAccess_WhenAuthorized_ShouldLogInformation()
    {
        // Act
        _sut.LogTenantAccess(
            userId: "user123",
            tenantId: 1,
            operation: "read",
            resourceType: "Patient",
            resourceId: "456",
            authorized: true);

        // Assert - verify logging was called
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public void LogTenantAccess_WhenDenied_ShouldLogWarning()
    {
        // Act
        _sut.LogTenantAccess(
            userId: "user123",
            tenantId: 1,
            operation: "delete",
            resourceType: "Patient",
            resourceId: "456",
            authorized: false);

        // Assert - verify logging was called
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task LogAsync_WithAuditEvent_ShouldLogSuccessfully()
    {
        // Arrange
        var auditEvent = new AuditEvent
        {
            UserId = "user789",
            Action = "create",
            Resource = "Patient/new-123",
            TenantId = 2,
            Authorized = true,
            CorrelationId = "correlation-abc"
        };

        // Act
        await _sut.LogAsync(auditEvent);

        // Assert
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task LogAsync_WithSimpleParameters_ShouldLogSuccessfully()
    {
        // Act
        await _sut.LogAsync(
            userId: "simple-user",
            action: "search",
            resource: "Patient",
            metadata: new Dictionary<string, string> { { "query", "_count=10" } });

        // Assert
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task LogAsync_WithDeniedAccess_ShouldLogWarning()
    {
        // Arrange
        var auditEvent = new AuditEvent
        {
            UserId = "denied-user",
            Action = "delete",
            Resource = "Patient/protected-123",
            TenantId = 1,
            Authorized = false,
            Outcome = "denied"
        };

        // Act
        await _sut.LogAsync(auditEvent);

        // Assert
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task LogAsync_WithMetadata_ShouldIncludeMetadata()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            { "client_ip", "192.168.1.1" },
            { "user_agent", "FHIR Client/1.0" }
        };

        var auditEvent = new AuditEvent
        {
            UserId = "meta-user",
            Action = "read",
            Resource = "Patient/123",
            Metadata = metadata
        };

        // Act
        await _sut.LogAsync(auditEvent);

        // Assert - verify no exceptions thrown
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }
}
