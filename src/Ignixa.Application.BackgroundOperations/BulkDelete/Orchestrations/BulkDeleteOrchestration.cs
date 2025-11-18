// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete.Activities;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;

/// <summary>
/// Durable Task orchestration for FHIR bulk delete operations.
/// Implements the bulk delete workflow:
/// 1. Identifies resources to delete using search criteria
/// 2. Partitions resources into batches for parallel processing
/// 3. Deletes each batch in parallel
/// 4. Aggregates results and returns summary
///
/// Supports both system-level (all types) and type-specific deletions.
/// Supports soft delete (default) and hard delete modes.
/// </summary>
public class BulkDeleteOrchestration : TaskOrchestration<BulkDeleteOrchestrationOutput, BulkDeleteOrchestrationInput>
{
    private const int BatchSize = 100; // Delete resources in batches of 100

    public override async Task<BulkDeleteOrchestrationOutput> RunTask(
        OrchestrationContext context,
        BulkDeleteOrchestrationInput input)
    {
        var deletedResourcesByType = new Dictionary<string, long>();
        long totalResourcesDeleted = 0;

        try
        {
            // Phase 1: Identify resources to delete
            var getResourcesInput = new GetResourcesToDeleteInput(
                TenantId: input.TenantId,
                ResourceType: input.ResourceType,
                SearchQuery: input.SearchQuery,
                ExcludedResourceTypes: input.ExcludedResourceTypes,
                NotReferencedBy: input.NotReferencedBy);

            GetResourcesToDeleteOutput resourcesToDelete;
            try
            {
                resourcesToDelete = await context.ScheduleTask<GetResourcesToDeleteOutput>(
                    typeof(GetResourcesToDeleteActivity),
                    getResourcesInput);
            }
            catch (Exception ex)
            {
                return new BulkDeleteOrchestrationOutput(
                    Success: false,
                    TotalResourcesDeleted: 0,
                    DeletedResourcesByType: null,
                    ErrorMessage: $"Failed to identify resources to delete: {ex.Message}",
                    FailurePhase: "Identification");
            }

            if (resourcesToDelete.TotalCount == 0)
            {
                // No resources to delete - success with zero deletions
                return new BulkDeleteOrchestrationOutput(
                    Success: true,
                    TotalResourcesDeleted: 0,
                    DeletedResourcesByType: new Dictionary<string, long>(),
                    ErrorMessage: null,
                    FailurePhase: null);
            }

            // Phase 2: Delete resources in parallel batches
            var deleteTasks = new List<Task<DeleteResourceBatchOutput>>();

            foreach (var (resourceType, resourceIds) in resourcesToDelete.ResourcesByType)
            {
                // Partition resource IDs into batches
                var batches = PartitionIntoBatches(resourceIds, BatchSize);

                foreach (var batch in batches)
                {
                    var deleteInput = new DeleteResourceBatchInput(
                        JobId: input.JobId,
                        TenantId: input.TenantId,
                        ResourceType: resourceType,
                        ResourceIds: batch,
                        HardDelete: input.HardDelete,
                        PurgeHistory: input.PurgeHistory);

                    var deleteTask = context.ScheduleTask<DeleteResourceBatchOutput>(
                        typeof(DeleteResourceBatchActivity),
                        deleteInput);

                    deleteTasks.Add(deleteTask);
                }
            }

            // Wait for all delete tasks to complete
            DeleteResourceBatchOutput[] batchResults;
            try
            {
                batchResults = await Task.WhenAll(deleteTasks);
            }
            catch (Exception ex)
            {
                return new BulkDeleteOrchestrationOutput(
                    Success: false,
                    TotalResourcesDeleted: totalResourcesDeleted,
                    DeletedResourcesByType: deletedResourcesByType.Count > 0 ? deletedResourcesByType : null,
                    ErrorMessage: $"Failed during resource deletion: {ex.Message}",
                    FailurePhase: "Deletion");
            }

            // Phase 3: Aggregate results
            foreach (var batchResult in batchResults)
            {
                if (!deletedResourcesByType.ContainsKey(batchResult.ResourceType))
                {
                    deletedResourcesByType[batchResult.ResourceType] = 0;
                }

                deletedResourcesByType[batchResult.ResourceType] += batchResult.DeletedCount;
                totalResourcesDeleted += batchResult.DeletedCount;
            }

            // Check if any batches had errors
            var allErrors = batchResults
                .Where(r => r.Errors != null && r.Errors.Count > 0)
                .SelectMany(r => r.Errors!)
                .ToList();

            var success = allErrors.Count == 0;
            var errorMessage = success
                ? null
                : $"Completed with {allErrors.Count} errors. First error: {allErrors.First()}";

            return new BulkDeleteOrchestrationOutput(
                Success: success,
                TotalResourcesDeleted: totalResourcesDeleted,
                DeletedResourcesByType: deletedResourcesByType,
                ErrorMessage: errorMessage,
                FailurePhase: success ? null : "PartialFailure");
        }
        catch (Exception ex)
        {
            return new BulkDeleteOrchestrationOutput(
                Success: false,
                TotalResourcesDeleted: totalResourcesDeleted,
                DeletedResourcesByType: deletedResourcesByType.Count > 0 ? deletedResourcesByType : null,
                ErrorMessage: $"Unexpected failure during bulk delete: {ex.Message}",
                FailurePhase: "Orchestration");
        }
    }

    private static List<IReadOnlyList<string>> PartitionIntoBatches(
        IReadOnlyList<string> items,
        int batchSize)
    {
        var batches = new List<IReadOnlyList<string>>();

        for (int i = 0; i < items.Count; i += batchSize)
        {
            var batch = items.Skip(i).Take(batchSize).ToList();
            batches.Add(batch);
        }

        return batches;
    }
}
