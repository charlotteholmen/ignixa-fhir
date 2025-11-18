// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete.Activities;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Application.BackgroundOperations.Export.Activities;
using Ignixa.Application.BackgroundOperations.Export.Models;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;

/// <summary>
/// Durable Task orchestration for FHIR bulk delete operations.
/// Implements high-performance partition-based deletion:
/// 1. Determines which resource types to delete
/// 2. For EACH type: calls GetExportRangesActivity to partition into surrogate ID ranges
/// 3. For EACH partition: queues DeleteResourceRangeActivity to stream and delete resources
/// 4. Waits for all workers to complete in parallel
/// 5. Returns aggregated results
///
/// This design achieves high throughput by:
/// - Eliminating need to load all IDs into memory
/// - Streaming directly from DB to delete (no intermediate buffering)
/// - Parallel execution of multiple worker activities per resource type
/// - Zero-memory overhead (processes ranges independently)
///
/// Supports both system-level (all types) and type-specific deletions.
/// Supports soft delete (default) and hard delete modes.
/// </summary>
public class BulkDeleteOrchestration : TaskOrchestration<BulkDeleteOrchestrationOutput, BulkDeleteOrchestrationInput>
{
    /// <summary>
    /// Valid range for NumberOfRangesPerType configuration.
    /// Minimum: 1 (no parallelism per type), Maximum: 16 (high parallelism but DurableTask overhead).
    /// </summary>
    private const int MinRangesPerType = 1;
    private const int MaxRangesPerType = 16;
    private const int DefaultRangesPerType = 4; // Conservative default for delete operations

    public override async Task<BulkDeleteOrchestrationOutput> RunTask(
        OrchestrationContext context,
        BulkDeleteOrchestrationInput input)
    {
        var workerResults = new List<DeleteResourceRangeOutput>();
        long totalResourcesDeleted = 0;
        var deletedResourcesByType = new Dictionary<string, long>();

        try
        {
            // Validate NumberOfRangesPerType if provided
            var rangesPerType = input.NumberOfRangesPerType ?? DefaultRangesPerType;
            if (rangesPerType < MinRangesPerType || rangesPerType > MaxRangesPerType)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input),
                    rangesPerType,
                    $"NumberOfRangesPerType must be between {MinRangesPerType} and {MaxRangesPerType}. " +
                    $"Lower values reduce parallelism but decrease DurableTask overhead. " +
                    $"Higher values increase parallelism but may overwhelm the task queue.");
            }

            // Determine which resource types to delete
            var resourceTypes = GetResourceTypesToProcess(input);

            // Phase 1: For EACH resource type, get its surrogate ID ranges
            var allWorkerTasks = new List<Task<DeleteResourceRangeOutput>>();

            try
            {
                foreach (var resourceType in resourceTypes)
                {
                    // Step 1: Determine partitions (surrogate ID ranges) for this resource type
                    var getRangesInput = new GetExportRangesInput(
                        TenantId: input.TenantId,
                        ResourceType: resourceType,
                        NumberOfRanges: rangesPerType);

                    var rangesOutput = await context.ScheduleTask<GetExportRangesOutput>(
                        typeof(GetExportRangesActivity),
                        getRangesInput);

                    // Step 2: For EACH range, queue a worker activity (all in parallel)
                    foreach (var (startId, endId) in rangesOutput.Ranges)
                    {
                        var workerInput = new DeleteResourceRangeInput(
                            JobId: input.JobId,
                            TenantId: input.TenantId,
                            ResourceType: resourceType,
                            StartSurrogateId: startId,
                            EndSurrogateId: endId,
                            SearchQuery: input.SearchQuery,
                            HardDelete: input.HardDelete,
                            PurgeHistory: input.PurgeHistory);

                        // Schedule worker task (doesn't wait - queues for parallel execution)
                        var workerTask = context.ScheduleTask<DeleteResourceRangeOutput>(
                            typeof(DeleteResourceRangeActivity),
                            workerInput);

                        allWorkerTasks.Add(workerTask);
                    }
                }
            }
            catch (Exception ex)
            {
                // Failure during initialization (GetExportRangesActivity or scheduling)
                return new BulkDeleteOrchestrationOutput(
                    Success: false,
                    TotalResourcesDeleted: 0,
                    DeletedResourcesByType: null,
                    ErrorMessage: $"Bulk delete initialization failed: {ex.Message}",
                    FailurePhase: "Initialization");
            }

            // If no resources found to delete
            if (allWorkerTasks.Count == 0)
            {
                return new BulkDeleteOrchestrationOutput(
                    Success: true,
                    TotalResourcesDeleted: 0,
                    DeletedResourcesByType: new Dictionary<string, long>(),
                    ErrorMessage: null,
                    FailurePhase: null);
            }

            // Phase 2: Wait for ALL worker activities to complete in parallel
            // This is where we achieve high throughput - multiple workers running simultaneously
            DeleteResourceRangeOutput[] completedWorkers;
            try
            {
                completedWorkers = await Task.WhenAll(allWorkerTasks);
            }
            catch (Exception ex)
            {
                // Failure during worker execution (one or more workers failed)
                return new BulkDeleteOrchestrationOutput(
                    Success: false,
                    TotalResourcesDeleted: totalResourcesDeleted,
                    DeletedResourcesByType: deletedResourcesByType.Count > 0 ? deletedResourcesByType : null,
                    ErrorMessage: $"Worker execution failed: {ex.Message}",
                    FailurePhase: "WorkerExecution");
            }

            // Phase 3: Aggregate results from all workers
            var allErrors = new List<string>();
            foreach (var workerOutput in completedWorkers)
            {
                workerResults.Add(workerOutput);

                // Aggregate deleted counts by resource type
                if (!deletedResourcesByType.ContainsKey(workerOutput.ResourceType))
                {
                    deletedResourcesByType[workerOutput.ResourceType] = 0;
                }
                deletedResourcesByType[workerOutput.ResourceType] += workerOutput.DeletedCount;
                totalResourcesDeleted += workerOutput.DeletedCount;

                // Collect errors
                if (workerOutput.Errors != null && workerOutput.Errors.Count > 0)
                {
                    allErrors.AddRange(workerOutput.Errors);
                }
            }

            // Check if any batches had errors
            var success = allErrors.Count == 0;
            var errorMessage = success
                ? null
                : $"Completed with {allErrors.Count} errors. Sample errors: {string.Join("; ", allErrors.Take(3))}";

            return new BulkDeleteOrchestrationOutput(
                Success: success,
                TotalResourcesDeleted: totalResourcesDeleted,
                DeletedResourcesByType: deletedResourcesByType,
                ErrorMessage: errorMessage,
                FailurePhase: success ? null : "PartialFailure");
        }
        catch (Exception ex)
        {
            // Unexpected failure during aggregation or final processing
            return new BulkDeleteOrchestrationOutput(
                Success: false,
                TotalResourcesDeleted: totalResourcesDeleted,
                DeletedResourcesByType: deletedResourcesByType.Count > 0 ? deletedResourcesByType : null,
                ErrorMessage: $"Unexpected failure during bulk delete: {ex.Message}",
                FailurePhase: "Aggregation");
        }
    }

    private static List<string> GetResourceTypesToProcess(BulkDeleteOrchestrationInput input)
    {
        // If specific resource type is provided, use it
        if (!string.IsNullOrEmpty(input.ResourceType))
        {
            return new List<string> { input.ResourceType };
        }

        // System-level delete: get all resource types except excluded ones
        var allTypes = GetDefaultResourceTypes();

        if (input.ExcludedResourceTypes != null && input.ExcludedResourceTypes.Count > 0)
        {
            var excluded = new HashSet<string>(input.ExcludedResourceTypes, StringComparer.OrdinalIgnoreCase);
            return allTypes.Where(t => !excluded.Contains(t)).ToList();
        }

        return allTypes;
    }

    private static List<string> GetDefaultResourceTypes()
    {
        // Default FHIR resource types (same as export)
        return new List<string>
        {
            "Patient",
            "Observation",
            "Condition",
            "MedicationRequest",
            "Encounter",
            "Procedure",
            "DiagnosticReport",
            "AllergyIntolerance",
            "Immunization",
            "CarePlan",
            "Goal",
            "Claim",
            "Coverage",
            "ExplanationOfBenefit",
        };
    }
}
