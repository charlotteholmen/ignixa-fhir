// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkPatch.Models;
using Ignixa.Application.BackgroundOperations.BulkPatch.Orchestrations;
using Ignixa.Application.Features.Patch;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.Models;
using Medino;

namespace Ignixa.Application.BackgroundOperations.BulkPatch;

/// <summary>
/// Handler for creating and starting FHIR bulk patch jobs.
/// Validates input parameters, creates job metadata, and starts the DurableTask orchestration.
/// </summary>
public class CreateBulkPatchJobHandler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<BulkPatchJobDefinition> jobRepository,
    FhirPatchParametersParser patchParametersParser)
    : IRequestHandler<CreateBulkPatchJobCommand, CreateBulkPatchJobResult>
{
    public async Task<CreateBulkPatchJobResult> HandleAsync(
        CreateBulkPatchJobCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.PatchParameters);

        var existingJobs = await jobRepository.ListAsync((int)BackgroundJobType.BulkPatch, cancellationToken);
        var activeJobs = existingJobs.Where(j =>
            j.Definition.TenantId == request.TenantId &&
            (j.Status == "Queued" || j.Status == "Running")).ToList();

        if (activeJobs.Count > 0)
        {
            throw new InvalidOperationException(
                $"A bulk patch job is already running for tenant {request.TenantId}. " +
                $"Only one bulk patch job can run at a time. Job ID: {activeJobs[0].JobId}");
        }

        var fhirPatchOperations = patchParametersParser.Parse(request.PatchParameters);

        var operations = ConvertToBulkPatchOperationDefinitions(fhirPatchOperations);

        if (operations.Count == 0)
        {
            throw new ArgumentException(
                "Parameters resource must contain at least one 'replace' or 'add' operation.",
                nameof(request));
        }

        var jobId = Guid.NewGuid().ToString("N");

        var job = new BackgroundJob<BulkPatchJobDefinition>
        {
            JobId = jobId,
            JobType = (int)BackgroundJobType.BulkPatch,
            Status = "Queued",
            Definition = new BulkPatchJobDefinition
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

        var orchestrationInput = new BulkPatchOrchestrationInput
        {
            JobId = jobId,
            TenantId = request.TenantId,
            ResourceType = request.ResourceType,
            SearchQuery = request.SearchQuery,
            Operations = operations
        };

        var instance = await taskHubClient.CreateOrchestrationInstanceAsync(
            typeof(BulkPatchOrchestration),
            jobId,
            orchestrationInput);

        job.OrchestrationInstanceId = instance.InstanceId;
        await jobRepository.UpdateAsync(job, request.TenantId, cancellationToken);

        return new CreateBulkPatchJobResult
        {
            JobId = jobId,
            Status = "Queued",
            OrchestrationInstanceId = instance.InstanceId,
            CreateDate = job.CreateDate
        };
    }

    private static List<BulkPatchOperationDefinition> ConvertToBulkPatchOperationDefinitions(
        FhirPatchOperation[] fhirPatchOperations)
    {
        var operations = new List<BulkPatchOperationDefinition>();

        foreach (var fhirOp in fhirPatchOperations)
        {
            var bulkPatchType = fhirOp.Type switch
            {
                FhirPatchOperationType.Replace => "replace",
                FhirPatchOperationType.Add => "upsert",
                _ => null
            };

            if (bulkPatchType == null)
            {
                continue;
            }

            operations.Add(new BulkPatchOperationDefinition
            {
                Type = bulkPatchType,
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
