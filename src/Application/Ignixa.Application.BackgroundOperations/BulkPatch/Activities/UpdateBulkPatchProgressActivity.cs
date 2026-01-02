// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkPatch.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkPatch.Activities;

public class UpdateBulkPatchProgressActivity(
    IBackgroundJobRepository<BulkPatchJobDefinition> jobRepository,
    ILogger<UpdateBulkPatchProgressActivity> logger)
    : AsyncTaskActivity<UpdateBulkPatchProgressInput, bool>
{
    protected override async Task<bool> ExecuteAsync(
        TaskContext context,
        UpdateBulkPatchProgressInput input)
    {
        logger.LogDebug("Updating progress for job {JobId}", input.JobId);

        var job = await jobRepository.GetAsync(input.JobId, input.TenantId, CancellationToken.None);
        if (job == null)
        {
            logger.LogWarning("Job {JobId} not found", input.JobId);
            return false;
        }

        var progress = new BulkPatchJobProgress
        {
            ProcessedResources = input.ProcessedResources,
            UpdatedResources = input.UpdatedResources,
            IgnoredResources = input.IgnoredResources,
            FailedResources = input.FailedResources,
            ProgressPercentage = input.TotalEstimated > 0
                ? (double)input.ProcessedResources / input.TotalEstimated * 100
                : 0,
            CurrentResourceType = input.CurrentResourceType
        };

        job.Progress = JsonNode.Parse(JsonSerializer.Serialize(progress));
        job.HeartbeatDate = DateTimeOffset.UtcNow;

        await jobRepository.UpdateAsync(job, input.TenantId, CancellationToken.None);

        return true;
    }
}
