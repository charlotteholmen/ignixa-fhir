// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;

/// <summary>
/// Durable Task orchestration for reindexing SearchParameters.
/// Follows the same pattern as ExportOrchestration:
/// 1. Emit SearchParameterReindexStarted events for each SearchParameter
/// 2. Partition resource type into surrogate ID ranges
/// 3. Fan-out to parallel ReindexWorkerActivity instances
/// 4. Wait for all workers to complete
/// 5. Emit SearchParameterReindexCompleted/Failed events
///
/// This design achieves high throughput by:
/// - Eliminating pagination (using surrogate ID ranges)
/// - Parallel execution of 8-16 worker activities per resource type
/// - Streaming resources directly from DB to indexing pipeline
/// </summary>
public class ReindexOrchestration : TaskOrchestration<ReindexOrchestrationOutput, ReindexOrchestrationInput>
{
    /// <summary>
    /// Valid range for NumberOfRangesPerType configuration.
    /// Minimum: 1 (no parallelism), Maximum: 16 (high parallelism but DurableTask overhead).
    /// </summary>
    private const int MinRangesPerType = 1;
    private const int MaxRangesPerType = 16;

    public override async Task<ReindexOrchestrationOutput> RunTask(
        OrchestrationContext context,
        ReindexOrchestrationInput input)
    {
        var startTime = context.CurrentUtcDateTime;
        var workerResults = new List<ReindexWorkerOutput>();
        long totalResourcesReindexed = 0;

        try
        {
            // Validate NumberOfRangesPerType - fail fast if out of range
            if (input.NumberOfRangesPerType < MinRangesPerType || input.NumberOfRangesPerType > MaxRangesPerType)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input),
                    input.NumberOfRangesPerType,
                    $"NumberOfRangesPerType must be between {MinRangesPerType} and {MaxRangesPerType}. " +
                    $"Lower values reduce parallelism but decrease DurableTask overhead. " +
                    $"Higher values increase parallelism but may overwhelm the task queue.");
            }

            // Phase 1: Emit SearchParameterReindexStarted events
            try
            {
                await context.ScheduleTask<bool>(
                    typeof(EmitReindexEventsActivity),
                    new EmitReindexEventsInput(
                        TenantId: input.TenantId,
                        ResourceType: input.ResourceType,
                        SearchParameters: input.SearchParameters,
                        JobId: input.JobId,
                        IsStart: true));
            }
            catch (Exception)
            {
                // Failure during event emission - this is non-critical, continue with reindex
                // Events are for observability, not required for correctness
                // Log warning but don't fail the entire orchestration
                // (In production, would use structured logging here)
            }

            // Phase 2: Determine partitions (surrogate ID ranges) for this resource type
            GetReindexRangesOutput rangesOutput;
            try
            {
                var getRangesInput = new GetReindexRangesInput(
                    TenantId: input.TenantId,
                    ResourceType: input.ResourceType,
                    NumberOfRanges: input.NumberOfRangesPerType);

                rangesOutput = await context.ScheduleTask<GetReindexRangesOutput>(
                    typeof(GetReindexRangesActivity),
                    getRangesInput);
            }
            catch (Exception ex)
            {
                // Failure during initialization (GetReindexRangesActivity)
                try
                {
                    await context.ScheduleTask<bool>(
                        typeof(EmitReindexEventsActivity),
                        new EmitReindexEventsInput(
                            TenantId: input.TenantId,
                            ResourceType: input.ResourceType,
                            SearchParameters: input.SearchParameters,
                            JobId: input.JobId,
                            IsStart: false,
                            ErrorMessage: $"Reindex initialization failed: {ex.Message}"));
                }
                catch
                {
                    // Ignore failure to emit - already failing
                }

                return new ReindexOrchestrationOutput(
                    Success: false,
                    TotalResourcesReindexed: 0,
                    WorkerResults: null,
                    ErrorMessage: $"Reindex initialization failed: {ex.Message}",
                    FailurePhase: "Initialization");
            }

            // If no resources to reindex, emit completion with 0 resources
            if (rangesOutput.Ranges.Count == 0)
            {
                try
                {
                    await context.ScheduleTask<bool>(
                        typeof(EmitReindexEventsActivity),
                        new EmitReindexEventsInput(
                            TenantId: input.TenantId,
                            ResourceType: input.ResourceType,
                            SearchParameters: input.SearchParameters,
                            JobId: input.JobId,
                            IsStart: false,
                            ResourcesIndexed: 0,
                            Duration: context.CurrentUtcDateTime - startTime));
                }
                catch
                {
                    // Ignore failure to emit - already completing successfully
                }

                return new ReindexOrchestrationOutput(
                    Success: true,
                    TotalResourcesReindexed: 0,
                    WorkerResults: Array.Empty<ReindexWorkerOutput>(),
                    ErrorMessage: null,
                    FailurePhase: null);
            }

            // Phase 3: Fan-out to workers
            var allWorkerTasks = new List<Task<ReindexWorkerOutput>>();
            try
            {
                foreach (var (startId, endId) in rangesOutput.Ranges)
                {
                    var workerInput = new ReindexWorkerInput(
                        JobId: input.JobId,
                        TenantId: input.TenantId,
                        ResourceType: input.ResourceType,
                        StartSurrogateId: startId,
                        EndSurrogateId: endId,
                        SearchParameters: input.SearchParameters);

                    // Schedule worker task (doesn't wait - queues for parallel execution)
                    var workerTask = context.ScheduleTask<ReindexWorkerOutput>(
                        typeof(ReindexWorkerActivity),
                        workerInput);

                    allWorkerTasks.Add(workerTask);
                }
            }
            catch (Exception ex)
            {
                // Failure during worker scheduling
                try
                {
                    await context.ScheduleTask<bool>(
                        typeof(EmitReindexEventsActivity),
                        new EmitReindexEventsInput(
                            TenantId: input.TenantId,
                            ResourceType: input.ResourceType,
                            SearchParameters: input.SearchParameters,
                            JobId: input.JobId,
                            IsStart: false,
                            ErrorMessage: $"Worker scheduling failed: {ex.Message}"));
                }
                catch
                {
                    // Ignore failure to emit - already failing
                }

                return new ReindexOrchestrationOutput(
                    Success: false,
                    TotalResourcesReindexed: 0,
                    WorkerResults: null,
                    ErrorMessage: $"Worker scheduling failed: {ex.Message}",
                    FailurePhase: "Initialization");
            }

            // Phase 4: Wait for ALL worker activities to complete in parallel
            // This is where we achieve high throughput - 8-16 workers running simultaneously
            ReindexWorkerOutput[] completedWorkers;
            try
            {
                completedWorkers = await Task.WhenAll(allWorkerTasks);
            }
            catch (Exception ex)
            {
                // Failure during worker execution (one or more workers failed)
                try
                {
                    await context.ScheduleTask<bool>(
                        typeof(EmitReindexEventsActivity),
                        new EmitReindexEventsInput(
                            TenantId: input.TenantId,
                            ResourceType: input.ResourceType,
                            SearchParameters: input.SearchParameters,
                            JobId: input.JobId,
                            IsStart: false,
                            ErrorMessage: $"Worker execution failed: {ex.Message}"));
                }
                catch
                {
                    // Ignore failure to emit - already failing
                }

                return new ReindexOrchestrationOutput(
                    Success: false,
                    TotalResourcesReindexed: totalResourcesReindexed,
                    WorkerResults: workerResults.AsReadOnly(),
                    ErrorMessage: $"Worker execution failed: {ex.Message}",
                    FailurePhase: "WorkerExecution");
            }

            // Phase 5: Aggregate results from all workers
            foreach (var workerOutput in completedWorkers)
            {
                workerResults.Add(workerOutput);
                totalResourcesReindexed += workerOutput.ResourcesProcessed;
            }

            // Phase 6: Emit SearchParameterReindexCompleted events
            try
            {
                await context.ScheduleTask<bool>(
                    typeof(EmitReindexEventsActivity),
                    new EmitReindexEventsInput(
                        TenantId: input.TenantId,
                        ResourceType: input.ResourceType,
                        SearchParameters: input.SearchParameters,
                        JobId: input.JobId,
                        IsStart: false,
                        ResourcesIndexed: totalResourcesReindexed,
                        Duration: context.CurrentUtcDateTime - startTime));
            }
            catch
            {
                // Ignore failure to emit - already completing successfully
            }

            // Return success result with detailed worker outputs
            return new ReindexOrchestrationOutput(
                Success: true,
                TotalResourcesReindexed: totalResourcesReindexed,
                WorkerResults: workerResults.AsReadOnly(),
                ErrorMessage: null,
                FailurePhase: null);
        }
        catch (Exception ex)
        {
            // Unexpected failure during aggregation or final processing
            // This should be rare - most failures are caught in the specific phases above
            try
            {
                await context.ScheduleTask<bool>(
                    typeof(EmitReindexEventsActivity),
                    new EmitReindexEventsInput(
                        TenantId: input.TenantId,
                        ResourceType: input.ResourceType,
                        SearchParameters: input.SearchParameters,
                        JobId: input.JobId,
                        IsStart: false,
                        ErrorMessage: $"Unexpected failure during reindex: {ex.Message}"));
            }
            catch
            {
                // Ignore failure to emit - already failing
            }

            return new ReindexOrchestrationOutput(
                Success: false,
                TotalResourcesReindexed: totalResourcesReindexed,
                WorkerResults: workerResults.AsReadOnly(),
                ErrorMessage: $"Unexpected failure during reindex: {ex.Message}",
                FailurePhase: "Aggregation");
        }
    }
}
