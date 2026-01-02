// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Activities;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.BulkUpdate;

public class GetBulkUpdateRangesActivityTests
{
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly ISearchService _searchService;
    private readonly ILogger<GetBulkUpdateRangesActivity> _logger;

    public GetBulkUpdateRangesActivityTests()
    {
        _searchServiceFactory = Substitute.For<ISearchServiceFactory>();
        _searchService = Substitute.For<ISearchService>();
        _logger = NullLogger<GetBulkUpdateRangesActivity>.Instance;

        _searchServiceFactory.GetSearchServiceAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_searchService);
    }

    [Fact]
    public async Task GivenSpecificResourceType_WhenGettingRanges_ThenOnlyThatTypeIsQueried()
    {
        // Arrange
        var input = new GetBulkUpdateRangesInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = "Patient",
            SearchQuery = null
        };

        var ranges = new List<(long StartId, long EndId)> { (1L, 1000L), (1001L, 2000L) };
        _searchService.GetExportRangesAsync("Patient", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ranges);

        var activity = new GetBulkUpdateRangesActivity(_searchServiceFactory, _logger);

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldNotBeNull();
        result.Ranges.ShouldNotBeNull();
        result.Ranges.Count.ShouldBe(2);
        result.Ranges.All(r => r.ResourceType == "Patient").ShouldBeTrue();

        await _searchService.Received(1).GetExportRangesAsync("Patient", Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _searchService.DidNotReceive().GetExportRangesAsync("Observation", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNoResourceType_WhenGettingRanges_ThenDefaultTypesAreQueried()
    {
        // Arrange
        var input = new GetBulkUpdateRangesInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = null,
            SearchQuery = null
        };

        var patientRanges = new List<(long StartId, long EndId)> { (1L, 1000L) };
        var observationRanges = new List<(long StartId, long EndId)> { (1L, 500L) };

        _searchService.GetExportRangesAsync("Patient", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(patientRanges);
        _searchService.GetExportRangesAsync("Observation", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(observationRanges);
        _searchService.GetExportRangesAsync("Condition", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<(long, long)>());
        _searchService.GetExportRangesAsync("MedicationRequest", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<(long, long)>());
        _searchService.GetExportRangesAsync("Encounter", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<(long, long)>());
        _searchService.GetExportRangesAsync("Procedure", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<(long, long)>());

        var activity = new GetBulkUpdateRangesActivity(_searchServiceFactory, _logger);

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldNotBeNull();
        result.Ranges.Count.ShouldBe(2);
        result.Ranges.Count(r => r.ResourceType == "Patient").ShouldBe(1);
        result.Ranges.Count(r => r.ResourceType == "Observation").ShouldBe(1);
    }

    [Fact]
    public async Task GivenNoResourcesFound_WhenGettingRanges_ThenEmptyListReturned()
    {
        // Arrange
        var input = new GetBulkUpdateRangesInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = "Patient",
            SearchQuery = null
        };

        _searchService.GetExportRangesAsync("Patient", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<(long, long)>());

        var activity = new GetBulkUpdateRangesActivity(_searchServiceFactory, _logger);

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldNotBeNull();
        result.Ranges.ShouldBeEmpty();
        result.TotalEstimatedResources.ShouldBe(0);
    }

    [Fact]
    public async Task GivenMultipleRanges_WhenGettingRanges_ThenTotalEstimatedResourcesCalculated()
    {
        // Arrange
        var input = new GetBulkUpdateRangesInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = "Patient",
            SearchQuery = null
        };

        var ranges = new List<(long StartId, long EndId)>
        {
            (1L, 4000L),
            (4001L, 8000L),
            (8001L, 12000L),
            (12001L, 16000L)
        };
        _searchService.GetExportRangesAsync("Patient", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ranges);

        var activity = new GetBulkUpdateRangesActivity(_searchServiceFactory, _logger);

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldNotBeNull();
        result.Ranges.Count.ShouldBe(4);
        result.TotalEstimatedResources.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GivenTenantId_WhenGettingRanges_ThenSearchServiceCreatedForCorrectTenant()
    {
        // Arrange
        var input = new GetBulkUpdateRangesInput
        {
            JobId = "job-123",
            TenantId = 5,
            ResourceType = "Patient",
            SearchQuery = null
        };

        _searchService.GetExportRangesAsync("Patient", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<(long, long)>());

        var activity = new GetBulkUpdateRangesActivity(_searchServiceFactory, _logger);

        // Act
        await InvokeActivityAsync(activity, input);

        // Assert
        await _searchServiceFactory.Received(1).GetSearchServiceAsync(5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenRanges_WhenGettingRanges_ThenRangeContainsCorrectSurrogateIds()
    {
        // Arrange
        var input = new GetBulkUpdateRangesInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = "Patient",
            SearchQuery = null
        };

        var ranges = new List<(long StartId, long EndId)> { (100L, 500L) };
        _searchService.GetExportRangesAsync("Patient", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ranges);

        var activity = new GetBulkUpdateRangesActivity(_searchServiceFactory, _logger);

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldNotBeNull();
        result.Ranges.ShouldHaveSingleItem();
        var range = result.Ranges[0];
        range.StartSurrogateId.ShouldBe(100L);
        range.EndSurrogateId.ShouldBe(500L);
        range.ResourceType.ShouldBe("Patient");
    }

    private static async Task<GetBulkUpdateRangesOutput> InvokeActivityAsync(
        GetBulkUpdateRangesActivity activity,
        GetBulkUpdateRangesInput input)
    {
        var taskContext = new TaskContext(new OrchestrationInstance { InstanceId = "test" });
        var method = typeof(GetBulkUpdateRangesActivity)
            .GetMethod("ExecuteAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = (Task<GetBulkUpdateRangesOutput>)method.Invoke(activity, [taskContext, input]);
        return await task;
    }
}
