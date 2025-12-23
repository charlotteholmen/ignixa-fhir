// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Activities;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;

namespace Ignixa.Application.BackgroundOperations.TtlCleanup.Orchestrations;

/// <summary>
/// DurableTask orchestration for TTL (Time-To-Live) cleanup operations.
/// Uses the "eternal orchestration" pattern - runs forever with periodic cleanup cycles.
///
/// Each cycle:
/// 1. Gets list of active tenants from ITenantConfigurationStore
/// 2. For each tenant (except system partition 0), schedules TtlCleanupActivity in parallel
/// 3. Waits for all activities to complete
/// 4. Sleeps for ScanInterval using a durable timer
/// 5. Calls ContinueAsNew to restart with fresh state (truncates history)
///
/// Benefits of eternal orchestration pattern:
/// - Schedule survives application restarts (state is persisted)
/// - Durable timers are reliable (no missed cycles)
/// - History is truncated on each cycle (bounded storage)
/// - Single instance management (use fixed instance ID)
/// </summary>
public class TtlCleanupOrchestration(ITenantConfigurationStore tenantConfigurationStore)
    : TaskOrchestration<TtlCleanupOrchestrationOutput, TtlCleanupOrchestrationInput>
{
    /// <summary>
    /// Well-known instance ID for the singleton TTL cleanup orchestration.
    /// Using a fixed ID ensures only one instance runs at a time.
    /// </summary>
    public const string SingletonInstanceId = "ttl-cleanup-eternal";

    private readonly ITenantConfigurationStore _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));

    public override async Task<TtlCleanupOrchestrationOutput> RunTask(
        OrchestrationContext context,
        TtlCleanupOrchestrationInput input)
    {
        var tenantResults = new List<TtlCleanupActivityOutput>();
        int totalExpired = 0;
        int totalDeleted = 0;
        int totalFailed = 0;

        try
        {
            // Phase 1: Get all active tenants
            var tenants = await _tenantConfigurationStore.GetAllTenantsAsync(CancellationToken.None);

            // Filter out system partition (Partition 0) and inactive tenants
            var activeTenants = tenants
                .Where(t => t.TenantId != SystemConstants.SystemPartitionId && t.IsActive)
                .ToList();

            if (activeTenants.Count > 0)
            {
                // Phase 2: Schedule cleanup activity for each active tenant
                var activityTasks = new List<Task<TtlCleanupActivityOutput>>();

                foreach (var tenant in activeTenants)
                {
                    var activityInput = new TtlCleanupActivityInput(
                        TenantId: tenant.TenantId,
                        BatchSize: input.BatchSize);

                    var activityTask = context.ScheduleTask<TtlCleanupActivityOutput>(
                        typeof(TtlCleanupActivity),
                        activityInput);

                    activityTasks.Add(activityTask);
                }

                // Phase 3: Wait for all activities to complete in parallel
                var completedActivities = await Task.WhenAll(activityTasks);

                // Phase 4: Aggregate results from all tenants
                foreach (var activityOutput in completedActivities)
                {
                    tenantResults.Add(activityOutput);
                    totalExpired += activityOutput.ExpiredCount;
                    totalDeleted += activityOutput.DeletedCount;
                    totalFailed += activityOutput.FailedCount;
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but continue - eternal orchestration should not die
            // The next cycle will retry
            return await ScheduleNextCycleAsync(
                context,
                input,
                new TtlCleanupOrchestrationOutput(
                    Success: false,
                    TotalExpired: totalExpired,
                    TotalDeleted: totalDeleted,
                    TotalFailed: totalFailed,
                    TenantResults: tenantResults.AsReadOnly(),
                    ErrorMessage: $"TTL cleanup cycle failed: {ex.Message}"));
        }

        // Phase 5: Schedule next cycle and continue as new
        return await ScheduleNextCycleAsync(
            context,
            input,
            new TtlCleanupOrchestrationOutput(
                Success: true,
                TotalExpired: totalExpired,
                TotalDeleted: totalDeleted,
                TotalFailed: totalFailed,
                TenantResults: tenantResults.AsReadOnly(),
                ErrorMessage: null));
    }

    /// <summary>
    /// Schedules the next cleanup cycle using a durable timer and ContinueAsNew.
    /// This is the core of the eternal orchestration pattern.
    /// </summary>
    private static async Task<TtlCleanupOrchestrationOutput> ScheduleNextCycleAsync(
        OrchestrationContext context,
        TtlCleanupOrchestrationInput input,
        TtlCleanupOrchestrationOutput currentResult)
    {
        // Wait for the scan interval using a durable timer
        // Durable timers survive orchestration replays and app restarts
        var nextCycleTime = context.CurrentUtcDateTime.Add(input.EffectiveScanInterval);
        await context.CreateTimer(nextCycleTime, true);

        // ContinueAsNew restarts the orchestration with fresh state
        // This truncates the history, preventing unbounded growth
        context.ContinueAsNew(input);

        // This return is never reached due to ContinueAsNew, but required for compilation
        return currentResult;
    }
}
