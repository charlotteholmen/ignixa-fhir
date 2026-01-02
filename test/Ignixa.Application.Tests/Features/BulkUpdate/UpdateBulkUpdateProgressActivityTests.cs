// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Activities;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.BulkUpdate;

public class UpdateBulkUpdateProgressActivityTests
{
    private readonly IBackgroundJobRepository<BulkUpdateJobDefinition> _jobRepository;
    private readonly ILogger<UpdateBulkUpdateProgressActivity> _logger;

    public UpdateBulkUpdateProgressActivityTests()
    {
        _jobRepository = Substitute.For<IBackgroundJobRepository<BulkUpdateJobDefinition>>();
        _logger = NullLogger<UpdateBulkUpdateProgressActivity>.Instance;
    }

    [Fact]
    public async Task GivenValidJob_WhenUpdatingProgress_ThenProgressIsUpdated()
    {
        // Arrange
        var input = new UpdateBulkUpdateProgressInput
        {
            JobId = "job-123",
            TenantId = 1,
            ProcessedResources = 500,
            UpdatedResources = 400,
            IgnoredResources = 80,
            FailedResources = 20,
            TotalEstimated = 1000,
            CurrentResourceType = "Patient"
        };

        var existingJob = CreateTestJob("job-123", 1);
        _jobRepository.GetAsync("job-123", 1, Arg.Any<CancellationToken>())
            .Returns(existingJob);

        var activity = new UpdateBulkUpdateProgressActivity(_jobRepository, _logger);

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldBeTrue();

        await _jobRepository.Received(1).UpdateAsync(
            Arg.Is<BackgroundJob<BulkUpdateJobDefinition>>(j =>
                j.JobId == "job-123" &&
                j.Progress != null),
            Arg.Is<int>(1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenJobNotFound_WhenUpdatingProgress_ThenReturnsFalse()
    {
        // Arrange
        var input = new UpdateBulkUpdateProgressInput
        {
            JobId = "nonexistent-job",
            TenantId = 1,
            ProcessedResources = 100,
            UpdatedResources = 50,
            IgnoredResources = 40,
            FailedResources = 10,
            TotalEstimated = 1000
        };

        _jobRepository.GetAsync("nonexistent-job", 1, Arg.Any<CancellationToken>())
            .Returns((BackgroundJob<BulkUpdateJobDefinition>)null);

        var activity = new UpdateBulkUpdateProgressActivity(_jobRepository, _logger);

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldBeFalse();

        await _jobRepository.DidNotReceive().UpdateAsync(
            Arg.Any<BackgroundJob<BulkUpdateJobDefinition>>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenProgressInput_WhenUpdatingProgress_ThenProgressPercentageCalculated()
    {
        // Arrange
        var input = new UpdateBulkUpdateProgressInput
        {
            JobId = "job-123",
            TenantId = 1,
            ProcessedResources = 250,
            UpdatedResources = 200,
            IgnoredResources = 40,
            FailedResources = 10,
            TotalEstimated = 500,
            CurrentResourceType = "Observation"
        };

        var existingJob = CreateTestJob("job-123", 1);
        _jobRepository.GetAsync("job-123", 1, Arg.Any<CancellationToken>())
            .Returns(existingJob);

        BackgroundJob<BulkUpdateJobDefinition> capturedJob = null;
        await _jobRepository.UpdateAsync(
            Arg.Do<BackgroundJob<BulkUpdateJobDefinition>>(j => capturedJob = j),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        var activity = new UpdateBulkUpdateProgressActivity(_jobRepository, _logger);

        // Act
        await InvokeActivityAsync(activity, input);

        // Assert
        capturedJob.ShouldNotBeNull();
        capturedJob.Progress.ShouldNotBeNull();

        var progress = JsonSerializer.Deserialize<BulkUpdateJobProgress>(capturedJob.Progress.ToJsonString());
        progress.ProcessedResources.ShouldBe(250);
        progress.UpdatedResources.ShouldBe(200);
        progress.IgnoredResources.ShouldBe(40);
        progress.FailedResources.ShouldBe(10);
        progress.ProgressPercentage.ShouldBe(50.0);
        progress.CurrentResourceType.ShouldBe("Observation");
    }

    [Fact]
    public async Task GivenZeroTotalEstimated_WhenUpdatingProgress_ThenProgressPercentageIsZero()
    {
        // Arrange
        var input = new UpdateBulkUpdateProgressInput
        {
            JobId = "job-123",
            TenantId = 1,
            ProcessedResources = 100,
            UpdatedResources = 50,
            IgnoredResources = 40,
            FailedResources = 10,
            TotalEstimated = 0,
            CurrentResourceType = "Patient"
        };

        var existingJob = CreateTestJob("job-123", 1);
        _jobRepository.GetAsync("job-123", 1, Arg.Any<CancellationToken>())
            .Returns(existingJob);

        BackgroundJob<BulkUpdateJobDefinition> capturedJob = null;
        await _jobRepository.UpdateAsync(
            Arg.Do<BackgroundJob<BulkUpdateJobDefinition>>(j => capturedJob = j),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        var activity = new UpdateBulkUpdateProgressActivity(_jobRepository, _logger);

        // Act
        await InvokeActivityAsync(activity, input);

        // Assert
        capturedJob.ShouldNotBeNull();
        var progress = JsonSerializer.Deserialize<BulkUpdateJobProgress>(capturedJob.Progress.ToJsonString());
        progress.ProgressPercentage.ShouldBe(0);
    }

    [Fact]
    public async Task GivenProgressUpdate_WhenUpdatingProgress_ThenHeartbeatDateUpdated()
    {
        // Arrange
        var oldHeartbeat = DateTimeOffset.UtcNow.AddMinutes(-10);
        var input = new UpdateBulkUpdateProgressInput
        {
            JobId = "job-123",
            TenantId = 1,
            ProcessedResources = 100,
            UpdatedResources = 50,
            IgnoredResources = 40,
            FailedResources = 10,
            TotalEstimated = 1000
        };

        var existingJob = CreateTestJob("job-123", 1);
        existingJob.HeartbeatDate = oldHeartbeat;

        _jobRepository.GetAsync("job-123", 1, Arg.Any<CancellationToken>())
            .Returns(existingJob);

        BackgroundJob<BulkUpdateJobDefinition> capturedJob = null;
        await _jobRepository.UpdateAsync(
            Arg.Do<BackgroundJob<BulkUpdateJobDefinition>>(j => capturedJob = j),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        var activity = new UpdateBulkUpdateProgressActivity(_jobRepository, _logger);

        // Act
        await InvokeActivityAsync(activity, input);

        // Assert
        capturedJob.ShouldNotBeNull();
        capturedJob.HeartbeatDate.ShouldBeGreaterThan(oldHeartbeat);
    }

    [Fact]
    public async Task GivenDifferentTenantId_WhenUpdatingProgress_ThenQueryUsesCorrectTenantId()
    {
        // Arrange
        var input = new UpdateBulkUpdateProgressInput
        {
            JobId = "job-123",
            TenantId = 5,
            ProcessedResources = 100,
            UpdatedResources = 50,
            IgnoredResources = 40,
            FailedResources = 10,
            TotalEstimated = 1000
        };

        _jobRepository.GetAsync("job-123", 5, Arg.Any<CancellationToken>())
            .Returns((BackgroundJob<BulkUpdateJobDefinition>)null);

        var activity = new UpdateBulkUpdateProgressActivity(_jobRepository, _logger);

        // Act
        await InvokeActivityAsync(activity, input);

        // Assert
        await _jobRepository.Received(1).GetAsync("job-123", 5, Arg.Any<CancellationToken>());
    }

    private static BackgroundJob<BulkUpdateJobDefinition> CreateTestJob(string jobId, int tenantId)
    {
        return new BackgroundJob<BulkUpdateJobDefinition>
        {
            JobId = jobId,
            JobType = (int)BackgroundJobType.BulkUpdate,
            Status = "Running",
            Definition = new BulkUpdateJobDefinition
            {
                TenantId = tenantId,
                ResourceType = "Patient",
                Operations = new List<BulkUpdateOperationDefinition>
                {
                    new BulkUpdateOperationDefinition
                    {
                        Type = "replace",
                        Path = "Patient.active",
                        Value = true
                    }
                }
            },
            CreateDate = DateTimeOffset.UtcNow.AddHours(-1),
            HeartbeatDate = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
    }

    private static async Task<bool> InvokeActivityAsync(
        UpdateBulkUpdateProgressActivity activity,
        UpdateBulkUpdateProgressInput input)
    {
        var taskContext = new TaskContext(new OrchestrationInstance { InstanceId = "test" });
        var method = typeof(UpdateBulkUpdateProgressActivity)
            .GetMethod("ExecuteAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = (Task<bool>)method.Invoke(activity, [taskContext, input]);
        return await task;
    }
}
