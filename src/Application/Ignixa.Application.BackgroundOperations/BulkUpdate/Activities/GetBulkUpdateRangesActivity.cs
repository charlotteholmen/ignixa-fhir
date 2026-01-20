// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkUpdate.Activities;

public class GetBulkUpdateRangesActivity(
    ISearchServiceFactory searchServiceFactory,
    ILogger<GetBulkUpdateRangesActivity> logger)
    : AsyncTaskActivity<GetBulkUpdateRangesInput, GetBulkUpdateRangesOutput>
{
    private const int MaxResourcesPerRange = 10000;
    private const int NumberOfRangesPerType = 4;

    protected override async Task<GetBulkUpdateRangesOutput> ExecuteAsync(
        TaskContext context,
        GetBulkUpdateRangesInput input)
    {
        // Note: DurableTask activities cannot access CancellationToken from TaskContext.
        // Cancellation is handled at the orchestration level by terminating the instance.
        logger.LogInformation("Getting bulk update ranges for job {JobId}", input.JobId);

        var searchService = await searchServiceFactory.GetSearchServiceAsync(
            input.TenantId,
            CancellationToken.None);

        var resourceTypes = input.ResourceType != null
            ? new List<string> { input.ResourceType }
            : GetDefaultResourceTypes();

        var ranges = new List<BulkUpdateRange>();
        var totalEstimated = 0;

        foreach (var resourceType in resourceTypes)
        {
            var typeRanges = await searchService.GetExportRangesAsync(
                resourceType,
                NumberOfRangesPerType,
                CancellationToken.None);

            if (typeRanges.Count == 0)
            {
                logger.LogDebug("No resources found for {ResourceType}", resourceType);
                continue;
            }

            foreach (var (startId, endId) in typeRanges)
            {
                var estimatedCount = (int)((endId - startId + 1) / NumberOfRangesPerType);
                if (estimatedCount > MaxResourcesPerRange)
                {
                    estimatedCount = MaxResourcesPerRange;
                }

                ranges.Add(new BulkUpdateRange(
                    ResourceType: resourceType,
                    StartSurrogateId: startId,
                    EndSurrogateId: endId,
                    EstimatedCount: estimatedCount));

                totalEstimated += estimatedCount;
            }
        }

        logger.LogInformation(
            "Found {RangeCount} ranges with {TotalEstimated} estimated resources",
            ranges.Count,
            totalEstimated);

        return new GetBulkUpdateRangesOutput(
            Ranges: ranges,
            TotalEstimatedResources: totalEstimated);
    }

    private static List<string> GetDefaultResourceTypes()
    {
        return
        [
            "Patient",
            "Observation",
            "Condition",
            "MedicationRequest",
            "Encounter",
            "Procedure"
        ];
    }
}
