// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Activities;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Models;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.BulkUpdate.Orchestrations;

/// <summary>
/// DurableTask orchestration for FHIR bulk update operation.
/// Coordinates range determination and parallel processing of resources.
/// </summary>
public class BulkUpdateOrchestration : TaskOrchestration<BulkUpdateOrchestrationOutput, BulkUpdateOrchestrationInput>
{
    public override async Task<BulkUpdateOrchestrationOutput> RunTask(
        OrchestrationContext context,
        BulkUpdateOrchestrationInput input)
    {
        try
        {
            var rangesInput = new GetBulkUpdateRangesInput
            {
                JobId = input.JobId,
                TenantId = input.TenantId,
                ResourceType = input.ResourceType,
                SearchQuery = input.SearchQuery
            };

            var rangesOutput = await context.ScheduleTask<GetBulkUpdateRangesOutput>(
                typeof(GetBulkUpdateRangesActivity),
                rangesInput);

            if (rangesOutput.Ranges.Count == 0)
            {
                return new BulkUpdateOrchestrationOutput
                {
                    JobId = input.JobId,
                    Status = "Completed",
                    TotalProcessed = 0,
                    TotalUpdated = 0,
                    TotalIgnored = 0,
                    TotalFailed = 0
                };
            }

            var workerTasks = rangesOutput.Ranges.Select(range =>
            {
                var workerInput = new BulkUpdateWorkerInput
                {
                    JobId = input.JobId,
                    TenantId = input.TenantId,
                    ResourceType = range.ResourceType,
                    StartSurrogateId = range.StartSurrogateId,
                    EndSurrogateId = range.EndSurrogateId,
                    SearchQuery = input.SearchQuery,
                    Operations = input.Operations,
                    BatchSize = input.BatchSize
                };

                return context.ScheduleTask<BulkUpdateWorkerOutput>(
                    typeof(BulkUpdateWorkerActivity),
                    workerInput);
            }).ToList();

            var workerOutputs = await Task.WhenAll(workerTasks);

            var updatedCounts = new Dictionary<string, int>();
            var ignoredCounts = new Dictionary<string, int>();
            var failedCounts = new Dictionary<string, int>();
            var allIssues = new List<BulkUpdateIssue>();
            var totalUpdated = 0;
            var totalIgnored = 0;
            var totalFailed = 0;
            var totalProcessed = 0;
            const int maxTotalIssues = 1000;

            foreach (var output in workerOutputs)
            {
                totalProcessed += output.ProcessedCount;
                totalUpdated += output.UpdatedCount;
                totalIgnored += output.IgnoredCount;
                totalFailed += output.FailedCount;

                if (output.UpdatedCount > 0)
                    updatedCounts[output.ResourceType] = updatedCounts.GetValueOrDefault(output.ResourceType) + output.UpdatedCount;
                if (output.IgnoredCount > 0)
                    ignoredCounts[output.ResourceType] = ignoredCounts.GetValueOrDefault(output.ResourceType) + output.IgnoredCount;
                if (output.FailedCount > 0)
                    failedCounts[output.ResourceType] = failedCounts.GetValueOrDefault(output.ResourceType) + output.FailedCount;

                if (output.Issues != null && allIssues.Count < maxTotalIssues)
                {
                    var spaceLeft = maxTotalIssues - allIssues.Count;
                    allIssues.AddRange(output.Issues.Take(spaceLeft));
                }
            }

            return new BulkUpdateOrchestrationOutput
            {
                JobId = input.JobId,
                Status = "Completed",
                TotalProcessed = totalProcessed,
                TotalUpdated = totalUpdated,
                TotalIgnored = totalIgnored,
                TotalFailed = totalFailed,
                UpdatedCounts = updatedCounts,
                IgnoredCounts = ignoredCounts,
                FailedCounts = failedCounts,
                Issues = allIssues.Count > 0 ? allIssues : null
            };
        }
        catch (Exception ex)
        {
            return new BulkUpdateOrchestrationOutput
            {
                JobId = input.JobId,
                Status = "Failed",
                ErrorMessage = ex.Message,
                TotalProcessed = 0,
                TotalUpdated = 0,
                TotalIgnored = 0,
                TotalFailed = 0
            };
        }
    }
}
