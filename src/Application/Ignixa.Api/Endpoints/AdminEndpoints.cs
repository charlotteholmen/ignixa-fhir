// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Api.Http;
using Ignixa.Application.BackgroundOperations.JobManagement;
using Ignixa.Application.Features.Mcp.Dtos;
using Ignixa.Application.Features.Mcp.Tools.TenantManagement;
using Ignixa.Domain.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Administrative operations for tenant and job management.
/// These operations are designed for server administration and CLI tool usage.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>
    /// Registers administrative operation endpoints.
    ///
    /// Supported Operations:
    /// - GET /$tenants - List all active tenants with configuration info
    /// - GET /tenant/{tenantId}/$jobs-list - List all import/export jobs for a tenant
    /// </summary>
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /$tenants - Root-level tenant listing (no tenant context)
        endpoints.MapGet("/$tenants", HandleListTenants)
            .WithName("ListTenants")
            .Produces<ListTenantsInfoResultDto>(StatusCodes.Status200OK, "application/json")
            .WithOpenApi();

        // GET /tenant/{tenantId}/$jobs-list - List jobs for a specific tenant
        endpoints.MapGet("/tenant/{tenantId:int}/$jobs-list", HandleListJobs)
            .WithName("ListJobs")
            .Produces<List<JobSummaryDto>>(StatusCodes.Status200OK, "application/json")
            .WithOpenApi();

        return endpoints;
    }

    /// <summary>
    /// Handles the $tenants operation to list all active tenants.
    /// </summary>
    private static async Task<IResult> HandleListTenants(
        [FromServices] ITenantConfigurationStore tenantStore,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get all active tenants (excludes system partition 0)
            var allTenants = await tenantStore.GetAllTenantsAsync(cancellationToken);
            var accessibleTenants = allTenants
                .Where(t => t.IsActive && !t.IsSystemPartition)
                .OrderBy(t => t.TenantId)
                .ToList();

            // Get system-wide tenant mode
            var mode = tenantStore.Mode;
            var modeDescription = mode switch
            {
                Domain.Models.TenantMode.Isolated => "single-tenant",
                Domain.Models.TenantMode.Distributed => "multi-tenant",
                _ => "unknown"
            };

            // Map to DTOs
            var tenantInfos = accessibleTenants.Select(t => new TenantInfoDto
            {
                Id = t.TenantId,
                Name = t.DisplayName,
                FhirVersion = t.FhirVersion,
                ValidationTier = t.ValidationDepth,
                IsActive = t.IsActive,
                Description = $"Tenant {t.TenantId}: {t.DisplayName} (FHIR {t.FhirVersion}, Validation: {t.ValidationDepth})"
            }).ToList();

            var result = new ListTenantsInfoResultDto
            {
                Mode = modeDescription,
                TotalCount = tenantInfos.Count,
                Tenants = tenantInfos
            };

            return Results.Json(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Error listing tenants",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Handles the $jobs-list operation to list all jobs for a tenant.
    /// </summary>
    private static async Task<IResult> HandleListJobs(
        [FromRoute] int tenantId,
        [FromQuery(Name = "jobType")] string? jobType,
        [FromQuery(Name = "status")] string? status,
        [FromServices] IBackgroundJobRepository<Domain.Models.ImportJobDefinition> importRepository,
        [FromServices] IBackgroundJobRepository<Domain.Models.ExportJobDefinition> exportRepository,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate job type filter
            if (jobType != null && jobType != "Import" && jobType != "Export")
            {
                return Results.BadRequest(new
                {
                    error = $"Invalid jobType '{jobType}'. Must be 'Import', 'Export', or null."
                });
            }

            var results = new List<JobSummaryDto>();

            // Get import jobs if requested
            if (jobType == null || jobType == "Import")
            {
                var importJobs = await importRepository.ListAsync(
                    jobType: (int)Domain.Models.BackgroundJobType.Import,
                    cancellationToken: cancellationToken);

                // Filter by tenant and status
                var filteredImports = importJobs
                    .Where(j => j.Definition.TenantId == tenantId)
                    .Where(j => status == null || j.Status == status);

                foreach (var job in filteredImports)
                {
                    results.Add(MapToSummaryDto(job, "Import"));
                }
            }

            // Get export jobs if requested
            if (jobType == null || jobType == "Export")
            {
                var exportJobs = await exportRepository.ListAsync(
                    jobType: (int)Domain.Models.BackgroundJobType.Export,
                    cancellationToken: cancellationToken);

                // Filter by tenant and status
                var filteredExports = exportJobs
                    .Where(j => j.Definition.TenantId == tenantId)
                    .Where(j => status == null || j.Status == status);

                foreach (var job in filteredExports)
                {
                    results.Add(MapToSummaryDto(job, "Export"));
                }
            }

            // Sort by create date descending (most recent first)
            var sortedResults = results.OrderByDescending(j => j.CreateDate).ToList();

            return Results.Json(sortedResults, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Error listing jobs",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static JobSummaryDto MapToSummaryDto<T>(Domain.Models.BackgroundJob<T> job, string jobType)
        where T : class, Domain.Models.IJobDefinition
    {
        // Extract progress if available
        double? progressPercentage = null;
        string? progressDescription = null;

        if (job.Progress != null)
        {
            var progressJson = job.Progress.ToJsonString();

            if (jobType == "Import")
            {
                var progress = JsonSerializer.Deserialize<Domain.Models.ImportJobProgress>(progressJson);
                progressPercentage = progress?.ProgressPercentage;
                progressDescription = progress != null
                    ? $"{progress.ProgressPercentage:F2}% ({progress.ProcessedFiles} files, {progress.ProcessedResources} resources)"
                    : null;
            }
            else
            {
                var progress = JsonSerializer.Deserialize<Domain.Models.ExportJobProgress>(progressJson);
                progressPercentage = progress?.ProgressPercentage;
                progressDescription = progress != null
                    ? $"{progress.ProgressPercentage:F2}% ({progress.ResourcesExported} resources)"
                    : null;
            }
        }

        return new JobSummaryDto
        {
            JobId = job.JobId,
            JobType = jobType,
            Status = job.Status,
            ProgressPercentage = progressPercentage,
            ProgressDescription = progressDescription ?? job.Status,
            CreateDate = job.CreateDate,
            StartDate = job.StartDate,
            EndDate = job.EndDate,
            ErrorMessage = job.ErrorMessage
        };
    }
}
