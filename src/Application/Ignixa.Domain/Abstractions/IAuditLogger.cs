// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Audit logger for tracking tenant access and security events.
/// Implementations may delegate to local logging, external sidecars, or other audit systems.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Logs a tenant access attempt (authorized or unauthorized).
    /// </summary>
    /// <param name="userId">User identifier (from claims)</param>
    /// <param name="tenantId">Tenant identifier being accessed</param>
    /// <param name="operation">Operation type (GET, PUT, DELETE, SEARCH, etc.)</param>
    /// <param name="resourceType">FHIR resource type</param>
    /// <param name="resourceId">Resource ID (if applicable)</param>
    /// <param name="authorized">Whether the access was authorized</param>
    void LogTenantAccess(
        string userId,
        int tenantId,
        string operation,
        string resourceType,
        string? resourceId,
        bool authorized);

    /// <summary>
    /// Logs an audit event asynchronously.
    /// </summary>
    /// <param name="auditEvent">The audit event to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs an audit event asynchronously with explicit parameters.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="action">Action performed.</param>
    /// <param name="resource">Resource affected.</param>
    /// <param name="metadata">Additional metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    Task LogAsync(
        string userId,
        string action,
        string resource,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an audit event with full context for logging.
/// </summary>
public record AuditEvent
{
    /// <summary>
    /// User identifier (from claims).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Action performed (e.g., "read", "write", "delete", "search").
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// Resource affected (e.g., "Patient/123").
    /// </summary>
    public required string Resource { get; init; }

    /// <summary>
    /// Timestamp of the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Tenant identifier.
    /// </summary>
    public int TenantId { get; init; }

    /// <summary>
    /// Resource type (e.g., "Patient", "Observation").
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Whether the operation was authorized.
    /// </summary>
    public bool Authorized { get; init; } = true;

    /// <summary>
    /// Outcome of the operation (e.g., "success", "failure", "error").
    /// </summary>
    public string Outcome { get; init; } = "success";

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
