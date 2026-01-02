// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkPatch;
using Ignixa.Application.BackgroundOperations.Jobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// API endpoints for FHIR $bulk-patch operation.
/// Enables bulk updates to FHIR resources using FHIR Patch semantics.
/// </summary>
public static class BulkPatchEndpoints
{
    /// <summary>
    /// Registers bulk-patch endpoints with the application.
    /// </summary>
    public static void MapBulkPatchEndpoints(this WebApplication app)
    {
        // PATCH /$bulk-patch - System-level bulk patch (auto-detect tenant)
        app.MapMethods("/$bulk-patch", ["PATCH"], StartBulkPatchSystemLevelAsync)
            .WithName("StartBulkPatchSystemLevel")
            .WithOpenApi();

        // PATCH /tenant/{tenantId}/$bulk-patch - Tenant-scoped bulk patch
        app.MapMethods("/tenant/{tenantId:int}/$bulk-patch", ["PATCH"], StartBulkPatchAsync)
            .WithName("StartBulkPatch")
            .WithOpenApi();

        // PATCH /{resourceType}/$bulk-patch - Resource type scoped bulk patch (auto-detect tenant)
        app.MapMethods("/{resourceType}/$bulk-patch", ["PATCH"], StartBulkPatchByTypeAsync)
            .WithName("StartBulkPatchByType")
            .WithOpenApi();

        // PATCH /tenant/{tenantId}/{resourceType}/$bulk-patch - Tenant + type scoped
        app.MapMethods("/tenant/{tenantId:int}/{resourceType}/$bulk-patch", ["PATCH"], StartBulkPatchByTypeTenantAsync)
            .WithName("StartBulkPatchByTypeTenant")
            .WithOpenApi();

        // GET /_bulk-patch/{jobId} - Poll job status (auto-detect tenant)
        app.MapGet("/_bulk-patch/{jobId}", GetBulkPatchStatusSystemLevelAsync)
            .WithName("GetBulkPatchStatusSystemLevel")
            .WithOpenApi();

        // GET /tenant/{tenantId}/_bulk-patch/{jobId} - Poll job status
        app.MapGet("/tenant/{tenantId:int}/_bulk-patch/{jobId}", GetBulkPatchStatusAsync)
            .WithName("GetBulkPatchStatus")
            .WithOpenApi();

        // DELETE /tenant/{tenantId}/_bulk-patch/{jobId} - Cancel job
        app.MapDelete("/tenant/{tenantId:int}/_bulk-patch/{jobId}", CancelBulkPatchAsync)
            .WithName("CancelBulkPatch")
            .WithOpenApi();
    }

    /// <summary>
    /// Starts a system-level bulk patch operation (auto-detects tenant from context).
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartBulkPatchSystemLevelAsync(
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Unable to determine tenant from request context"));
        }

        return await StartBulkPatchCoreAsync(tenantId, null, null, mediator, httpContext);
    }

    /// <summary>
    /// Starts a tenant-scoped bulk patch operation.
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartBulkPatchAsync(
        [FromRoute] int tenantId,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        return await StartBulkPatchCoreAsync(tenantId, null, null, mediator, httpContext);
    }

    /// <summary>
    /// Starts a resource type-scoped bulk patch operation (auto-detects tenant from context).
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartBulkPatchByTypeAsync(
        [FromRoute] string resourceType,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Unable to determine tenant from request context"));
        }

        return await StartBulkPatchCoreAsync(tenantId, resourceType, httpContext.Request.QueryString.Value?.TrimStart('?'), mediator, httpContext);
    }

    /// <summary>
    /// Starts a tenant and resource type-scoped bulk patch operation.
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartBulkPatchByTypeTenantAsync(
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        return await StartBulkPatchCoreAsync(tenantId, resourceType, httpContext.Request.QueryString.Value?.TrimStart('?'), mediator, httpContext);
    }

    /// <summary>
    /// Core implementation for starting a bulk patch operation.
    /// Validates request headers and body, creates job definition, and starts orchestration.
    /// </summary>
    private static async Task<IResult> StartBulkPatchCoreAsync(
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
            var command = new CreateBulkPatchJobCommand
            {
                TenantId = tenantId,
                ResourceType = resourceType,
                SearchQuery = searchQuery,
                PatchParameters = resource
            };

            var result = await mediator.SendAsync(command, httpContext.RequestAborted);

            var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/tenant/{tenantId}/_bulk-patch/{result.JobId}";
            httpContext.Response.Headers["Content-Location"] = statusUrl;

            return Results.Accepted(statusUrl, new { jobId = result.JobId, status = result.Status });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(CreateOperationOutcome(ex.Message));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already running", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(CreateOperationOutcome(ex.Message));
        }
    }

    /// <summary>
    /// Gets bulk patch job status at system level (auto-detects tenant from context).
    /// Returns 202 Accepted while in progress, 200 OK when complete with results.
    /// </summary>
    private static async Task<IResult> GetBulkPatchStatusSystemLevelAsync(
        [FromRoute] string jobId,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Unable to determine tenant from request context"));
        }

        return await GetBulkPatchStatusCoreAsync(tenantId, jobId, mediator, httpContext);
    }

    /// <summary>
    /// Gets the status of a bulk patch job.
    /// Returns 202 Accepted while in progress, 200 OK when complete with results.
    /// </summary>
    private static async Task<IResult> GetBulkPatchStatusAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        return await GetBulkPatchStatusCoreAsync(tenantId, jobId, mediator, httpContext);
    }

    /// <summary>
    /// Core implementation for getting bulk patch job status.
    /// Retrieves job status and returns appropriate HTTP response based on job state.
    /// </summary>
    private static async Task<IResult> GetBulkPatchStatusCoreAsync(
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
                JobType = "BulkPatch",
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
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { error = "Bulk patch job not found" });
        }
    }

    /// <summary>
    /// Builds HTTP response for in-progress bulk patch jobs.
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
    /// Builds HTTP response for completed bulk patch jobs.
    /// Returns 200 OK with result counts.
    /// </summary>
    private static IResult BuildCompletedResponse(GetJobStatusResult status, int tenantId)
    {
        var response = new
        {
            transactionTime = status.EndDate ?? status.CreateDate,
            request = $"/tenant/{tenantId}/$bulk-patch",
            result = status.Result
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// Builds HTTP response for failed bulk patch jobs.
    /// Returns 200 OK with error information.
    /// </summary>
    private static IResult BuildFailedResponse(GetJobStatusResult status, int tenantId)
    {
        return Results.Ok(new
        {
            transactionTime = status.EndDate ?? status.CreateDate,
            request = $"/tenant/{tenantId}/$bulk-patch",
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
    /// Builds HTTP response for cancelled bulk patch jobs.
    /// Returns 200 OK with cancellation message.
    /// </summary>
    private static IResult BuildCancelledResponse(GetJobStatusResult status, int tenantId)
    {
        return Results.Ok(new
        {
            transactionTime = status.EndDate ?? status.CreateDate,
            request = $"/tenant/{tenantId}/$bulk-patch",
            error = new[]
            {
                new
                {
                    type = "OperationOutcome",
                    message = "Bulk patch cancelled by user"
                }
            }
        });
    }

    /// <summary>
    /// Cancels a bulk patch job.
    /// Terminates the orchestration and marks the job as cancelled.
    /// </summary>
    private static async Task<IResult> CancelBulkPatchAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<BulkPatchJobDefinition> jobRepository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetAsync(jobId, tenantId, cancellationToken);
        if (job == null)
        {
            return Results.NotFound(new { error = "Bulk patch job not found" });
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
