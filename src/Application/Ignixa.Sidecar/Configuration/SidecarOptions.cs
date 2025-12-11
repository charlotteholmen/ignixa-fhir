// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Sidecar.Configuration;

/// <summary>
/// Configuration options for the sidecar provider.
/// </summary>
public class SidecarOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Sidecar";

    /// <summary>
    /// The provider mode (Local, Sidecar, or Hybrid).
    /// Default: Local for development-friendly defaults.
    /// </summary>
    public ProviderMode ProviderMode { get; set; } = ProviderMode.Local;

    /// <summary>
    /// The gRPC endpoint for the sidecar container.
    /// Required when ProviderMode is Sidecar.
    /// Default: http://localhost:5050
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:5050";

    /// <summary>
    /// Timeout in milliseconds for sidecar calls.
    /// Default: 5000ms (5 seconds).
    /// </summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Number of retry attempts for failed sidecar calls.
    /// Default: 3 retries.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Whether to fail-open (allow access) when the sidecar is unavailable.
    /// Default: false (fail-closed for security).
    /// </summary>
    public bool FailOpen { get; set; }

    /// <summary>
    /// Circuit breaker configuration for sidecar calls.
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>
    /// Hybrid mode configuration for individual service overrides.
    /// </summary>
    public HybridOptions Hybrid { get; set; } = new();
}

/// <summary>
/// Circuit breaker configuration options.
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Number of failures before opening the circuit.
    /// Default: 5 failures.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in seconds for sampling failures.
    /// Default: 30 seconds.
    /// </summary>
    public int SamplingDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum number of requests required before the circuit can open.
    /// Default: 10 requests.
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Duration in seconds the circuit stays open before half-open state.
    /// Default: 30 seconds.
    /// </summary>
    public int BreakDurationSeconds { get; set; } = 30;
}

/// <summary>
/// Hybrid mode configuration for individual service provider selection.
/// </summary>
public class HybridOptions
{
    /// <summary>
    /// Provider for authorization service.
    /// Default: Local.
    /// </summary>
    public ProviderMode Authorization { get; set; } = ProviderMode.Local;

    /// <summary>
    /// Provider for audit logging service.
    /// Default: Local.
    /// </summary>
    public ProviderMode AuditLogging { get; set; } = ProviderMode.Local;

    /// <summary>
    /// Provider for logging service.
    /// Default: Local.
    /// </summary>
    public ProviderMode Logging { get; set; } = ProviderMode.Local;
}
