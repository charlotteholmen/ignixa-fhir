// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Polly;
using Polly.Retry;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

/// <summary>
/// DurableTask activity that reindexes resources within a surrogate ID range.
/// For each resource in the range, extracts search parameter values for any SP
/// where the resource's TransactionId is less than or equal to the SP's ActivationTransactionId.
///
/// Processing flow:
/// 1. Stream resources from database using GetResourcesForReindexAsync
/// 2. For each resource, determine which SPs need indexing based on TransactionId
/// 3. Parse resource and extract search indices for applicable SPs
/// 4. Batch upsert search index entries back to database
/// </summary>
public class ReindexWorkerActivity(
    ISearchServiceFactory searchServiceFactory,
    IFhirRepositoryFactory repositoryFactory,
    IFhirVersionContext fhirVersionContext,
    ITenantConfigurationStore tenantConfigurationStore,
    ILogger<ReindexWorkerActivity> logger) : AsyncTaskActivity<ReindexWorkerInput, ReindexWorkerOutput>
{
    private const int BatchSize = 100;

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
                    "Reindex database operation failed (attempt {Attempt}/3). Retrying after {DelayMs}ms. Error: {ErrorMessage}",
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

    protected override async Task<ReindexWorkerOutput> ExecuteAsync(
        TaskContext context,
        ReindexWorkerInput input)
    {
        logger.LogInformation(
            "Reindex worker starting: JobId={JobId}, ResourceType={ResourceType}, Range=[{Start}..{End}], SPs={SPCount}",
            input.JobId,
            input.ResourceType,
            input.StartSurrogateId,
            input.EndSurrogateId,
            input.SearchParameters.Count);

        try
        {
            var tenantConfig = await _retryPolicy.ExecuteAsync(async () =>
                await tenantConfigurationStore.GetTenantConfigurationAsync(
                    input.TenantId,
                    CancellationToken.None));

            if (tenantConfig is null)
            {
                throw new InvalidOperationException($"Tenant {input.TenantId} not found or inactive");
            }

            var searchService = await _retryPolicy.ExecuteAsync(async () =>
                await searchServiceFactory.GetSearchServiceAsync(
                    input.TenantId,
                    CancellationToken.None));

            var repository = await _retryPolicy.ExecuteAsync(async () =>
                await repositoryFactory.GetRepositoryAsync(
                    input.TenantId,
                    CancellationToken.None));

            var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
            var schemaProvider = fhirVersionContext.GetSchemaProvider(fhirVersion, input.TenantId);
            var searchIndexer = fhirVersionContext.GetSearchIndexer(fhirVersion, input.TenantId);

            long resourcesProcessed = 0;
            long indexEntriesCreated = 0;

            var maxTransactionId = input.SearchParameters.Max(sp => sp.ActivationTransactionId);

            var batchIndices = new List<(long SurrogateId, IReadOnlyList<SearchIndexEntry> Entries)>();
            var applicableSpUrlsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var applicableSPsList = new List<ReindexSearchParam>();

            await foreach (var resource in searchService.GetResourcesForReindexAsync(
                input.ResourceType,
                input.StartSurrogateId,
                input.EndSurrogateId,
                maxTransactionId,
                CancellationToken.None))
            {
                applicableSPsList.Clear();
                foreach (var sp in input.SearchParameters)
                {
                    if (resource.TransactionId <= sp.ActivationTransactionId)
                    {
                        applicableSPsList.Add(sp);
                    }
                }

                if (applicableSPsList.Count == 0)
                    continue;

                try
                {
                    var resourceNode = JsonSourceNodeFactory.Parse(resource.ResourceBytes);
                    var typedElement = resourceNode.ToElement(schemaProvider);

                    var allIndices = searchIndexer.Extract((IElement)typedElement);

                    applicableSpUrlsSet.Clear();
                    foreach (var sp in applicableSPsList)
                    {
                        applicableSpUrlsSet.Add(sp.Canonical);
                    }

                    var filteredIndices = allIndices
                        .Where(entry => entry.SearchParameter.Url is not null &&
                                       applicableSpUrlsSet.Contains(entry.SearchParameter.Url.ToString()))
                        .ToList();

                    if (filteredIndices.Count > 0)
                    {
                        batchIndices.Add((resource.SurrogateId, filteredIndices));
                        indexEntriesCreated += filteredIndices.Count;
                    }

                    resourcesProcessed++;

                    if (batchIndices.Count >= BatchSize)
                    {
                        await _retryPolicy.ExecuteAsync(async () =>
                            await repository.UpsertSearchIndicesAsync(batchIndices, CancellationToken.None));
                        batchIndices.Clear();

                        logger.LogDebug(
                            "Reindex progress: JobId={JobId}, Processed={Processed}, Entries={Entries}",
                            input.JobId,
                            resourcesProcessed,
                            indexEntriesCreated);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to extract search indices for resource {ResourceType}/{ResourceId}: {Message}",
                        resource.ResourceType,
                        resource.ResourceId,
                        ex.Message);
                }
            }

            if (batchIndices.Count > 0)
            {
                await _retryPolicy.ExecuteAsync(async () =>
                    await repository.UpsertSearchIndicesAsync(batchIndices, CancellationToken.None));
            }

            logger.LogInformation(
                "Reindex worker complete: JobId={JobId}, ResourceType={ResourceType}, Processed={Processed}, Entries={Entries}",
                input.JobId,
                input.ResourceType,
                resourcesProcessed,
                indexEntriesCreated);

            return new ReindexWorkerOutput(
                ResourceType: input.ResourceType,
                StartSurrogateId: input.StartSurrogateId,
                EndSurrogateId: input.EndSurrogateId,
                ResourcesProcessed: resourcesProcessed,
                IndexEntriesCreated: indexEntriesCreated);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Reindex worker failed: JobId={JobId}, ResourceType={ResourceType}, Range=[{Start}..{End}]",
                input.JobId,
                input.ResourceType,
                input.StartSurrogateId,
                input.EndSurrogateId);

            throw;
        }
    }
}
