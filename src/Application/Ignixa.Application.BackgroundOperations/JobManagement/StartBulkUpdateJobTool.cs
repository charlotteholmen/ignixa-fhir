// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Application.BackgroundOperations.BulkUpdate;
using Ignixa.Application.Features.Experimental.Mcp.Authorization;
using Ignixa.Application.Features.Experimental.Mcp.Dtos;
using Ignixa.Application.Features.Experimental.Mcp.Tools;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Serialization.Models;
using Medino;
using ModelContextProtocol.Server;

namespace Ignixa.Application.BackgroundOperations.JobManagement;

/// <summary>
/// MCP tool for starting FHIR bulk update jobs using FHIR Patch semantics.
/// Applies replace or upsert operations to resources matching specified criteria.
/// Requires Mcp or Contributor role with update permission.
/// </summary>
[McpServerToolType]
public class StartBulkUpdateJobTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;

    public StartBulkUpdateJobTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator,
        IMcpAuthorizationService? mcpAuthorizationService = null)
        : base(fhirRequestContextAccessor, tenantStore, mcpAuthorizationService)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [McpServerTool(Name = "start_bulk_update_job")]
    [Description(@"Start a FHIR bulk update job using FHIR Patch semantics.
Apply replace or upsert operations to resources matching criteria.
Returns job ID for tracking progress via get_job_status tool.
Requires Mcp or Contributor role with update permission.
Example: resourceType='Patient', operations=[{type='replace', path='Patient.meta.tag', value={system='http://example.org', code='reviewed'}}]")]
    public async Task<JobSummaryDto> StartBulkUpdateJobAsync(
        [Description("Array of patch operations to apply. Each operation must have: type ('replace' or 'upsert'), path (FHIRPath expression), value (object to set), and optional name (element name for upsert).")]
        IReadOnlyList<BulkUpdateOperation> operations,

        [Description("Resource type to update (e.g., 'Patient', 'Observation'). Leave empty to apply to all resource types matching search query.")]
        string? resourceType = null,

        [Description("FHIR search parameters to filter resources (e.g., 'status=active&category=encounter-diagnosis'). Leave empty to update all resources of the specified type.")]
        string? searchQuery = null,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Validate MCP access and update permission
        await EnsureOperationAuthorizedAsync(McpOperationType.Update, resourceType: null, cancellationToken);

        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Build FHIR Parameters resource from operations list
        var patchParameters = BuildPatchParameters(operations);

        // Create bulk update job via handler
        var command = new CreateBulkUpdateJobCommand
        {
            TenantId = resolvedTenantId,
            ResourceType = resourceType,
            SearchQuery = searchQuery,
            PatchParameters = patchParameters
        };

        var result = await _mediator.SendAsync(command, cancellationToken);

        // Build progress description
        var resourceScope = !string.IsNullOrEmpty(resourceType) ? resourceType : "all resource types";
        var searchScope = !string.IsNullOrEmpty(searchQuery) ? $" matching '{searchQuery}'" : string.Empty;
        var progressDescription = $"Applying {operations.Count} operation(s) to {resourceScope}{searchScope}";

        return new JobSummaryDto
        {
            JobId = result.JobId,
            JobType = "BulkUpdate",
            Status = result.Status,
            ProgressPercentage = null,
            ProgressDescription = progressDescription,
            CreateDate = result.CreateDate,
            StartDate = null,
            EndDate = null,
            ErrorMessage = null
        };
    }

    /// <summary>
    /// Builds a FHIR Parameters resource from the operation list.
    /// Each operation becomes a parameter with name "operation" containing type, path, value, and optional name parts.
    /// </summary>
    private static ParametersJsonNode BuildPatchParameters(IReadOnlyList<BulkUpdateOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        if (operations.Count == 0)
        {
            throw new ArgumentException("Operations list cannot be empty", nameof(operations));
        }

        var parametersJson = new JsonObject
        {
            ["resourceType"] = "Parameters",
            ["parameter"] = new JsonArray()
        };

        if (parametersJson["parameter"] is not JsonArray parameterArray)
        {
            throw new InvalidOperationException("Failed to initialize parameter array");
        }

        foreach (var op in operations)
        {
            if (string.IsNullOrWhiteSpace(op.Type))
            {
                throw new ArgumentException("Operation 'type' is required and cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(op.Path))
            {
                throw new ArgumentException("Operation 'path' is required and cannot be empty");
            }

            // Map operation type to FHIR Patch operation type
            var operationType = op.Type switch
            {
                var t when t.Equals("replace", StringComparison.OrdinalIgnoreCase) => "replace",
                var t when t.Equals("upsert", StringComparison.OrdinalIgnoreCase) => "add",
                var t when t.Equals("add", StringComparison.OrdinalIgnoreCase) => "add",
                _ => throw new ArgumentException($"Invalid operation type '{op.Type}'. Must be 'replace' or 'upsert'.")
            };

            var operationParam = new JsonObject
            {
                ["name"] = "operation",
                ["part"] = new JsonArray()
            };

            if (operationParam["part"] is not JsonArray partArray)
            {
                throw new InvalidOperationException("Failed to initialize part array");
            }

            // Add type part
            partArray.Add(new JsonObject
            {
                ["name"] = "type",
                ["valueCode"] = operationType
            });

            // Add path part
            partArray.Add(new JsonObject
            {
                ["name"] = "path",
                ["valueString"] = op.Path
            });

            // Add value part with proper FHIR value[x] typing
            var valuePart = CreateValuePart(op.Value);
            partArray.Add(valuePart);

            // Add optional name part for upsert operations
            if (!string.IsNullOrWhiteSpace(op.Name))
            {
                partArray.Add(new JsonObject
                {
                    ["name"] = "name",
                    ["valueString"] = op.Name
                });
            }

            parameterArray.Add(operationParam);
        }

        return new ParametersJsonNode(parametersJson);
    }

    /// <summary>
    /// Creates a parameter part with proper FHIR value[x] typing.
    /// The parser looks for properties starting with "value" (e.g., valueString, valueCode, valueCoding).
    /// For complex objects, serialize to JSON and embed as the appropriate value[x] type.
    /// </summary>
    private static JsonObject CreateValuePart(object? value)
    {
        var part = new JsonObject { ["name"] = "value" };

        if (value == null)
        {
            // For null values, use valueString with empty string
            part["valueString"] = string.Empty;
            return part;
        }

        // Handle JsonNode directly (already serialized)
        if (value is JsonNode jsonNode)
        {
            // If it's a JsonObject with FHIR-specific properties, determine type
            if (jsonNode is JsonObject jsonObj)
            {
                // Coding: has system and/or code
                if (jsonObj.ContainsKey("system") || jsonObj.ContainsKey("code"))
                {
                    part["valueCoding"] = jsonNode;
                    return part;
                }

                // CodeableConcept: has coding or text
                if (jsonObj.ContainsKey("coding") || jsonObj.ContainsKey("text"))
                {
                    part["valueCodeableConcept"] = jsonNode;
                    return part;
                }

                // Default: serialize as generic JSON and use valueString
                part["valueString"] = jsonNode.ToJsonString();
                return part;
            }

            // For arrays or primitives in JsonNode, serialize to string
            part["valueString"] = jsonNode.ToJsonString();
            return part;
        }

        // Handle primitive types
        if (value is string str)
        {
            part["valueString"] = str;
            return part;
        }

        if (value is int intVal)
        {
            part["valueInteger"] = intVal;
            return part;
        }

        if (value is bool boolVal)
        {
            part["valueBoolean"] = boolVal;
            return part;
        }

        if (value is decimal decVal)
        {
            part["valueDecimal"] = decVal;
            return part;
        }

        // For complex objects, serialize to JSON and determine type
        var json = JsonSerializer.Serialize(value);
        var node = JsonNode.Parse(json);

        if (node is JsonObject obj)
        {
            // Check for FHIR types
            if (obj.ContainsKey("system") || obj.ContainsKey("code"))
            {
                part["valueCoding"] = node;
                return part;
            }

            if (obj.ContainsKey("coding") || obj.ContainsKey("text"))
            {
                part["valueCodeableConcept"] = node;
                return part;
            }

            // Generic object - serialize as string
            part["valueString"] = json;
            return part;
        }

        // Fallback to string serialization
        part["valueString"] = json;
        return part;
    }
}

/// <summary>
/// Represents a single bulk update operation for MCP tool input.
/// </summary>
public class BulkUpdateOperation
{
    /// <summary>
    /// Type of patch operation: "replace" or "upsert".
    /// </summary>
    [Description("Type of operation: 'replace' (update existing) or 'upsert' (add if missing, update if exists)")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// FHIRPath expression identifying the element(s) to modify.
    /// </summary>
    [Description("FHIRPath expression (e.g., 'Patient.meta.tag', 'Observation.status', 'Condition.category')")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Optional name for the element being added (used with upsert operations).
    /// </summary>
    [Description("Optional element name for upsert operations")]
    public string? Name { get; set; }

    /// <summary>
    /// The value to set at the specified path.
    /// </summary>
    [Description("Value to set (can be primitive, object, or array)")]
    public object? Value { get; set; }
}
