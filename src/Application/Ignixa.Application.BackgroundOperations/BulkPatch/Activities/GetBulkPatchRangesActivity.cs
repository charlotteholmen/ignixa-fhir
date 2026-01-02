// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkPatch.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkPatch.Activities;

public class GetBulkPatchRangesActivity(
    ISearchServiceFactory searchServiceFactory,
    ILogger<GetBulkPatchRangesActivity> logger)
    : AsyncTaskActivity<GetBulkPatchRangesInput, GetBulkPatchRangesOutput>
{
    private const int MaxResourcesPerRange = 10000;
    private const int NumberOfRangesPerType = 4;

    protected override async Task<GetBulkPatchRangesOutput> ExecuteAsync(
        TaskContext context,
        GetBulkPatchRangesInput input)
    {
        logger.LogInformation("Getting bulk patch ranges for job {JobId}", input.JobId);

        var searchService = await searchServiceFactory.GetSearchServiceAsync(
            input.TenantId,
            CancellationToken.None);

        var resourceTypes = input.ResourceType != null
            ? new List<string> { input.ResourceType }
            : GetDefaultResourceTypes();

        var ranges = new List<BulkPatchRange>();
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

                ranges.Add(new BulkPatchRange(
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

        return new GetBulkPatchRangesOutput(
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
