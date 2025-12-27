// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Api.Services;

/// <summary>
/// Background service that periodically syncs ConformanceState with the event store
/// to enable multi-instance (webfarm) consistency. Polls for new events every N seconds.
/// After catching up on events, checks for Pending SearchParameters and schedules reindex orchestrations.
/// </summary>
public class ConformanceStateSyncService(
    ISourceEventStore eventStore,
    ConformanceState conformanceState,
    TaskHubClient taskHubClient,
    IServiceProvider serviceProvider,
    ILogger<ConformanceStateSyncService> logger,
    IConfiguration configuration) : BackgroundService
{
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(
        configuration.GetValue("Conformance:SyncIntervalSeconds", 30));
    private const int MaxConcurrentReindexJobsPerTenant = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for initial state to be initialized before starting sync
        while (!conformanceState.IsInitialized && !stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Waiting for ConformanceState to initialize before starting sync...");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        logger.LogInformation(
            "ConformanceStateSyncService started. Polling every {Interval}s for new events",
            _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
                await SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ConformanceStateSyncService encountered an error during sync, will retry");
            }
        }

        logger.LogInformation("ConformanceStateSyncService stopped");
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        var beforeEventId = conformanceState.LastProcessedEventId;

        await conformanceState.CatchUpAsync(eventStore, cancellationToken);

        var afterEventId = conformanceState.LastProcessedEventId;

        if (afterEventId > beforeEventId)
        {
            var eventsApplied = afterEventId - beforeEventId;
            logger.LogInformation(
                "ConformanceStateSyncService caught up: applied {EventCount} events ({Before} -> {After})",
                eventsApplied,
                beforeEventId,
                afterEventId);

            await TriggerReindexIfNeededAsync(cancellationToken);
        }
        else
        {
            logger.LogDebug("ConformanceStateSyncService: no new events (at EventId {EventId})", afterEventId);
        }
    }

    private async Task TriggerReindexIfNeededAsync(CancellationToken cancellationToken)
    {
        var pendingSPs = conformanceState.AllSearchParameters.Values
            .Where(sp => sp.Status == SearchParameterStatus.Pending)
            .GroupBy(sp => sp.ResourceType)
            .ToList();

        if (pendingSPs.Count == 0)
            return;

        using var scope = serviceProvider.CreateScope();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantConfigurationStore>();
        var allTenants = await tenantStore.GetAllTenantsAsync(cancellationToken);

        foreach (var tenantConfig in allTenants)
        {
            if (!tenantConfig.IsActive)
            {
                logger.LogDebug("Skipping reindex for inactive tenant {TenantId}", tenantConfig.TenantId);
                continue;
            }

            var runningJobsForTenant = 0;

            foreach (var group in pendingSPs)
            {
                var resourceType = group.Key;
                var searchParams = group.Select(sp => new ReindexSearchParam(
                    sp.Canonical,
                    sp.Code,
                    sp.SearchParamId,
                    sp.ActivationTransactionId
                )).ToList();

                var instanceId = $"reindex-{tenantConfig.TenantId}-{resourceType}";

                try
                {
                    var existing = await taskHubClient.GetOrchestrationStateAsync(instanceId);
                    if (existing?.OrchestrationStatus is OrchestrationStatus.Running or OrchestrationStatus.Pending)
                    {
                        logger.LogDebug(
                            "Reindex orchestration {InstanceId} already running (status: {Status}), skipping",
                            instanceId,
                            existing.OrchestrationStatus);
                        runningJobsForTenant++;
                        continue;
                    }

                    if (runningJobsForTenant >= MaxConcurrentReindexJobsPerTenant)
                    {
                        logger.LogInformation(
                            "Tenant {TenantId} has {Count} running reindex jobs, skipping {ResourceType}",
                            tenantConfig.TenantId,
                            runningJobsForTenant,
                            resourceType);
                        continue;
                    }

                    var jobId = Guid.NewGuid().ToString("N");
                    await taskHubClient.CreateOrchestrationInstanceAsync(
                        typeof(ReindexOrchestration),
                        instanceId,
                        new ReindexOrchestrationInput(jobId, tenantConfig.TenantId, resourceType, searchParams));

                    logger.LogInformation(
                        "Scheduled reindex orchestration {InstanceId} for tenant {TenantId}, {ResourceType} with {SPCount} SearchParameters",
                        instanceId,
                        tenantConfig.TenantId,
                        resourceType,
                        searchParams.Count);

                    runningJobsForTenant++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to schedule reindex orchestration {InstanceId} for tenant {TenantId}: {Message}",
                        instanceId,
                        tenantConfig.TenantId,
                        ex.Message);
                }
            }
        }
    }
}
