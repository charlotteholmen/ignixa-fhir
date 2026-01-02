// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkUpdate;
using Ignixa.Application.BackgroundOperations.Jobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// API endpoints for FHIR $bulk-update operation.
/// Enables bulk updates to FHIR resources using FHIR Patch semantics.
/// </summary>
public static class BulkUpdateEndpoints
{
    /// <summary>
    /// Registers bulk-update endpoints with the application.
    /// </summary>
    public static void MapBulkUpdateEndpoints(this WebApplication app)
    {
        // PATCH /$bulk-update - System-level bulk update (auto-detect tenant)
        app.MapMethods("/$bulk-update", ["PATCH"], StartBulkUpdateSystemLevelAsync)
            .WithName("StartBulkUpdateSystemLevel")
            .WithOpenApi();

        // PATCH /tenant/{tenantId}/$bulk-update - Tenant-scoped bulk update
        app.MapMethods("/tenant/{tenantId:int}/$bulk-update", ["PATCH"], StartBulkUpdateAsync)
            .WithName("StartBulkUpdate")
            .WithOpenApi();

        // PATCH /{resourceType}/$bulk-update - Resource type scoped bulk update (auto-detect tenant)
        app.MapMethods("/{resourceType}/$bulk-update", ["PATCH"], StartBulkUpdateByTypeAsync)
            .WithName("StartBulkUpdateByType")
            .WithOpenApi();

        // PATCH /tenant/{tenantId}/{resourceType}/$bulk-update - Tenant + type scoped
        app.MapMethods("/tenant/{tenantId:int}/{resourceType}/$bulk-update", ["PATCH"], StartBulkUpdateByTypeTenantAsync)
            .WithName("StartBulkUpdateByTypeTenant")
            .WithOpenApi();

        // GET /_bulk-update/{jobId} - Poll job status (auto-detect tenant)
        app.MapGet("/_bulk-update/{jobId}", GetBulkUpdateStatusSystemLevelAsync)
            .WithName("GetBulkUpdateStatusSystemLevel")
            .WithOpenApi();

        // GET /tenant/{tenantId}/_bulk-update/{jobId} - Poll job status
        app.MapGet("/tenant/{tenantId:int}/_bulk-update/{jobId}", GetBulkUpdateStatusAsync)
            .WithName("GetBulkUpdateStatus")
            .WithOpenApi();

        // DELETE /tenant/{tenantId}/_bulk-update/{jobId} - Cancel job
        app.MapDelete("/tenant/{tenantId:int}/_bulk-update/{jobId}", CancelBulkUpdateAsync)
            .WithName("CancelBulkUpdate")
            .WithOpenApi();
    }

    /// <summary>
    /// Starts a system-level bulk update operation (auto-detects tenant from context).
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartBulkUpdateSystemLevelAsync(
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Unable to determine tenant from request context"));
        }

        return await StartBulkUpdateCoreAsync(tenantId, null, null, mediator, httpContext);
    }

    /// <summary>
    /// Starts a tenant-scoped bulk update operation.
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartBulkUpdateAsync(
        [FromRoute] int tenantId,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        return await StartBulkUpdateCoreAsync(tenantId, null, null, mediator, httpContext);
    }

    /// <summary>
    /// Starts a resource type-scoped bulk update operation (auto-detects tenant from context).
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartBulkUpdateByTypeAsync(
        [FromRoute] string resourceType,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Unable to determine tenant from request context"));
        }

        return await StartBulkUpdateCoreAsync(tenantId, resourceType, httpContext.Request.QueryString.Value?.TrimStart('?'), mediator, httpContext);
    }

    /// <summary>
    /// Starts a tenant and resource type-scoped bulk update operation.
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartBulkUpdateByTypeTenantAsync(
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        return await StartBulkUpdateCoreAsync(tenantId, resourceType, httpContext.Request.QueryString.Value?.TrimStart('?'), mediator, httpContext);
    }

    /// <summary>
    /// Core implementation for starting a bulk update operation.
    /// Validates request headers and body, creates job definition, and starts orchestration.
    /// </summary>
    private static async Task<IResult> StartBulkUpdateCoreAsync(
        int tenantId,
        string? resourceType,
        string? searchQuery,
        IMediator mediator,
        HttpContext httpContext)
    {
        var preferHeader = httpContext.Request.Headers["Prefer"].FirstOrDefault();
        if (preferHeader == null || !preferHeader.Contains("respond-async", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Prefer: respond-async header is required for bulk operations"));
        }

        string requestBody;
        using (var reader = new StreamReader(httpContext.Request.Body))
        {
            requestBody = await reader.ReadToEndAsync();
        }

        ResourceJsonNode resource;
        try
        {
            resource = JsonSourceNodeFactory.Parse(requestBody);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Invalid request body. Expected FHIR Parameters resource: " + ex.Message));
        }

        if (resource.ResourceType != "Parameters")
        {
            return Results.BadRequest(CreateOperationOutcome(
                $"Expected Parameters resource, got {resource.ResourceType}"));
        }

        try
        {
            var command = new CreateBulkUpdateJobCommand
            {
                TenantId = tenantId,
                ResourceType = resourceType,
                SearchQuery = searchQuery,
                PatchParameters = resource
            };

            var result = await mediator.SendAsync(command, httpContext.RequestAborted);

            var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/tenant/{tenantId}/_bulk-update/{result.JobId}";
            httpContext.Response.Headers["Content-Location"] = statusUrl;

            return Results.Accepted(statusUrl, new { jobId = result.JobId, status = result.Status });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(CreateOperationOutcome(ex.Message));
        }
        catch (BulkUpdateJobAlreadyRunningException ex)
        {
            return Results.BadRequest(CreateOperationOutcome(ex.Message));
        }
    }

    /// <summary>
    /// Gets bulk update job status at system level (auto-detects tenant from context).
    /// Returns 202 Accepted while in progress, 200 OK when complete with results.
    /// </summary>
    private static async Task<IResult> GetBulkUpdateStatusSystemLevelAsync(
        [FromRoute] string jobId,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Unable to determine tenant from request context"));
        }

        return await GetBulkUpdateStatusCoreAsync(tenantId, jobId, mediator, httpContext);
    }

    /// <summary>
    /// Gets the status of a bulk update job.
    /// Returns 202 Accepted while in progress, 200 OK when complete with results.
    /// </summary>
    private static async Task<IResult> GetBulkUpdateStatusAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        return await GetBulkUpdateStatusCoreAsync(tenantId, jobId, mediator, httpContext);
    }

    /// <summary>
    /// Core implementation for getting bulk update job status.
    /// Retrieves job status and returns appropriate HTTP response based on job state.
    /// </summary>
    private static async Task<IResult> GetBulkUpdateStatusCoreAsync(
        int tenantId,
        string jobId,
        IMediator mediator,
        HttpContext httpContext)
    {
        try
        {
            var query = new GetJobStatusQuery
            {
                JobId = jobId,
                JobType = "BulkUpdate",
                TenantId = tenantId
            };

            var jobStatus = await mediator.SendAsync(query, httpContext.RequestAborted);

            return jobStatus.Status switch
            {
                "Queued" or "Running" => BuildProgressResponse(jobStatus, httpContext),
                "Completed" => BuildCompletedResponse(jobStatus, tenantId),
                "Failed" => BuildFailedResponse(jobStatus, tenantId),
                "Cancelled" => BuildCancelledResponse(jobStatus, tenantId),
                _ => Results.StatusCode(500)
            };
        }
        catch (JobNotFoundException)
        {
            return Results.NotFound(new { error = "Bulk update job not found" });
        }
    }

    /// <summary>
    /// Builds HTTP response for in-progress bulk update jobs.
    /// Returns 202 Accepted with progress information.
    /// </summary>
    private static IResult BuildProgressResponse(GetJobStatusResult status, HttpContext httpContext)
    {
        var progressText = status.ProgressDescription ?? "Processing...";
        if (status.ProgressPercentage.HasValue)
        {
            progressText = $"{status.ProgressPercentage:F2}% - {progressText}";
        }

        httpContext.Response.Headers["X-Progress"] = progressText;

        return Results.Accepted(value: new
        {
            jobId = status.JobId,
            status = status.Status,
            progressPercentage = status.ProgressPercentage,
            progressDescription = status.ProgressDescription
        });
    }

    /// <summary>
    /// Builds HTTP response for completed bulk update jobs.
    /// Returns 200 OK with result counts.
    /// </summary>
    private static IResult BuildCompletedResponse(GetJobStatusResult status, int tenantId)
    {
        var response = new
        {
            transactionTime = status.EndDate ?? status.CreateDate,
            request = $"/tenant/{tenantId}/$bulk-update",
            result = status.Result
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// Builds HTTP response for failed bulk update jobs.
    /// Returns 200 OK with error information.
    /// </summary>
    private static IResult BuildFailedResponse(GetJobStatusResult status, int tenantId)
    {
        return Results.Ok(new
        {
            transactionTime = status.EndDate ?? status.CreateDate,
            request = $"/tenant/{tenantId}/$bulk-update",
            error = new[]
            {
                new
                {
                    type = "OperationOutcome",
                    message = status.ErrorMessage
                }
            }
        });
    }

    /// <summary>
    /// Builds HTTP response for cancelled bulk update jobs.
    /// Returns 200 OK with cancellation message.
    /// </summary>
    private static IResult BuildCancelledResponse(GetJobStatusResult status, int tenantId)
    {
        return Results.Ok(new
        {
            transactionTime = status.EndDate ?? status.CreateDate,
            request = $"/tenant/{tenantId}/$bulk-update",
            error = new[]
            {
                new
                {
                    type = "OperationOutcome",
                    message = "Bulk update cancelled by user"
                }
            }
        });
    }

    /// <summary>
    /// Cancels a bulk update job.
    /// Terminates the orchestration and marks the job as cancelled.
    /// </summary>
    private static async Task<IResult> CancelBulkUpdateAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<BulkUpdateJobDefinition> jobRepository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetAsync(jobId, tenantId, cancellationToken);
        if (job == null)
        {
            return Results.NotFound(new { error = "Bulk update job not found" });
        }

        var instance = new OrchestrationInstance { InstanceId = jobId };
        await taskHubClient.TerminateInstanceAsync(instance, "Cancelled by user");

        job.Status = "Cancelled";
        job.EndDate = DateTimeOffset.UtcNow;
        await jobRepository.UpdateAsync(job, tenantId, cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// Creates a FHIR OperationOutcome for error responses.
    /// </summary>
    private static OperationOutcomeJsonNode CreateOperationOutcome(string message)
    {
        var outcome = new OperationOutcomeJsonNode();
        outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Invalid,
            Diagnostics = message
        });
        return outcome;
    }
}
