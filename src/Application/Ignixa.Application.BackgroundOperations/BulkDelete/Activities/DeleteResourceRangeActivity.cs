// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Abstractions;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Parsing;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Activities;

/// <summary>
/// DurableTask activity that deletes resources in a single partition (resource type + surrogate ID range).
/// Streams resources directly from database and deletes them without loading all IDs into memory.
/// Each worker instance runs independently and in parallel with other workers.
/// Follows the same high-performance pattern as ExportWorkerActivity.
/// </summary>
public class DeleteResourceRangeActivity : AsyncTaskActivity<DeleteResourceRangeInput, DeleteResourceRangeOutput>
{
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IQueryParameterParser _parameterParser;
    private readonly ISearchOptionsBuilder _searchOptionsBuilder;
    private readonly ILogger<DeleteResourceRangeActivity> _logger;

    private const int MaxErrorsToCapture = 10;

    public DeleteResourceRangeActivity(
        ISearchServiceFactory searchServiceFactory,
        IFhirRepositoryFactory repositoryFactory,
        ITenantConfigurationStore tenantConfigurationStore,
        IQueryParameterParser parameterParser,
        ISearchOptionsBuilder searchOptionsBuilder,
        ILogger<DeleteResourceRangeActivity> logger)
    {
        _searchServiceFactory = searchServiceFactory ?? throw new ArgumentNullException(nameof(searchServiceFactory));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
        _parameterParser = parameterParser ?? throw new ArgumentNullException(nameof(parameterParser));
        _searchOptionsBuilder = searchOptionsBuilder ?? throw new ArgumentNullException(nameof(searchOptionsBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<DeleteResourceRangeOutput> ExecuteAsync(
        TaskContext context,
        DeleteResourceRangeInput input)
    {
        _logger.LogInformation(
            "Starting bulk delete worker: Job={JobId}, Type={ResourceType}, Range=[{StartId}..{EndId}], HardDelete={HardDelete}",
            input.JobId,
            input.ResourceType,
            input.StartSurrogateId,
            input.EndSurrogateId,
            input.HardDelete);

        try
        {
            // Get tenant configuration
            var tenantConfig = await _tenantConfigurationStore.GetTenantConfigurationAsync(
                input.TenantId,
                CancellationToken.None);

            if (tenantConfig == null)
            {
                throw new InvalidOperationException($"Tenant {input.TenantId} not found or inactive");
            }

            // Get search service and repository for this tenant
            var searchService = await _searchServiceFactory.GetSearchServiceAsync(
                input.TenantId,
                CancellationToken.None);

            var repository = await _repositoryFactory.GetRepositoryAsync(
                input.TenantId,
                CancellationToken.None);

            long deletedCount = 0;
            long failedCount = 0;
            var errors = new List<string>();

            try
            {
                // Build search query string with all applicable filters
                var queryStringBuilder = new System.Text.StringBuilder();

                // Add user-provided search filters
                if (!string.IsNullOrWhiteSpace(input.SearchQuery))
                {
                    queryStringBuilder.Append(input.SearchQuery);
                }

                // Parse filter parameters and build SearchOptions
                var filterParams = _parameterParser.Parse(queryStringBuilder.ToString());
                var searchOptions = _searchOptionsBuilder.Build(input.ResourceType, filterParams);

                // Add partition boundaries (surrogate ID range)
                searchOptions.MaxItemCount = 1000;  // Process in smaller batches for delete
                searchOptions.StartSurrogateId = input.StartSurrogateId;
                searchOptions.EndSurrogateId = input.EndSurrogateId;
                searchOptions.IncludeHistory = input.PurgeHistory;
                searchOptions.NotReferencedFilters = input.NotReferencedBy.ToList();

                // Log filter application
                if (filterParams.Count > 0)
                {
                    _logger.LogDebug(
                        "Bulk delete worker applying filters: Job={JobId}, Type={ResourceType}, Filters={FilterCount}",
                        input.JobId,
                        input.ResourceType,
                        filterParams.Count);
                }

                // Stream results directly from database and delete each resource
                // This single async enumeration runs until ALL resources in the range are processed
                await foreach (var resource in searchService.SearchStreamAsync(searchOptions, CancellationToken.None))
                {
                    try
                    {
                        // If purging history, we target the specific version returned by search
                        // If hard deleting current, we might target version or just resource ID (depending on repo impl)
                        var targetVersion = input.PurgeHistory ? resource.VersionId : null;
                        var key = new ResourceKey(input.ResourceType, resource.ResourceId, targetVersion);

                        if (input.HardDelete)
                        {
                            await repository.HardDeleteAsync(key, CancellationToken.None);
                            deletedCount++;
                        }
                        else
                        {
                            // Perform soft delete (standard FHIR delete)
                            var request = new ResourceRequest("DELETE", $"/{input.ResourceType}/{resource.ResourceId}");
                            var result = await repository.DeleteAsync(key, request, null, CancellationToken.None);

                            if (result != null)
                            {
                                deletedCount++;
                            }
                            else
                            {
                                // Resource never existed (should be rare in range-based query)
                                failedCount++;
                                if (errors.Count < MaxErrorsToCapture)
                                {
                                    errors.Add($"{input.ResourceType}/{resource.ResourceId}: Resource not found");
                                }
                            }
                        }

                        // Log progress periodically (every 1K resources)
                        if ((deletedCount + failedCount) % 1000 == 0)
                        {
                            _logger.LogInformation(
                                "Delete progress: Job={JobId}, Type={ResourceType}, Range=[{StartId}..{EndId}], Deleted={Deleted}, Failed={Failed}",
                                input.JobId,
                                input.ResourceType,
                                input.StartSurrogateId,
                                input.EndSurrogateId,
                                deletedCount,
                                failedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        if (errors.Count < MaxErrorsToCapture)
                        {
                            errors.Add($"{input.ResourceType}/{resource.ResourceId}: {ex.Message}");
                        }

                        _logger.LogWarning(
                            ex,
                            "Failed to delete resource: Job={JobId}, ResourceType={ResourceType}, ResourceId={ResourceId}",
                            input.JobId,
                            input.ResourceType,
                            resource.ResourceId);
                    }
                }

                _logger.LogInformation(
                    "Completed bulk delete worker: Job={JobId}, Type={ResourceType}, Range=[{StartId}..{EndId}], Deleted={Deleted}, Failed={Failed}",
                    input.JobId,
                    input.ResourceType,
                    input.StartSurrogateId,
                    input.EndSurrogateId,
                    deletedCount,
                    failedCount);

                return new DeleteResourceRangeOutput(
                    ResourceType: input.ResourceType,
                    StartSurrogateId: input.StartSurrogateId,
                    EndSurrogateId: input.EndSurrogateId,
                    DeletedCount: deletedCount,
                    FailedCount: failedCount,
                    Errors: errors.Count > 0 ? errors : null);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Bulk delete worker failed during processing: Job={JobId}, Type={ResourceType}, Range=[{StartId}..{EndId}]",
                    input.JobId,
                    input.ResourceType,
                    input.StartSurrogateId,
                    input.EndSurrogateId);

                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Bulk delete worker failed: Job={JobId}, Type={ResourceType}, Range=[{StartId}..{EndId}]",
                input.JobId,
                input.ResourceType,
                input.StartSurrogateId,
                input.EndSurrogateId);

            throw;
        }
    }
}
