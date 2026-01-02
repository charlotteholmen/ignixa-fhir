// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Models;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.BackgroundOperations.Jobs;

/// <summary>
/// Handler for retrieving job status information.
/// Queries DurableTask orchestration state and updates job metadata accordingly.
/// Supports Import, Export, and BulkUpdate job types.
/// </summary>
public class GetJobStatusHandler : IRequestHandler<GetJobStatusQuery, GetJobStatusResult>
{
    private readonly TaskHubClient _taskHubClient;
    private readonly IBackgroundJobRepository<ImportJobDefinition> _importRepository;
    private readonly IBackgroundJobRepository<ExportJobDefinition> _exportRepository;
    private readonly IBackgroundJobRepository<BulkUpdateJobDefinition> _bulkUpdateRepository;

    public GetJobStatusHandler(
        TaskHubClient taskHubClient,
        IBackgroundJobRepository<ImportJobDefinition> importRepository,
        IBackgroundJobRepository<ExportJobDefinition> exportRepository,
        IBackgroundJobRepository<BulkUpdateJobDefinition> bulkUpdateRepository)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _importRepository = importRepository ?? throw new ArgumentNullException(nameof(importRepository));
        _exportRepository = exportRepository ?? throw new ArgumentNullException(nameof(exportRepository));
        _bulkUpdateRepository = bulkUpdateRepository ?? throw new ArgumentNullException(nameof(bulkUpdateRepository));
    }

    public async Task<GetJobStatusResult> HandleAsync(
        GetJobStatusQuery request,
        CancellationToken cancellationToken)
    {
        // Validate job type
        if (request.JobType != "Import" && request.JobType != "Export" && request.JobType != "BulkUpdate")
        {
            throw new ArgumentException(
                $"Invalid jobType '{request.JobType}'. Must be 'Import', 'Export', or 'BulkUpdate'.",
                nameof(request));
        }

        // Get job metadata based on type
        string status;
        DateTimeOffset createDate;
        DateTimeOffset? startDate;
        DateTimeOffset? endDate;
        string? errorMessage;
        object? definition;
        object? result;
        double? progressPercentage = null;
        string? progressDescription = null;

        if (request.JobType == "Import")
        {
            var job = await _importRepository.GetAsync(request.JobId, request.TenantId, cancellationToken);
            if (job == null)
            {
                throw new InvalidOperationException(
                    $"Import job '{request.JobId}' not found for tenant {request.TenantId}");
            }

            // Update job status from orchestration
            await UpdateJobStatusFromOrchestrationAsync(job, request.TenantId, cancellationToken);

            status = job.Status;
            createDate = job.CreateDate;
            startDate = job.StartDate;
            endDate = job.EndDate;
            errorMessage = job.ErrorMessage;

            definition = new
            {
                inputFormat = job.Definition.InputFormat,
                inputSource = job.Definition.InputSource,
                mode = job.Definition.Mode,
                inputFileCount = job.Definition.InputFiles.Count
            };

            // Parse progress if available
            if (job.Progress != null)
            {
                var progress = JsonSerializer.Deserialize<ImportJobProgress>(job.Progress.ToJsonString());
                progressPercentage = progress?.ProgressPercentage;
                progressDescription = progress != null
                    ? $"{progress.ProgressPercentage:F2}% ({progress.ProcessedFiles} files, {progress.ProcessedResources} resources)"
                    : status;
            }

            // Parse result if available
            if (job.Result != null)
            {
                var importResult = JsonSerializer.Deserialize<ImportJobResult>(job.Result.ToJsonString());
                result = new
                {
                    totalResources = importResult?.TotalResources ?? 0,
                    totalErrors = importResult?.TotalErrors ?? 0,
                    errorFileUrl = importResult?.ErrorFileUrl
                };
            }
            else
            {
                result = null;
            }
        }
        else if (request.JobType == "Export")
        {
            var job = await _exportRepository.GetAsync(request.JobId, request.TenantId, cancellationToken);
            if (job == null)
            {
                throw new InvalidOperationException(
                    $"Export job '{request.JobId}' not found for tenant {request.TenantId}");
            }

            // Update job status from orchestration
            await UpdateJobStatusFromOrchestrationAsync(job, request.TenantId, cancellationToken);

            status = job.Status;
            createDate = job.CreateDate;
            startDate = job.StartDate;
            endDate = job.EndDate;
            errorMessage = job.ErrorMessage;

            definition = new
            {
                resourceTypes = job.Definition.ResourceTypes,
                since = job.Definition.Since,
                outputFormat = job.Definition.OutputFormat,
                outputPath = job.Definition.OutputPath
            };

            // Parse progress if available
            if (job.Progress != null)
            {
                var progress = JsonSerializer.Deserialize<ExportJobProgress>(job.Progress.ToJsonString());
                progressPercentage = progress?.ProgressPercentage;
                progressDescription = progress != null
                    ? $"{progress.ProgressPercentage:F2}% ({progress.ResourcesExported} resources)"
                    : status;
            }

            // Parse result if available
            if (job.Result != null)
            {
                var exportResult = JsonSerializer.Deserialize<ExportJobResult>(job.Result.ToJsonString());
                result = new
                {
                    outputFiles = exportResult?.ExportedFiles ?? new Dictionary<string, string>(),
                    totalResources = exportResult?.TotalResources ?? 0
                };
            }
            else
            {
                result = null;
            }
        }
        else // BulkUpdate
        {
            var job = await _bulkUpdateRepository.GetAsync(request.JobId, request.TenantId, cancellationToken);
            if (job == null)
            {
                throw new InvalidOperationException(
                    $"BulkUpdate job '{request.JobId}' not found for tenant {request.TenantId}");
            }

            // Update job status from orchestration
            await UpdateJobStatusFromOrchestrationAsync(job, request.TenantId, cancellationToken);

            status = job.Status;
            createDate = job.CreateDate;
            startDate = job.StartDate;
            endDate = job.EndDate;
            errorMessage = job.ErrorMessage;

            definition = new
            {
                resourceType = job.Definition.ResourceType,
                searchQuery = job.Definition.SearchQuery,
                operationCount = job.Definition.Operations.Count
            };

            // Parse progress if available
            if (job.Progress != null)
            {
                var progress = JsonSerializer.Deserialize<BulkUpdateJobProgress>(job.Progress.ToJsonString());
                progressPercentage = progress?.ProgressPercentage;
                progressDescription = progress != null
                    ? $"{progress.ProgressPercentage:F2}% ({progress.ProcessedResources} processed)"
                    : status;
            }

            // Parse result if available
            if (job.Result != null)
            {
                var bulkUpdateResult = JsonSerializer.Deserialize<BulkUpdateJobResult>(job.Result.ToJsonString());
                result = new
                {
                    totalUpdated = bulkUpdateResult?.UpdatedCounts?.Values.Sum() ?? 0,
                    totalIgnored = bulkUpdateResult?.IgnoredCounts?.Values.Sum() ?? 0,
                    totalFailed = bulkUpdateResult?.FailedCounts?.Values.Sum() ?? 0,
                    updatedCounts = bulkUpdateResult?.UpdatedCounts,
                    ignoredCounts = bulkUpdateResult?.IgnoredCounts,
                    failedCounts = bulkUpdateResult?.FailedCounts,
                    issues = bulkUpdateResult?.Issues
                };
            }
            else
            {
                result = null;
            }
        }

        return new GetJobStatusResult
        {
            JobId = request.JobId,
            JobType = request.JobType,
            Status = status,
            ProgressPercentage = progressPercentage,
            ProgressDescription = progressDescription ?? status,
            CreateDate = createDate,
            StartDate = startDate,
            EndDate = endDate,
            ErrorMessage = errorMessage,
            Definition = definition,
            Result = result
        };
    }

    /// <summary>
    /// Updates job status based on orchestration state.
    /// Handles state transitions: Queued -> Running -> Completed/Failed/Cancelled.
    /// </summary>
    private async Task UpdateJobStatusFromOrchestrationAsync<T>(
        BackgroundJob<T> job,
        int tenantId,
        CancellationToken cancellationToken)
        where T : class
    {
        var state = await _taskHubClient.GetOrchestrationStateAsync(job.JobId);

        if (state != null)
        {
            switch (state.OrchestrationStatus)
            {
                case OrchestrationStatus.Running:
                case OrchestrationStatus.Pending:
                    job.Status = "Running";
                    if (job.StartDate == null)
                    {
                        job.StartDate = DateTimeOffset.UtcNow;
                    }
                    break;

                case OrchestrationStatus.Completed:
                    job.Status = "Completed";
                    job.EndDate = DateTimeOffset.UtcNow;

                    // Extract output from orchestration for import jobs
                    if (job is BackgroundJob<ImportJobDefinition> && state.Output != null)
                    {
                        var output = JsonSerializer.Deserialize<ImportOrchestrationOutput>(state.Output);
                        if (output != null)
                        {
                            var jobResult = new ImportJobResult
                            {
                                TotalResources = output.TotalResources,
                                TotalErrors = output.TotalErrors,
                                ErrorFileUrl = output.ErrorFileUrl
                            };
                            job.Result = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(jobResult));
                        }
                    }

                    // Extract output from orchestration for bulk update jobs
                    if (job is BackgroundJob<BulkUpdateJobDefinition> && state.Output != null)
                    {
                        var output = JsonSerializer.Deserialize<BulkUpdateOrchestrationOutput>(state.Output);
                        if (output != null)
                        {
                            var jobResult = new BulkUpdateJobResult
                            {
                                UpdatedCounts = output.UpdatedCounts ?? [],
                                IgnoredCounts = output.IgnoredCounts ?? [],
                                FailedCounts = output.FailedCounts ?? [],
                                Issues = output.Issues ?? []
                            };
                            job.Result = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(jobResult));
                        }
                    }
                    break;

                case OrchestrationStatus.Failed:
                    job.Status = "Failed";
                    job.EndDate = DateTimeOffset.UtcNow;
                    job.ErrorMessage = "Orchestration failed";
                    break;

                case OrchestrationStatus.Terminated:
                    job.Status = "Cancelled";
                    job.EndDate = DateTimeOffset.UtcNow;
                    break;
            }

            // Update job in repository
            if (job is BackgroundJob<ImportJobDefinition> importJob)
            {
                await _importRepository.UpdateAsync(importJob, tenantId, cancellationToken);
            }
            else if (job is BackgroundJob<ExportJobDefinition> exportJob)
            {
                await _exportRepository.UpdateAsync(exportJob, tenantId, cancellationToken);
            }
            else if (job is BackgroundJob<BulkUpdateJobDefinition> bulkUpdateJob)
            {
                await _bulkUpdateRepository.UpdateAsync(bulkUpdateJob, tenantId, cancellationToken);
            }
        }
    }
}
