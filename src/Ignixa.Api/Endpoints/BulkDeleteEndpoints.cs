// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete;
using Ignixa.Application.BackgroundOperations.Jobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// API endpoints for FHIR bulk delete operations ($bulk-delete).
/// Implements the Azure Healthcare APIs FHIR bulk delete specification.
/// Uses DurableTask framework for durable, reliable background processing.
/// </summary>
public static class BulkDeleteEndpoints
{
    /// <summary>
    /// Registers bulk delete-related endpoints with the application.
    /// </summary>
    public static void MapBulkDeleteEndpoints(this WebApplication app)
    {
        // DELETE /tenant/{tenantId}/$bulk-delete - Start a system-level bulk delete job
        app.MapDelete("/tenant/{tenantId:int}/$bulk-delete", StartSystemBulkDeleteAsync)
            .WithName("StartSystemBulkDelete")
            .WithOpenApi();

        // DELETE /tenant/{tenantId}/{resourceType}/$bulk-delete - Start a type-specific bulk delete job
        app.MapDelete("/tenant/{tenantId:int}/{resourceType}/$bulk-delete", StartTypeBulkDeleteAsync)
            .WithName("StartTypeBulkDelete")
            .WithOpenApi();

        // GET /tenant/{tenantId}/_operations/bulk-delete/{jobId} - Poll bulk delete job status
        app.MapGet("/tenant/{tenantId:int}/_operations/bulk-delete/{jobId}", GetBulkDeleteStatusAsync)
            .WithName("GetBulkDeleteStatus")
            .WithOpenApi();

        // DELETE /tenant/{tenantId}/_operations/bulk-delete/{jobId} - Cancel bulk delete job
        app.MapDelete("/tenant/{tenantId:int}/_operations/bulk-delete/{jobId}", CancelBulkDeleteAsync)
            .WithName("CancelBulkDelete")
            .WithOpenApi();
    }

    /// <summary>
    /// Starts a system-level bulk delete operation (all resource types).
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartSystemBulkDeleteAsync(
        [FromRoute] int tenantId,
        [FromQuery(Name = "_hardDelete")] bool hardDelete,
        [FromQuery(Name = "_purgeHistory")] bool purgeHistory,
        [FromQuery(Name = "excludedResourceTypes")] string? excludedResourceTypes,
        [FromQuery(Name = "_remove-references")] bool removeReferences,
        [FromQuery(Name = "_not-referenced")] string? notReferenced,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        // Parse excluded resource types from comma-separated query parameter
        var excluded = string.IsNullOrWhiteSpace(excludedResourceTypes)
            ? Array.Empty<string>()
            : excludedResourceTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Parse not-referenced resource types
        var notReferencedBy = string.IsNullOrWhiteSpace(notReferenced)
            ? Array.Empty<string>()
            : notReferenced.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Check required headers
        if (!ValidateRequiredHeaders(httpContext, out var headerError))
        {
            return headerError!;
        }

        try
        {
            var command = new CreateBulkDeleteJobCommand
            {
                TenantId = tenantId,
                ResourceType = null, // System-level delete
                SearchQuery = null,
                HardDelete = hardDelete,
                PurgeHistory = purgeHistory,
                ExcludedResourceTypes = excluded,
                RemoveReferences = removeReferences,
                NotReferencedBy = notReferencedBy
            };

            var result = await mediator.SendAsync(command, httpContext.RequestAborted);

            // Return 202 Accepted with Content-Location header
            var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/tenant/{tenantId}/_operations/bulk-delete/{result.JobId}";
            httpContext.Response.Headers["Content-Location"] = statusUrl;

            return Results.Accepted(statusUrl, new { jobId = result.JobId, status = "queued" });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "invalid",
                        diagnostics = ex.Message
                    }
                }
            });
        }
    }

    /// <summary>
    /// Starts a type-specific bulk delete operation.
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartTypeBulkDeleteAsync(
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromQuery(Name = "_hardDelete")] bool hardDelete,
        [FromQuery(Name = "_purgeHistory")] bool purgeHistory,
        [FromQuery(Name = "_remove-references")] bool removeReferences,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        // Check required headers
        if (!ValidateRequiredHeaders(httpContext, out var headerError))
        {
            return headerError!;
        }

        // Extract search parameters from query string (excluding bulk delete specific params)
        var searchQuery = BuildSearchQueryFromRequest(httpContext);

        try
        {
            var command = new CreateBulkDeleteJobCommand
            {
                TenantId = tenantId,
                ResourceType = resourceType,
                SearchQuery = searchQuery,
                HardDelete = hardDelete,
                PurgeHistory = purgeHistory,
                ExcludedResourceTypes = Array.Empty<string>(),
                RemoveReferences = removeReferences,
                NotReferencedBy = Array.Empty<string>()
            };

            var result = await mediator.SendAsync(command, httpContext.RequestAborted);

            // Return 202 Accepted with Content-Location header
            var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/tenant/{tenantId}/_operations/bulk-delete/{result.JobId}";
            httpContext.Response.Headers["Content-Location"] = statusUrl;

            return Results.Accepted(statusUrl, new { jobId = result.JobId, status = "queued" });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "invalid",
                        diagnostics = ex.Message
                    }
                }
            });
        }
    }

    /// <summary>
    /// Gets the status of a bulk delete job.
    /// Returns 202 Accepted while in progress, 200 OK when complete with results.
    /// </summary>
    private static async Task<IResult> GetBulkDeleteStatusAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        try
        {
            var query = new GetJobStatusQuery
            {
                JobId = jobId,
                JobType = "BulkDelete",
                TenantId = tenantId
            };

            var jobStatus = await mediator.SendAsync(query, httpContext.RequestAborted);

            // Return response based on status
            return jobStatus.Status switch
            {
                "Queued" or "Running" => Results.Accepted(
                    value: new
                    {
                        jobId = jobStatus.JobId,
                        status = jobStatus.Status,
                        progressPercentage = jobStatus.ProgressPercentage,
                        progressDescription = jobStatus.ProgressDescription
                    }),

                "Completed" => Results.Ok(BuildCompletedResponse(jobStatus)),

                "Failed" => Results.Ok(new
                {
                    transactionTime = jobStatus.EndDate ?? jobStatus.CreateDate,
                    request = $"/tenant/{tenantId}/$bulk-delete",
                    error = new[]
                    {
                        new
                        {
                            type = "OperationOutcome",
                            message = jobStatus.ErrorMessage,
                        },
                    },
                }),

                _ => Results.StatusCode(500),
            };
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { error = "Bulk delete job not found" });
        }
    }

    /// <summary>
    /// Cancels a bulk delete job.
    /// </summary>
    private static async Task<IResult> CancelBulkDeleteAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<BulkDeleteJobDefinition> jobRepository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetAsync(jobId, tenantId, cancellationToken);
        if (job == null)
        {
            return Results.NotFound(new { error = "Bulk delete job not found" });
        }

        // Terminate the orchestration
        var instance = new OrchestrationInstance { InstanceId = jobId };
        await taskHubClient.TerminateInstanceAsync(instance, "Cancelled by user");

        job.Status = "Cancelled";
        job.EndDate = DateTimeOffset.UtcNow;
        await jobRepository.UpdateAsync(job, tenantId, cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// Validates required headers for bulk delete operations.
    /// </summary>
    private static bool ValidateRequiredHeaders(HttpContext httpContext, out IResult? error)
    {
        error = null;

        // Check Accept header
        if (!httpContext.Request.Headers.TryGetValue("Accept", out var acceptHeader) ||
            !acceptHeader.ToString().Contains("application/fhir+json"))
        {
            error = Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "invalid",
                        diagnostics = "Accept header must include 'application/fhir+json'"
                    }
                }
            });
            return false;
        }

        // Check Prefer header
        if (!httpContext.Request.Headers.TryGetValue("Prefer", out var preferHeader) ||
            !preferHeader.ToString().Contains("respond-async"))
        {
            error = Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "invalid",
                        diagnostics = "Prefer header must include 'respond-async'"
                    }
                }
            });
            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds search query string from HTTP request query parameters.
    /// Excludes bulk delete specific parameters.
    /// </summary>
    private static string? BuildSearchQueryFromRequest(HttpContext httpContext)
    {
        var excludedParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_hardDelete",
            "_purgeHistory",
            "_remove-references",
            "_not-referenced",
            "excludedResourceTypes"
        };

        var searchParams = httpContext.Request.Query
            .Where(kvp => !excludedParams.Contains(kvp.Key))
            .Select(kvp => $"{kvp.Key}={kvp.Value}")
            .ToList();

        return searchParams.Count > 0 ? string.Join("&", searchParams) : null;
    }

    /// <summary>
    /// Builds FHIR Parameters response for completed bulk delete.
    /// Returns the count of deleted resources per type.
    /// </summary>
    private static object BuildCompletedResponse(GetJobStatusResult jobStatus)
    {
        var parameters = new List<object>();

        if (jobStatus.Result != null)
        {
            var resultDynamic = jobStatus.Result as dynamic;
            var deletedByType = resultDynamic?.deletedResourcesByType as Dictionary<string, long>;

            if (deletedByType != null)
            {
                foreach (var (resourceType, count) in deletedByType)
                {
                    parameters.Add(new
                    {
                        name = "ResourceDeletedCount",
                        part = new[]
                        {
                            new { name = "ResourceType", valueString = resourceType },
                            new { name = "Count", valueInteger64 = count }
                        }
                    });
                }
            }
        }

        return new
        {
            resourceType = "Parameters",
            parameter = parameters
        };
    }
}
