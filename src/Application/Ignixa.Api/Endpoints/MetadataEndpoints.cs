// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Mvc;
using Ignixa.Abstractions;
using Ignixa.Api.Extensions;
using Ignixa.Api.Http;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for FHIR metadata (CapabilityStatement).
/// Supports both tenant-agnostic (/metadata) and tenant-explicit (/tenant/{tenantId}/metadata) routes.
/// </summary>
public static class MetadataEndpoints
{
    public static IEndpointRouteBuilder MapMetadataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMetadataTenantEndpoints();
        endpoints.MapMetadataAgnosticEndpoints();
        endpoints.MapVersionsEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit FHIR metadata endpoints (/tenant/{tenantId}/metadata).
    /// Always supported in all multi-tenancy scenarios.
    /// </summary>
    public static IEndpointRouteBuilder MapMetadataTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-explicit route: GET /tenant/{tenantId}/metadata
        endpoints.MapGet("/tenant/{tenantId:int}/metadata", HandleGetTenantMetadata)
            .WithName("GetTenantMetadata")
            .Produces(StatusCodes.Status200OK, contentType: KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic FHIR metadata endpoints (/metadata).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// </summary>
    public static IEndpointRouteBuilder MapMetadataAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-agnostic route: GET /metadata
        endpoints.MapGet("/metadata", HandleGetMetadata)
            .WithName("GetMetadata")
            .Produces(StatusCodes.Status200OK, contentType: KnownContentTypes.ApplicationFhirJson);

        return endpoints;
    }

    /// <summary>
    /// GET /metadata
    /// Returns the FHIR server's capability statement (tenant-agnostic).
    /// In multi-tenant scenarios, returns system-wide capabilities.
    /// In single-tenant scenarios, returns the single tenant's capabilities.
    /// </summary>
    private static async Task<IResult> HandleGetMetadata(
        HttpContext context,
        [FromServices] IMediator mediator,
        [FromServices] ITenantConfigurationStore configStore,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(MetadataEndpoints).FullName!);
        logger.LogInformation("GET /metadata (tenant-agnostic)");

        // Validate Accept header for content negotiation
        ValidateAcceptHeader(context);

        // Resolve tenant ID: For /metadata endpoint, the TenantResolutionMiddleware doesn't
        // auto-detect because it's not classified as a resource endpoint. We need to manually
        // perform the same auto-detection logic here.
        int? tenantId = null;

        // First check if middleware already resolved it (though unlikely for /metadata)
        if (context.Items.TryGetValue("TenantId", out var tenantIdObj) &&
            tenantIdObj is int resolvedTenantId)
        {
            tenantId = resolvedTenantId;
            logger.LogDebug("Tenant auto-detected by middleware: {TenantId}", tenantId);
        }

        var query = new GetCapabilityStatementQuery(tenantId);
        var capabilityStatement = await mediator.SendAsync(query, cancellationToken);

        return FhirResults.Ok(capabilityStatement, context);
    }

    /// <summary>
    /// GET /tenant/{tenantId}/metadata
    /// Returns the FHIR server's capability statement for a specific tenant.
    /// </summary>
    private static async Task<IResult> HandleGetTenantMetadata(
        HttpContext context,
        int tenantId,
        [FromServices] IMediator mediator,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(MetadataEndpoints).FullName!);
        logger.LogInformation("GET /tenant/{TenantId}/metadata", tenantId);

        // Validate Accept header for content negotiation
        ValidateAcceptHeader(context);

        // TenantResolutionMiddleware already validated the tenant exists and is active
        // The tenantId is stored in HttpContext.Items

        var query = new GetCapabilityStatementQuery(tenantId);
        var capabilityStatement = await mediator.SendAsync(query, cancellationToken);

        return FhirResults.Ok(capabilityStatement, context);
    }

    /// <summary>
    /// Validates the Accept header for FHIR-compliant content negotiation.
    /// Throws NotAcceptableException if the Accept header specifies unsupported media types.
    /// </summary>
    private static void ValidateAcceptHeader(HttpContext context)
    {
        const string fhirJsonMediaType = KnownContentTypes.ApplicationFhirJson;
        const string jsonMediaType = KnownContentTypes.ApplicationJson;

        var acceptHeader = context.Request.Headers.Accept.ToString();

        if (string.IsNullOrWhiteSpace(acceptHeader))
        {
            // Accept header is optional; if not provided, default to KnownContentTypes.ApplicationFhirJson
            return;
        }

        // Parse Accept header values (may contain multiple values separated by comma)
        var acceptedTypes = acceptHeader.Split(',')
            .Select(t => t.Trim())
            .Select(t => t.Split(';')[0].Trim()) // Remove quality factors and parameters
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        // Check if any accepted type is supported
        bool hasAcceptableType = acceptedTypes.Any(t =>
            t.Equals(fhirJsonMediaType, StringComparison.OrdinalIgnoreCase) ||
            t.Equals(jsonMediaType, StringComparison.OrdinalIgnoreCase) ||
            t.Equals("*/*", StringComparison.OrdinalIgnoreCase));

        if (!hasAcceptableType)
        {
            // No acceptable media type found
            throw new NotAcceptableException(
                $"The server cannot produce content matching the requested Accept header: '{acceptHeader}'. " +
                $"Supported media types: KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson");
        }
    }

    /// <summary>
    /// Registers $versions endpoints for listing supported FHIR versions.
    /// https://build.fhir.org/capabilitystatement-operation-versions.html
    /// </summary>
    private static IEndpointRouteBuilder MapVersionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-agnostic route: GET /$versions
        endpoints.MapGet("/$versions", HandleGetVersions)
            .WithName("GetVersions")
            .Produces(StatusCodes.Status200OK, contentType: KnownContentTypes.ApplicationFhirJson);

        // Tenant-explicit route: GET /tenant/{tenantId}/$versions
        endpoints.MapGet("/tenant/{tenantId:int}/$versions", HandleGetTenantVersions)
            .WithName("GetTenantVersions")
            .Produces(StatusCodes.Status200OK, contentType: KnownContentTypes.ApplicationFhirJson);

        return endpoints;
    }

    /// <summary>
    /// GET /$versions
    /// Returns the FHIR versions supported by the server.
    /// Returns a Parameters resource with a list of version parameters.
    /// </summary>
    private static IResult HandleGetVersions(
        HttpContext context,
        [FromServices] IFhirVersionContext versionContext,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(MetadataEndpoints).FullName!);
        logger.LogInformation("GET /$versions");

        // Validate Accept header
        ValidateAcceptHeader(context);

        var parameters = BuildVersionsParameters(versionContext);
        return FhirResults.Ok(parameters, context);
    }

    /// <summary>
    /// GET /tenant/{tenantId}/$versions
    /// Returns the FHIR versions supported by the server for a specific tenant.
    /// Returns a Parameters resource with a list of version parameters.
    /// </summary>
    private static IResult HandleGetTenantVersions(
        HttpContext context,
        int tenantId,
        [FromServices] IFhirVersionContext versionContext,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(MetadataEndpoints).FullName!);
        logger.LogInformation("GET /tenant/{TenantId}/$versions", tenantId);

        // Validate Accept header
        ValidateAcceptHeader(context);

        // For now, all tenants support all FHIR versions
        // In the future, this could be tenant-specific
        var parameters = BuildVersionsParameters(versionContext);
        return FhirResults.Ok(parameters, context);
    }

    /// <summary>
    /// Builds a Parameters resource containing all supported FHIR versions.
    /// Format follows https://build.fhir.org/capabilitystatement-operation-versions.html
    /// </summary>
    private static ParametersJsonNode BuildVersionsParameters(IFhirVersionContext versionContext)
    {
        var parameters = new ParametersJsonNode();

        // Define supported FHIR versions with their enum values
        // Note: Order matters for default version selection (R4 is most commonly used)
        var supportedVersions = new[]
        {
            (FhirVersion.R4, isDefault: true),
            (FhirVersion.R4B, isDefault: false),
            (FhirVersion.R5, isDefault: false),
            (FhirVersion.R6, isDefault: false),
            (FhirVersion.Stu3, isDefault: false),
        };

        foreach (var (fhirVersion, isDefault) in supportedVersions)
        {
            // Get the full version string from the schema provider
            var schemaProvider = versionContext.GetBaseSchemaProvider(fhirVersion);
            var fullVersion = schemaProvider.FullVersion;

            // Create a parameter for this version
            var versionParam = new ParameterJsonNode();
            versionParam.Name = "version";

            // Add nested parts as per the spec
            var versionCodePart = new ParameterJsonNode();
            versionCodePart.Name = "code";
            versionCodePart.SetValue("valueCode", fullVersion);

            versionParam.Part.Add(versionCodePart);

            // Add default flag if this is the default version
            if (isDefault)
            {
                var defaultPart = new ParameterJsonNode();
                defaultPart.Name = "default";
                defaultPart.SetValue("valueBoolean", true);
                versionParam.Part.Add(defaultPart);
            }

            parameters.Parameter.Add(versionParam);
        }

        return parameters;
    }
}
