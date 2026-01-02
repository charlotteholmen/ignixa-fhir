// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Activities;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Models;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Orchestrations;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.BulkUpdate;

public class BulkUpdateOrchestrationTests
{
    [Fact]
    public async Task GivenNoRanges_WhenRunningOrchestration_ThenReturnsCompletedWithZeroCounts()
    {
        // Arrange
        var input = CreateTestOrchestrationInput();
        var orchestrationContext = Substitute.For<OrchestrationContext>();

        var emptyRangesOutput = new GetBulkUpdateRangesOutput(
            Ranges: new List<BulkUpdateRange>(),
            TotalEstimatedResources: 0);

        orchestrationContext.ScheduleTask<GetBulkUpdateRangesOutput>(
            typeof(GetBulkUpdateRangesActivity),
            Arg.Any<GetBulkUpdateRangesInput>())
            .Returns(Task.FromResult(emptyRangesOutput));

        var orchestration = new BulkUpdateOrchestration();

        // Act
        var result = await orchestration.RunTask(orchestrationContext, input);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe("Completed");
        result.JobId.ShouldBe(input.JobId);
        result.TotalProcessed.ShouldBe(0);
        result.TotalUpdated.ShouldBe(0);
        result.TotalIgnored.ShouldBe(0);
        result.TotalFailed.ShouldBe(0);
    }

    [Fact]
    public async Task GivenMultipleRanges_WhenRunningOrchestration_ThenAggregatesResultsFromAllWorkers()
    {
        // Arrange
        var input = CreateTestOrchestrationInput();
        var orchestrationContext = Substitute.For<OrchestrationContext>();

        var ranges = new List<BulkUpdateRange>
        {
            new BulkUpdateRange("Patient", 1, 1000, 500),
            new BulkUpdateRange("Patient", 1001, 2000, 500)
        };
        var rangesOutput = new GetBulkUpdateRangesOutput(ranges, 1000);

        orchestrationContext.ScheduleTask<GetBulkUpdateRangesOutput>(
            typeof(GetBulkUpdateRangesActivity),
            Arg.Any<GetBulkUpdateRangesInput>())
            .Returns(Task.FromResult(rangesOutput));

        var workerOutput1 = new BulkUpdateWorkerOutput("Patient", 500, 400, 80, 20, new List<BulkUpdateIssue>());
        var workerOutput2 = new BulkUpdateWorkerOutput("Patient", 500, 350, 120, 30, new List<BulkUpdateIssue>());

        var workerCallCount = 0;
        orchestrationContext.ScheduleTask<BulkUpdateWorkerOutput>(
            typeof(BulkUpdateWorkerActivity),
            Arg.Any<BulkUpdateWorkerInput>())
            .Returns(_ =>
            {
                workerCallCount++;
                return Task.FromResult(workerCallCount == 1 ? workerOutput1 : workerOutput2);
            });

        var orchestration = new BulkUpdateOrchestration();

        // Act
        var result = await orchestration.RunTask(orchestrationContext, input);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe("Completed");
        result.TotalProcessed.ShouldBe(1000);
        result.TotalUpdated.ShouldBe(750);
        result.TotalIgnored.ShouldBe(200);
        result.TotalFailed.ShouldBe(50);
    }

    [Fact]
    public async Task GivenWorkerWithIssues_WhenRunningOrchestration_ThenIssuesAreAggregated()
    {
        // Arrange
        var input = CreateTestOrchestrationInput();
        var orchestrationContext = Substitute.For<OrchestrationContext>();

        var ranges = new List<BulkUpdateRange>
        {
            new BulkUpdateRange("Patient", 1, 1000, 500)
        };
        var rangesOutput = new GetBulkUpdateRangesOutput(ranges, 500);

        orchestrationContext.ScheduleTask<GetBulkUpdateRangesOutput>(
            typeof(GetBulkUpdateRangesActivity),
            Arg.Any<GetBulkUpdateRangesInput>())
            .Returns(Task.FromResult(rangesOutput));

        var issues = new List<BulkUpdateIssue>
        {
            new BulkUpdateIssue("Patient", "patient-123", "Patch failed: invalid path"),
            new BulkUpdateIssue("Patient", "patient-456", "Patch failed: value type mismatch")
        };
        var workerOutput = new BulkUpdateWorkerOutput("Patient", 500, 400, 98, 2, issues);

        orchestrationContext.ScheduleTask<BulkUpdateWorkerOutput>(
            typeof(BulkUpdateWorkerActivity),
            Arg.Any<BulkUpdateWorkerInput>())
            .Returns(Task.FromResult(workerOutput));

        var orchestration = new BulkUpdateOrchestration();

        // Act
        var result = await orchestration.RunTask(orchestrationContext, input);

        // Assert
        result.ShouldNotBeNull();
        result.Issues.ShouldNotBeNull();
        result.Issues.Count.ShouldBe(2);
        result.Issues[0].ResourceId.ShouldBe("patient-123");
        result.Issues[1].ResourceId.ShouldBe("patient-456");
    }

    [Fact]
    public async Task GivenExceptionInRangesActivity_WhenRunningOrchestration_ThenReturnsFailedStatus()
    {
        // Arrange
        var input = CreateTestOrchestrationInput();
        var orchestrationContext = Substitute.For<OrchestrationContext>();

        orchestrationContext.ScheduleTask<GetBulkUpdateRangesOutput>(
            typeof(GetBulkUpdateRangesActivity),
            Arg.Any<GetBulkUpdateRangesInput>())
            .Returns<Task<GetBulkUpdateRangesOutput>>(_ =>
                throw new InvalidOperationException("Tenant not found"));

        var orchestration = new BulkUpdateOrchestration();

        // Act
        var result = await orchestration.RunTask(orchestrationContext, input);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe("Failed");
        result.ErrorMessage.ShouldContain("Tenant not found");
        result.TotalProcessed.ShouldBe(0);
        result.TotalUpdated.ShouldBe(0);
    }

    [Fact]
    public async Task GivenMultipleResourceTypes_WhenRunningOrchestration_ThenCountsAggregatedByType()
    {
        // Arrange
        var input = CreateTestOrchestrationInput();
        input = input with { ResourceType = null };

        var orchestrationContext = Substitute.For<OrchestrationContext>();

        var ranges = new List<BulkUpdateRange>
        {
            new BulkUpdateRange("Patient", 1, 500, 500),
            new BulkUpdateRange("Observation", 1, 300, 300)
        };
        var rangesOutput = new GetBulkUpdateRangesOutput(ranges, 800);

        orchestrationContext.ScheduleTask<GetBulkUpdateRangesOutput>(
            typeof(GetBulkUpdateRangesActivity),
            Arg.Any<GetBulkUpdateRangesInput>())
            .Returns(Task.FromResult(rangesOutput));

        var patientOutput = new BulkUpdateWorkerOutput("Patient", 500, 400, 80, 20, new List<BulkUpdateIssue>());
        var observationOutput = new BulkUpdateWorkerOutput("Observation", 300, 250, 40, 10, new List<BulkUpdateIssue>());

        var workerCallCount = 0;
        orchestrationContext.ScheduleTask<BulkUpdateWorkerOutput>(
            typeof(BulkUpdateWorkerActivity),
            Arg.Any<BulkUpdateWorkerInput>())
            .Returns(_ =>
            {
                workerCallCount++;
                return Task.FromResult(workerCallCount == 1 ? patientOutput : observationOutput);
            });

        var orchestration = new BulkUpdateOrchestration();

        // Act
        var result = await orchestration.RunTask(orchestrationContext, input);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe("Completed");
        result.TotalUpdated.ShouldBe(650);
        result.UpdatedCounts.ShouldNotBeNull();
        result.UpdatedCounts["Patient"].ShouldBe(400);
        result.UpdatedCounts["Observation"].ShouldBe(250);
    }

    [Fact]
    public async Task GivenMaxIssuesLimit_WhenRunningOrchestration_ThenIssuesAreCapped()
    {
        // Arrange
        var input = CreateTestOrchestrationInput();
        var orchestrationContext = Substitute.For<OrchestrationContext>();

        var ranges = new List<BulkUpdateRange>();
        for (int i = 0; i < 20; i++)
        {
            ranges.Add(new BulkUpdateRange("Patient", i * 100, (i + 1) * 100 - 1, 100));
        }
        var rangesOutput = new GetBulkUpdateRangesOutput(ranges, 2000);

        orchestrationContext.ScheduleTask<GetBulkUpdateRangesOutput>(
            typeof(GetBulkUpdateRangesActivity),
            Arg.Any<GetBulkUpdateRangesInput>())
            .Returns(Task.FromResult(rangesOutput));

        orchestrationContext.ScheduleTask<BulkUpdateWorkerOutput>(
            typeof(BulkUpdateWorkerActivity),
            Arg.Any<BulkUpdateWorkerInput>())
            .Returns(_ =>
            {
                var issues = new List<BulkUpdateIssue>();
                for (int i = 0; i < 100; i++)
                {
                    issues.Add(new BulkUpdateIssue("Patient", $"patient-{i}", "Error"));
                }
                return Task.FromResult(new BulkUpdateWorkerOutput("Patient", 100, 0, 0, 100, issues));
            });

        var orchestration = new BulkUpdateOrchestration();

        // Act
        var result = await orchestration.RunTask(orchestrationContext, input);

        // Assert
        result.ShouldNotBeNull();
        result.Issues.ShouldNotBeNull();
        result.Issues.Count.ShouldBeLessThanOrEqualTo(1000);
    }

    [Fact]
    public async Task GivenNoUpdatesOrFailures_WhenRunningOrchestration_ThenCountDictionariesAreEmpty()
    {
        // Arrange
        var input = CreateTestOrchestrationInput();
        var orchestrationContext = Substitute.For<OrchestrationContext>();

        var ranges = new List<BulkUpdateRange>
        {
            new BulkUpdateRange("Patient", 1, 1000, 1000)
        };
        var rangesOutput = new GetBulkUpdateRangesOutput(ranges, 1000);

        orchestrationContext.ScheduleTask<GetBulkUpdateRangesOutput>(
            typeof(GetBulkUpdateRangesActivity),
            Arg.Any<GetBulkUpdateRangesInput>())
            .Returns(Task.FromResult(rangesOutput));

        var workerOutput = new BulkUpdateWorkerOutput("Patient", 1000, 0, 1000, 0, new List<BulkUpdateIssue>());

        orchestrationContext.ScheduleTask<BulkUpdateWorkerOutput>(
            typeof(BulkUpdateWorkerActivity),
            Arg.Any<BulkUpdateWorkerInput>())
            .Returns(Task.FromResult(workerOutput));

        var orchestration = new BulkUpdateOrchestration();

        // Act
        var result = await orchestration.RunTask(orchestrationContext, input);

        // Assert
        result.ShouldNotBeNull();
        result.TotalUpdated.ShouldBe(0);
        result.TotalIgnored.ShouldBe(1000);
        result.TotalFailed.ShouldBe(0);
        (result.UpdatedCounts == null || !result.UpdatedCounts.Any()).ShouldBeTrue();
        (result.FailedCounts == null || !result.FailedCounts.Any()).ShouldBeTrue();
        result.IgnoredCounts["Patient"].ShouldBe(1000);
    }

    private static BulkUpdateOrchestrationInput CreateTestOrchestrationInput()
    {
        return new BulkUpdateOrchestrationInput
        {
            JobId = "test-job-123",
            TenantId = 1,
            ResourceType = "Patient",
            SearchQuery = null,
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.active",
                    Value = true
                }
            },
            BatchSize = 1000
        };
    }
}
