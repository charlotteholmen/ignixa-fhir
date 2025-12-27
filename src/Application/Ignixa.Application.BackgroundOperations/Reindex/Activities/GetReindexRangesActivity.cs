// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;
using Polly;
using Polly.Retry;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

/// <summary>
/// DurableTask activity that determines surrogate ID ranges for a resource type.
/// Called once per resource type by the orchestration to enable parallel processing.
/// Queries the database to find min/max surrogate IDs, then partitions into equal-sized ranges.
/// Follows the same pattern as GetExportRangesActivity.
/// </summary>
public class GetReindexRangesActivity(
    ISearchServiceFactory searchServiceFactory,
    ILogger<GetReindexRangesActivity> logger) : AsyncTaskActivity<GetReindexRangesInput, GetReindexRangesOutput>
{
    private readonly AsyncRetryPolicy _retryPolicy = Policy
        .Handle<SqlException>(IsTransientSqlError)
        .Or<TimeoutException>()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
            onRetry: (exception, timespan, retryCount, context) =>
            {
                logger.LogWarning(
                    exception,
                    "Reindex ranges query failed (attempt {Attempt}/3). Retrying after {DelayMs}ms. Error: {ErrorMessage}",
                    retryCount,
                    timespan.TotalMilliseconds,
                    exception.Message);
            });

    private static bool IsTransientSqlError(SqlException ex)
    {
        // SQL Server transient error numbers:
        // 1205 = Deadlock
        // -2 = Timeout
        // 10053 = Connection forcibly closed
        // 10054 = Connection reset
        // 10060 = Connection attempt failed
        // 40197 = Service error processing request
        // 40501 = Service busy
        // 40613 = Database unavailable
        return ex.Number is 1205 or -2 or 10053 or 10054 or 10060 or 40197 or 40501 or 40613;
    }

    protected override async Task<GetReindexRangesOutput> ExecuteAsync(
        TaskContext context,
        GetReindexRangesInput input)
    {
        logger.LogInformation(
            "Getting reindex ranges: TenantId={TenantId}, ResourceType={ResourceType}, NumberOfRanges={NumberOfRanges}",
            input.TenantId,
            input.ResourceType,
            input.NumberOfRanges);

        try
        {
            var searchService = await _retryPolicy.ExecuteAsync(async () =>
                await searchServiceFactory.GetSearchServiceAsync(
                    input.TenantId,
                    CancellationToken.None));

            var ranges = await _retryPolicy.ExecuteAsync(async () =>
                await searchService.GetExportRangesAsync(
                    input.ResourceType,
                    input.NumberOfRanges,
                    CancellationToken.None));

            logger.LogInformation(
                "Determined reindex ranges: TenantId={TenantId}, ResourceType={ResourceType}, RangeCount={RangeCount}",
                input.TenantId,
                input.ResourceType,
                ranges.Count);

            if (ranges.Count > 0)
            {
                for (int i = 0; i < ranges.Count; i++)
                {
                    var (startId, endId) = ranges[i];
                    var rangeSize = endId - startId + 1;
                    logger.LogDebug(
                        "Range {Index}: [{StartId}..{EndId}] ({Size} IDs in range)",
                        i + 1,
                        startId,
                        endId,
                        rangeSize);
                }
            }
            else
            {
                logger.LogInformation(
                    "No resources found for reindex: ResourceType={ResourceType}",
                    input.ResourceType);
            }

            return new GetReindexRangesOutput(
                ResourceType: input.ResourceType,
                Ranges: ranges);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to determine reindex ranges: TenantId={TenantId}, ResourceType={ResourceType}",
                input.TenantId,
                input.ResourceType);

            throw;
        }
    }
}
