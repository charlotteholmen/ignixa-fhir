// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Application.Features.Experimental.Configuration;

/// <summary>
/// Configuration options for experimental features.
/// </summary>
public class ExperimentalOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Experimental";

    /// <summary>
    /// Master switch to enable experimental features.
    /// When false, no experimental features are loaded regardless of individual settings.
    /// Default: true (enabled by default in Docker image).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Individual feature configurations.
    /// </summary>
    public ExperimentalFeaturesOptions Features { get; set; } = new();
}

/// <summary>
/// Container for individual experimental feature options.
/// </summary>
public class ExperimentalFeaturesOptions
{
    /// <summary>
    /// MCP (Model Context Protocol) server configuration.
    /// </summary>
    public McpExperimentalOptions Mcp { get; set; } = new();

    /// <summary>
    /// $transform operation configuration.
    /// </summary>
    public TransformExperimentalOptions Transform { get; set; } = new();

    /// <summary>
    /// Terminology operations configuration ($expand, $translate, $subsumes).
    /// </summary>
    public TerminologyExperimentalOptions Terminology { get; set; } = new();

    /// <summary>
    /// Future: $summary operation configuration.
    /// </summary>
    public SummaryExperimentalOptions Summary { get; set; } = new();

    /// <summary>
    /// FHIR $graphql operation configuration.
    /// </summary>
    public GraphQlExperimentalOptions GraphQl { get; set; } = new();
}

/// <summary>
/// Configuration options for MCP (Model Context Protocol) server.
/// </summary>
public class McpExperimentalOptions
{
    /// <summary>
    /// Whether MCP server is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Transport protocol for MCP server (e.g., "http").
    /// </summary>
    public string Transport { get; set; } = "http";
}

/// <summary>
/// Configuration options for $transform operation.
/// </summary>
public class TransformExperimentalOptions
{
    /// <summary>
    /// Whether $transform operation is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Timeout in seconds for transformation operations.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Configuration options for terminology operations.
/// </summary>
public class TerminologyExperimentalOptions
{
    /// <summary>
    /// Whether terminology operations are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to automatically import terminology packages on startup.
    /// </summary>
    public bool EnableAutoImport { get; set; }
}

/// <summary>
/// Configuration options for $summary (IPS) operation.
/// </summary>
public class SummaryExperimentalOptions
{
    /// <summary>
    /// Whether $summary (IPS) operation is enabled.
    /// Default: true (enabled in experimental mode).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of resources to include in summary.
    /// </summary>
    public int MaxResources { get; set; } = 1000;

    /// <summary>
    /// Allowed resource types for summary.
    /// </summary>
    public ICollection<string> AllowedResourceTypes { get; } = [];
}

/// <summary>
/// Configuration options for FHIR $graphql operation.
/// </summary>
public class GraphQlExperimentalOptions
{
    /// <summary>Whether $graphql operation is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum allowed query nesting depth. Enforced by HotChocolate's depth rule.</summary>
    public int MaxQueryDepth { get; set; } = 15;

    /// <summary>Whether schema introspection (__schema, __type) is enabled. Disable in production.</summary>
    public bool EnableIntrospection { get; set; } = true;

    /// <summary>Maximum query complexity cost allowed per request.</summary>
    public int MaxQueryComplexity { get; set; } = 500;

    /// <summary>Maximum page size for list queries (hard cap; _count argument is clamped to this).</summary>
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>Default page size when _count argument is not specified.</summary>
    public int DefaultPageSize { get; set; } = 10;

    /// <summary>Whether GET transport is enabled. Disable in production to prevent query strings in access logs.</summary>
    public bool EnableGetRequests { get; set; } = true;

    /// <summary>Per-request execution timeout in seconds.</summary>
    public int ExecutionTimeoutSeconds { get; set; } = 30;

    /// <summary>FHIR versions to pre-build schemas for at startup. Empty collection disables warm-up.</summary>
    public ICollection<FhirVersion> WarmupVersions { get; } = [FhirVersion.R4];
}
