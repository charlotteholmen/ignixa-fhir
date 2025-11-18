// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Handler for creating and starting FHIR bulk delete jobs.
/// Validates input parameters, creates job metadata, and starts the DurableTask orchestration.
/// </summary>
public class CreateBulkDeleteJobHandler : IRequestHandler<CreateBulkDeleteJobCommand, CreateBulkDeleteJobResult>
{
    private readonly TaskHubClient _taskHubClient;
    private readonly IBackgroundJobRepository<BulkDeleteJobDefinition> _jobRepository;

    public CreateBulkDeleteJobHandler(
        TaskHubClient taskHubClient,
        IBackgroundJobRepository<BulkDeleteJobDefinition> jobRepository)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
    }

    public async Task<CreateBulkDeleteJobResult> HandleAsync(
        CreateBulkDeleteJobCommand request,
        CancellationToken cancellationToken)
    {
        // Validate conflicting parameters
        if (request.HardDelete && request.PurgeHistory)
        {
            throw new ArgumentException(
                "HardDelete and PurgeHistory cannot both be true.",
                nameof(request));
        }

        // Generate job ID
        var jobId = Guid.NewGuid().ToString();

        // Create job metadata
        var job = new BackgroundJob<BulkDeleteJobDefinition>
        {
            JobId = jobId,
            JobType = (int)BackgroundJobType.BulkDelete,
            Status = "Queued",
            Definition = new BulkDeleteJobDefinition
            {
                TenantId = request.TenantId,
                ResourceType = request.ResourceType,
                SearchQuery = request.SearchQuery,
                HardDelete = request.HardDelete,
                PurgeHistory = request.PurgeHistory,
                ExcludedResourceTypes = request.ExcludedResourceTypes,
                RemoveReferences = request.RemoveReferences,
                NotReferencedBy = request.NotReferencedBy
            },
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        await _jobRepository.CreateAsync(job, cancellationToken);

        // Start the orchestration
        var orchestrationInput = new BulkDeleteOrchestrationInput(
            JobId: jobId,
            TenantId: request.TenantId,
            ResourceType: request.ResourceType,
            SearchQuery: request.SearchQuery,
            HardDelete: request.HardDelete,
            PurgeHistory: request.PurgeHistory,
            ExcludedResourceTypes: request.ExcludedResourceTypes,
            RemoveReferences: request.RemoveReferences,
            NotReferencedBy: request.NotReferencedBy);

        var instance = await _taskHubClient.CreateOrchestrationInstanceAsync(
            typeof(BulkDeleteOrchestration),
            jobId, // Use jobId as instance ID for easy lookup
            orchestrationInput);

        // Update job with orchestration instance ID
        job.OrchestrationInstanceId = instance.InstanceId;
        await _jobRepository.UpdateAsync(job, request.TenantId, cancellationToken);

        return new CreateBulkDeleteJobResult
        {
            JobId = jobId,
            Status = "Queued",
            OrchestrationInstanceId = instance.InstanceId,
            CreateDate = job.CreateDate
        };
    }
}
