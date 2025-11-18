// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Ignixa.Specification;
using Medino;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Handler for creating and starting FHIR bulk delete jobs.
/// Validates input parameters, creates job metadata, and starts the DurableTask orchestration.
/// Resolves all resource types from IFhirSchemaProvider for system-level deletes.
/// </summary>
public class CreateBulkDeleteJobHandler : IRequestHandler<CreateBulkDeleteJobCommand, CreateBulkDeleteJobResult>
{
    private readonly TaskHubClient _taskHubClient;
    private readonly IBackgroundJobRepository<BulkDeleteJobDefinition> _jobRepository;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IFhirVersionContext _fhirVersionContext;

    public CreateBulkDeleteJobHandler(
        TaskHubClient taskHubClient,
        IBackgroundJobRepository<BulkDeleteJobDefinition> jobRepository,
        ITenantConfigurationStore tenantConfigurationStore,
        IFhirVersionContext fhirVersionContext)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
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

        // Get tenant configuration to resolve FHIR version
        var tenantConfig = await _tenantConfigurationStore.GetTenantConfigurationAsync(
            request.TenantId,
            cancellationToken);

        if (tenantConfig == null)
        {
            throw new InvalidOperationException($"Tenant {request.TenantId} not found or inactive");
        }

        // Get all resource types from schema provider for this tenant's FHIR version
        // This ensures we use the correct types for R4, R4B, R5, STU3, etc.
        IReadOnlyCollection<string> allResourceTypes;
        if (string.IsNullOrEmpty(request.ResourceType))
        {
            var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
            var schemaProvider = _fhirVersionContext.GetSchemaProvider(fhirVersion, request.TenantId);
            allResourceTypes = schemaProvider.ResourceTypeNames.ToList();
        }
        else
        {
            // Type-specific delete - no need to resolve all types
            allResourceTypes = Array.Empty<string>();
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
            NotReferencedBy: request.NotReferencedBy,
            AllResourceTypes: allResourceTypes);

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
