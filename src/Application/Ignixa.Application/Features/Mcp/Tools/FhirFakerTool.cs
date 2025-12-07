// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Net;
using System.Text.Json;
using Medino;
using ModelContextProtocol.Server;
using Ignixa.Application.Features.Mcp.Dtos;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.FhirFakes.Population;
using Ignixa.Specification;

namespace Ignixa.Application.Features.Mcp.Tools;

/// <summary>
/// MCP tool for generating synthetic FHIR test data using FhirFaker (Ignixa.FhirFakes library).
/// Supports population generation by US state with realistic demographics, medical histories, and conditions.
/// </summary>
[McpServerToolType]
public class FhirFakerTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly IFhirSchemaProvider _schemaProvider;

    public FhirFakerTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator,
        IFhirRequestContextAccessor contextAccessor,
        IFhirSchemaProvider schemaProvider)
        : base(fhirRequestContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
    }

    [McpServerTool(Name = "generate_synthetic_patients")]
    [Description(@"Generate synthetic FHIR patients with realistic demographics, medical history, and clinical data.
Generates patients matching US state demographics using Ignixa.FhirFakes library.
Each patient includes encounters, conditions, medications, observations, and other clinical resources.
Resources are automatically created in your FHIR server (tenant-aware).

Example: state='Washington', count=100 generates 100 Seattle-area patients with realistic Pacific Northwest demographics")]
    public async Task<GenerateSyntheticPatientsResultDto> GenerateSyntheticPatientsAsync(
        [Description("US state name (e.g., 'Washington', 'California', 'Massachusetts'). Use ListAvailableStates to see options.")]
        string state,

        [Description("Number of patients to generate (1-10000). Recommend starting with 10-100 for testing.")]
        int count,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new ArgumentException("state parameter is required (e.g., 'Washington', 'California')");
        }

        if (count < 1 || count > 10000)
        {
            throw new ArgumentException("count must be between 1 and 10000");
        }

        // Resolve tenant
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);
        await ValidateTenantAccessAsync(resolvedTenantId, cancellationToken);

        // Update FHIR request context with resolved tenant
        var requestContext = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");
        requestContext.TenantId = resolvedTenantId;

        // Create population generator
        var generator = new PopulationGenerator(_schemaProvider);

        // Validate state is available
        if (!generator.AvailableStates.Contains(state, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"State '{state}' not found. Available states: {string.Join(", ", generator.AvailableStates)}");
        }

        // Generate populations and track results
        int successCount = 0;
        int failureCount = 0;
        var failureDetails = new List<string>();
        var generatedPatientIds = new List<string>();

        try
        {
            foreach (var scenarioContext in generator.Generate(state, count))
            {
                if (scenarioContext.Patient == null)
                {
                    failureCount++;
                    failureDetails.Add("Patient resource is null in scenario context");
                    continue;
                }

                try
                {
                    // Create patient resource
                    var patientId = scenarioContext.Patient.Id ?? Guid.NewGuid().ToString();
                    var patientResult = await CreateResourceAsync(
                        scenarioContext.Patient,
                        patientId,
                        requestContext,
                        cancellationToken);

                    if (!patientResult)
                    {
                        failureCount++;
                        failureDetails.Add($"Failed to create patient {patientId}");
                        continue;
                    }

                    // Create related resources (encounters, conditions, observations, etc.)
                    foreach (var resource in scenarioContext.AllResources)
                    {
                        if (resource.Equals(scenarioContext.Patient))
                        {
                            continue; // Already created
                        }

                        var resourceId = resource.Id ?? Guid.NewGuid().ToString();
                        var created = await CreateResourceAsync(
                            resource,
                            resourceId,
                            requestContext,
                            cancellationToken);

                        if (!created)
                        {
                            failureCount++;
                            failureDetails.Add($"Failed to create {resource.ResourceType} {resourceId}");
                        }
                    }

                    successCount++;
                    generatedPatientIds.Add(patientId);
                }
                catch (Exception ex)
                {
                    failureCount++;
                    failureDetails.Add($"Exception creating patient: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Error generating synthetic patients for state '{state}': {ex.Message}", ex);
        }

        return new GenerateSyntheticPatientsResultDto
        {
            State = state,
            RequestedCount = count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            GeneratedPatientIds = generatedPatientIds,
            Errors = failureDetails.Count > 0 ? failureDetails : null,
            Summary = $"Successfully created {successCount} patients with all their medical history " +
                      $"(encounters, conditions, medications, observations, etc.) from {state}. " +
                      $"Failed: {failureCount}"
        };
    }

    [McpServerTool(Name = "list_available_states")]
    [Description("List all available US states for synthetic patient generation. Returns state names that can be used with generate_synthetic_patients.")]
    public Task<ListAvailableStatesResultDto> ListAvailableStatesAsync(CancellationToken cancellationToken = default)
    {
        var generator = new PopulationGenerator(_schemaProvider);

        return Task.FromResult(new ListAvailableStatesResultDto
        {
            States = generator.AvailableStates.ToList(),
            Count = generator.AvailableStates.Count,
            Description = "Use any of these state names with generate_synthetic_patients. " +
                          "Patients will have demographics matching the largest city in that state."
        });
    }

    [McpServerTool(Name = "list_available_cities")]
    [Description("List all available cities with demographic data for synthetic patient generation. Each city includes population, demographics, and healthcare area codes.")]
    public Task<ListAvailableCitiesResultDto> ListAvailableCitiesAsync(CancellationToken cancellationToken = default)
    {
        var generator = new PopulationGenerator(_schemaProvider);

        var cities = generator.AvailableCities
            .Select(c => new CityInfoDto
            {
                Name = c.Name,
                State = c.State,
                Population = c.Population,
                ZipCodePrefix = c.ZipCodePrefix,
                RaceDistribution = c.RaceDistribution.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                AreaCodes = c.AreaCodes.ToList()
            })
            .ToList();

        return Task.FromResult(new ListAvailableCitiesResultDto
        {
            Cities = cities,
            Count = cities.Count,
            Description = "These cities have real demographic data from US Census. " +
                          "Use the state name from any city with generate_synthetic_patients to generate " +
                          "patients from that state's largest city."
        });
    }

    /// <summary>
    /// Creates a FHIR resource via the mediator.
    /// </summary>
    private async Task<bool> CreateResourceAsync(
        Ignixa.Serialization.SourceNodes.ResourceJsonNode resourceJsonNode,
        string resourceId,
        IFhirRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var resourceType = resourceJsonNode.ResourceType ?? "Unknown";

            var command = new CreateOrUpdateResourceCommand(
                ResourceType: resourceType,
                Id: resourceId,
                JsonNode: resourceJsonNode,
                HttpMethod: new HttpMethod("PUT"), // Use PUT for upsert behavior
                Coordinator: null);

            var result = await _mediator.SendAsync(command, cancellationToken);
            return result != null;
        }
        catch (Exception ex)
        {
            // Log but don't throw - allow partial success
            System.Diagnostics.Debug.WriteLine($"Failed to create resource: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// DTO for results of synthetic patient generation.
/// </summary>
public class GenerateSyntheticPatientsResultDto
{
    public string State { get; init; } = string.Empty;
    public int RequestedCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public IReadOnlyList<string> GeneratedPatientIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string>? Errors { get; init; }
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// DTO for listing available states.
/// </summary>
public class ListAvailableStatesResultDto
{
    public IReadOnlyList<string> States { get; init; } = Array.Empty<string>();
    public int Count { get; init; }
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// DTO for city information.
/// </summary>
public class CityInfoDto
{
    public string Name { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public long Population { get; init; }
    public string ZipCodePrefix { get; init; } = string.Empty;
    public Dictionary<string, double> RaceDistribution { get; init; } = new();
    public IReadOnlyList<string> AreaCodes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// DTO for listing available cities.
/// </summary>
public class ListAvailableCitiesResultDto
{
    public IReadOnlyList<CityInfoDto> Cities { get; init; } = Array.Empty<CityInfoDto>();
    public int Count { get; init; }
    public string Description { get; init; } = string.Empty;
}
