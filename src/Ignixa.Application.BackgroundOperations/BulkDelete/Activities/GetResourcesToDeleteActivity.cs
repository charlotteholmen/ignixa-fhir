// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Activities;

/// <summary>
/// DurableTask activity that identifies resources to delete based on search criteria.
/// Uses the search service to find matching resources.
/// </summary>
public class GetResourcesToDeleteActivity : AsyncTaskActivity<GetResourcesToDeleteInput, GetResourcesToDeleteOutput>
{
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly ILogger<GetResourcesToDeleteActivity> _logger;

    public GetResourcesToDeleteActivity(
        ISearchServiceFactory searchServiceFactory,
        ILogger<GetResourcesToDeleteActivity> logger)
    {
        _searchServiceFactory = searchServiceFactory ?? throw new ArgumentNullException(nameof(searchServiceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<GetResourcesToDeleteOutput> ExecuteAsync(
        TaskContext context,
        GetResourcesToDeleteInput input)
    {
        _logger.LogInformation(
            "Identifying resources to delete: TenantId={TenantId}, ResourceType={ResourceType}",
            input.TenantId,
            input.ResourceType);

        try
        {
            var searchService = await _searchServiceFactory.GetSearchServiceAsync(
                input.TenantId,
                CancellationToken.None);

            var resourcesByType = new Dictionary<string, IReadOnlyList<string>>();
            var totalCount = 0L;

            // Determine which resource types to process
            var resourceTypes = GetResourceTypesToProcess(input);

            foreach (var resourceType in resourceTypes)
            {
                // Build search options from query parameters
                var searchOptions = BuildSearchOptions(resourceType, input.SearchQuery);

                // Stream search results to collect resource IDs
                var resourceIds = new List<string>();

                await foreach (var entry in searchService.SearchStreamAsync(searchOptions, CancellationToken.None))
                {
                    resourceIds.Add(entry.ResourceId);
                }

                if (resourceIds.Count > 0)
                {
                    resourcesByType[resourceType] = resourceIds;
                    totalCount += resourceIds.Count;

                    _logger.LogInformation(
                        "Found {Count} {ResourceType} resources to delete",
                        resourceIds.Count,
                        resourceType);
                }
            }

            _logger.LogInformation(
                "Total resources identified for deletion: {TotalCount} across {TypeCount} resource types",
                totalCount,
                resourcesByType.Count);

            return new GetResourcesToDeleteOutput(
                ResourcesByType: resourcesByType,
                TotalCount: totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to identify resources to delete: TenantId={TenantId}, ResourceType={ResourceType}",
                input.TenantId,
                input.ResourceType);

            throw;
        }
    }

    private IEnumerable<string> GetResourceTypesToProcess(GetResourcesToDeleteInput input)
    {
        // If specific resource type is provided, use it
        if (!string.IsNullOrEmpty(input.ResourceType))
        {
            return new[] { input.ResourceType };
        }

        // System-level delete: get all resource types except excluded ones
        var allTypes = GetAllResourceTypes();

        if (input.ExcludedResourceTypes != null && input.ExcludedResourceTypes.Count > 0)
        {
            var excluded = new HashSet<string>(input.ExcludedResourceTypes, StringComparer.OrdinalIgnoreCase);
            return allTypes.Where(t => !excluded.Contains(t));
        }

        return allTypes;
    }

    private object BuildSearchOptions(string resourceType, string? searchQuery)
    {
        // This is a simplified implementation
        // In production, you would parse the search query and build proper SearchOptions
        // For now, we'll use dynamic to avoid tight coupling to Sparky.Search types

        dynamic options = Activator.CreateInstance(
            Type.GetType("Sparky.Search.SearchOptions, Ignixa.Search")
                ?? throw new InvalidOperationException("SearchOptions type not found"))
            ?? throw new InvalidOperationException("Failed to create SearchOptions");

        options.ResourceType = resourceType;

        // Parse and apply search query parameters if provided
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            // TODO: Parse query parameters and apply to options
            // For now, this returns all resources of the type
        }

        return options;
    }

    private static List<string> GetAllResourceTypes()
    {
        // Default FHIR resource types
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
