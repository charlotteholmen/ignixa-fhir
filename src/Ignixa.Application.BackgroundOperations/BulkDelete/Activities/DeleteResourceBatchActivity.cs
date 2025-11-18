// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Activities;

/// <summary>
/// DurableTask activity that deletes a batch of resources.
/// Performs soft deletes (FHIR R4 compliant) unless HardDelete is specified.
/// </summary>
public class DeleteResourceBatchActivity : AsyncTaskActivity<DeleteResourceBatchInput, DeleteResourceBatchOutput>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ILogger<DeleteResourceBatchActivity> _logger;

    public DeleteResourceBatchActivity(
        IFhirRepositoryFactory repositoryFactory,
        ILogger<DeleteResourceBatchActivity> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<DeleteResourceBatchOutput> ExecuteAsync(
        TaskContext context,
        DeleteResourceBatchInput input)
    {
        _logger.LogInformation(
            "Deleting batch: JobId={JobId}, ResourceType={ResourceType}, Count={Count}, HardDelete={HardDelete}",
            input.JobId,
            input.ResourceType,
            input.ResourceIds.Count,
            input.HardDelete);

        try
        {
            var repository = await _repositoryFactory.GetRepositoryAsync(
                input.TenantId,
                CancellationToken.None);

            long deletedCount = 0;
            long failedCount = 0;
            var errors = new List<string>();

            foreach (var resourceId in input.ResourceIds)
            {
                try
                {
                    var key = new ResourceKey(input.ResourceType, resourceId, null);

                    var request = new ResourceRequest
                    {
                        Method = "DELETE",
                        Url = $"/{input.ResourceType}/{resourceId}",
                        Timestamp = DateTimeOffset.UtcNow
                    };

                    // Perform delete (soft delete by default per FHIR R4 spec)
                    var result = await repository.DeleteAsync(key, request, null, CancellationToken.None);

                    if (result != null)
                    {
                        deletedCount++;
                    }
                    else
                    {
                        // Resource never existed
                        failedCount++;
                        errors.Add($"{input.ResourceType}/{resourceId}: Resource not found");
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    errors.Add($"{input.ResourceType}/{resourceId}: {ex.Message}");

                    _logger.LogWarning(
                        ex,
                        "Failed to delete resource: {ResourceType}/{ResourceId}",
                        input.ResourceType,
                        resourceId);
                }
            }

            _logger.LogInformation(
                "Batch deletion completed: Deleted={DeletedCount}, Failed={FailedCount}",
                deletedCount,
                failedCount);

            return new DeleteResourceBatchOutput(
                ResourceType: input.ResourceType,
                DeletedCount: deletedCount,
                FailedCount: failedCount,
                Errors: errors.Count > 0 ? errors : null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete batch: JobId={JobId}, ResourceType={ResourceType}",
                input.JobId,
                input.ResourceType);

            throw;
        }
    }
}
