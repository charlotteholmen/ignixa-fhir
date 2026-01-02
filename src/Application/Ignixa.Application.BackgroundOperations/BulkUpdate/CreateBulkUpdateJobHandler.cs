// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Models;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Orchestrations;
using Ignixa.Application.Features.Patch;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.Models;
using Medino;

namespace Ignixa.Application.BackgroundOperations.BulkUpdate;

/// <summary>
/// Handler for creating and starting FHIR bulk update jobs.
/// Validates input parameters, creates job metadata, and starts the DurableTask orchestration.
/// </summary>
public class CreateBulkUpdateJobHandler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<BulkUpdateJobDefinition> jobRepository,
    FhirPatchParametersParser patchParametersParser)
    : IRequestHandler<CreateBulkUpdateJobCommand, CreateBulkUpdateJobResult>
{
    public async Task<CreateBulkUpdateJobResult> HandleAsync(
        CreateBulkUpdateJobCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.PatchParameters);

        var existingJobs = await jobRepository.ListAsync((int)BackgroundJobType.BulkUpdate, cancellationToken);
        var activeJobs = existingJobs.Where(j =>
            j.Definition.TenantId == request.TenantId &&
            (j.Status == "Queued" || j.Status == "Running")).ToList();

        if (activeJobs.Count > 0)
        {
            throw new InvalidOperationException(
                $"A bulk update job is already running for tenant {request.TenantId}. " +
                $"Only one bulk update job can run at a time. Job ID: {activeJobs[0].JobId}");
        }

        var fhirPatchOperations = patchParametersParser.Parse(request.PatchParameters);

        var operations = ConvertToBulkUpdateOperationDefinitions(fhirPatchOperations);

        if (operations.Count == 0)
        {
            throw new ArgumentException(
                "Parameters resource must contain at least one 'replace' or 'add' operation.",
                nameof(request));
        }

        var jobId = Guid.NewGuid().ToString("N");

        var job = new BackgroundJob<BulkUpdateJobDefinition>
        {
            JobId = jobId,
            JobType = (int)BackgroundJobType.BulkUpdate,
            Status = "Queued",
            Definition = new BulkUpdateJobDefinition
            {
                TenantId = request.TenantId,
                ResourceType = request.ResourceType,
                SearchQuery = request.SearchQuery,
                Operations = operations
            },
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        await jobRepository.CreateAsync(job, cancellationToken);

        var orchestrationInput = new BulkUpdateOrchestrationInput
        {
            JobId = jobId,
            TenantId = request.TenantId,
            ResourceType = request.ResourceType,
            SearchQuery = request.SearchQuery,
            Operations = operations
        };

        var instance = await taskHubClient.CreateOrchestrationInstanceAsync(
            typeof(BulkUpdateOrchestration),
            jobId,
            orchestrationInput);

        job.OrchestrationInstanceId = instance.InstanceId;
        await jobRepository.UpdateAsync(job, request.TenantId, cancellationToken);

        return new CreateBulkUpdateJobResult
        {
            JobId = jobId,
            Status = "Queued",
            OrchestrationInstanceId = instance.InstanceId,
            CreateDate = job.CreateDate
        };
    }

    private static List<BulkUpdateOperationDefinition> ConvertToBulkUpdateOperationDefinitions(
        FhirPatchOperation[] fhirPatchOperations)
    {
        var operations = new List<BulkUpdateOperationDefinition>();

        foreach (var fhirOp in fhirPatchOperations)
        {
            var bulkUpdateType = fhirOp.Type switch
            {
                FhirPatchOperationType.Replace => "replace",
                FhirPatchOperationType.Add => "upsert",
                _ => null
            };

            if (bulkUpdateType == null)
            {
                continue;
            }

            operations.Add(new BulkUpdateOperationDefinition
            {
                Type = bulkUpdateType,
                Path = string.IsNullOrEmpty(fhirOp.Path)
                    ? throw new FhirPatchException($"{fhirOp.Type} operation requires non-empty 'path'")
                    : fhirOp.Path,
                Name = null,
                Value = fhirOp.Value
            });
        }

        return operations;
    }
}
