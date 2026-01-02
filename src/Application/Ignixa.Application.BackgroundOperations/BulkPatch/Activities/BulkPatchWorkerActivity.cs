// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkPatch.Models;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkPatch.Activities;

public class BulkPatchWorkerActivity(
    ISearchServiceFactory searchServiceFactory,
    IFhirRepositoryFactory repositoryFactory,
    IFhirVersionContext fhirVersionContext,
    ITenantConfigurationStore tenantConfigurationStore,
    IQueryParameterParser parameterParser,
    ISearchOptionsBuilder searchOptionsBuilder,
    FhirPatchEngine patchEngine,
    ILogger<BulkPatchWorkerActivity> logger)
    : AsyncTaskActivity<BulkPatchWorkerInput, BulkPatchWorkerOutput>
{
    private const int MaxIssuesPerWorker = 100;
    protected override async Task<BulkPatchWorkerOutput> ExecuteAsync(
        TaskContext context,
        BulkPatchWorkerInput input)
    {
        var startTime = DateTimeOffset.UtcNow;

        logger.LogInformation(
            "Processing bulk patch for {ResourceType} ({StartId}-{EndId})",
            input.ResourceType,
            input.StartSurrogateId,
            input.EndSurrogateId);

        var tenantConfig = await tenantConfigurationStore.GetTenantConfigurationAsync(
            input.TenantId,
            CancellationToken.None);

        if (tenantConfig == null)
        {
            throw new InvalidOperationException($"Tenant {input.TenantId} not found or inactive");
        }

        var searchService = await searchServiceFactory.GetSearchServiceAsync(
            input.TenantId,
            CancellationToken.None);

        var repository = await repositoryFactory.GetRepositoryAsync(
            input.TenantId,
            CancellationToken.None);

        var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        var schemaProvider = fhirVersionContext.GetSchemaProvider(fhirVersion, input.TenantId);
        var searchIndexer = fhirVersionContext.GetSearchIndexer(fhirVersion, input.TenantId);

        var patchOperations = ConvertToPatchOperations(input.Operations, input.ResourceType);
        if (patchOperations.Length == 0)
        {
            logger.LogDebug("No applicable operations for {ResourceType}", input.ResourceType);
            return new BulkPatchWorkerOutput(
                input.ResourceType,
                0,
                0,
                0,
                0,
                []);
        }

        var filterParams = parameterParser.Parse(input.SearchQuery ?? string.Empty);
        var searchOptions = searchOptionsBuilder.Build(input.ResourceType, filterParams);
        searchOptions.MaxItemCount = input.BatchSize;
        searchOptions.StartSurrogateId = input.StartSurrogateId;
        searchOptions.EndSurrogateId = input.EndSurrogateId;

        var processedCount = 0;
        var updatedCount = 0;
        var ignoredCount = 0;
        var failedCount = 0;
        var issues = new List<BulkPatchIssue>();

        var transactionId = await repository.GetNextTransactionIdAsync(CancellationToken.None);

        var batch = new List<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>();

        await foreach (var searchEntry in searchService.SearchStreamAsync(searchOptions, CancellationToken.None))
        {
            processedCount++;

            try
            {
                var resource = JsonSourceNodeFactory.Parse(searchEntry.ResourceBytes);
                if (resource == null)
                {
                    failedCount++;
                    issues.Add(new BulkPatchIssue(
                        input.ResourceType,
                        searchEntry.ResourceId,
                        "Failed to parse resource"));
                    continue;
                }

                var beforeJson = resource.SerializeToString();
                var patched = await patchEngine.ApplyPatchAsync(resource, patchOperations, CancellationToken.None);
                var afterJson = patched.SerializeToString();

                if (beforeJson == afterJson)
                {
                    ignoredCount++;
                    continue;
                }

                patched.InvalidateCaches();

                var typedElement = patched.ToElement(schemaProvider);
                var searchIndices = searchIndexer.Extract(typedElement).ToArray();

                batch.Add((
                    patched.ResourceType,
                    patched.Id ?? searchEntry.ResourceId,
                    patched,
                    searchIndices,
                    "PATCH",
                    processedCount));

                if (batch.Count >= input.BatchSize)
                {
                    var keys = await repository.BatchWriteAsync(transactionId, batch, CancellationToken.None);
                    updatedCount += keys.Count;
                    batch.Clear();
                }
            }
            catch (FhirPatchException ex)
            {
                failedCount++;
                if (issues.Count < MaxIssuesPerWorker)
                {
                    issues.Add(new BulkPatchIssue(
                        input.ResourceType,
                        searchEntry.ResourceId,
                        ex.Message));
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                if (issues.Count < MaxIssuesPerWorker)
                {
                    issues.Add(new BulkPatchIssue(
                        input.ResourceType,
                        searchEntry.ResourceId,
                        $"Unexpected error: {ex.Message}"));
                }
            }
        }

        if (batch.Count > 0)
        {
            var keys = await repository.BatchWriteAsync(transactionId, batch, CancellationToken.None);
            updatedCount += keys.Count;
        }

        await repository.CommitTransactionAsync(transactionId, CancellationToken.None);

        var elapsed = DateTimeOffset.UtcNow - startTime;
        var resourcesPerSecond = elapsed.TotalSeconds > 0
            ? processedCount / elapsed.TotalSeconds
            : 0;

        logger.LogInformation(
            "Completed {ResourceType}: {Processed} processed, {Updated} updated, {Ignored} ignored, {Failed} failed ({ResourcesPerSec:F2} resources/sec)",
            input.ResourceType,
            processedCount,
            updatedCount,
            ignoredCount,
            failedCount,
            resourcesPerSecond);

        return new BulkPatchWorkerOutput(
            input.ResourceType,
            processedCount,
            updatedCount,
            ignoredCount,
            failedCount,
            issues);
    }

    private static FhirPatchOperation[] ConvertToPatchOperations(
        IReadOnlyList<BulkPatchOperationDefinition> operations,
        string resourceType)
    {
        var result = new List<FhirPatchOperation>();

        foreach (var op in operations)
        {
            if (string.IsNullOrEmpty(op.Path))
            {
                throw new FhirPatchException("Patch operation requires non-empty 'path'");
            }

            var pathParts = op.Path.Split('.', 2);
            if (pathParts.Length < 2)
            {
                throw new FhirPatchException($"Path must be in format 'ResourceType.field' or 'Resource.field', got: {op.Path}");
            }

            var pathResourceType = pathParts[0];
            if (pathResourceType != "Resource" && pathResourceType != resourceType)
            {
                continue;
            }

            var opType = op.Type.ToUpperInvariant() switch
            {
                "REPLACE" => FhirPatchOperationType.Replace,
                "ADD" or "UPSERT" => FhirPatchOperationType.Add,
                _ => throw new FhirPatchException($"Unsupported operation type: {op.Type}")
            };

            result.Add(new FhirPatchOperation
            {
                Type = opType,
                Path = pathParts[1],
                Value = op.Value
            });
        }

        return [.. result];
    }
}
