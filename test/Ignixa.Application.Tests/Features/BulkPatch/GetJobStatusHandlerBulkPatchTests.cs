// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Application.BackgroundOperations.BulkPatch.Models;
using Ignixa.Application.BackgroundOperations.Jobs;
using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.BulkPatch;

public class GetJobStatusHandlerBulkPatchTests
{
    [Fact]
    public void GivenBulkPatchJobProgress_WhenSerializing_ThenRoundTripsCorrectly()
    {
        // Arrange
        var progress = new BulkPatchJobProgress
        {
            ProcessedResources = 500,
            UpdatedResources = 400,
            IgnoredResources = 80,
            FailedResources = 20,
            ProgressPercentage = 50.0,
            CurrentResourceType = "Patient"
        };

        // Act
        var json = JsonSerializer.Serialize(progress);
        var deserialized = JsonSerializer.Deserialize<BulkPatchJobProgress>(json);

        // Assert
        deserialized.ProcessedResources.ShouldBe(500);
        deserialized.UpdatedResources.ShouldBe(400);
        deserialized.IgnoredResources.ShouldBe(80);
        deserialized.FailedResources.ShouldBe(20);
        deserialized.ProgressPercentage.ShouldBe(50.0);
        deserialized.CurrentResourceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenBulkPatchJobResult_WhenSerializing_ThenRoundTripsCorrectly()
    {
        // Arrange
        var result = new BulkPatchJobResult
        {
            UpdatedCounts = new Dictionary<string, int> { { "Patient", 800 }, { "Observation", 200 } },
            IgnoredCounts = new Dictionary<string, int> { { "Patient", 100 } },
            FailedCounts = new Dictionary<string, int> { { "Patient", 50 } },
            Issues = new List<BulkPatchIssue>
            {
                new BulkPatchIssue("Patient", "patient-1", "Error 1")
            }
        };

        // Act
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<BulkPatchJobResult>(json);

        // Assert
        deserialized.UpdatedCounts["Patient"].ShouldBe(800);
        deserialized.UpdatedCounts["Observation"].ShouldBe(200);
        deserialized.IgnoredCounts["Patient"].ShouldBe(100);
        deserialized.FailedCounts["Patient"].ShouldBe(50);
        deserialized.Issues.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenGetJobStatusResult_WhenCreating_ThenAllPropertiesSet()
    {
        // Arrange
        var progress = new BulkPatchJobProgress
        {
            ProcessedResources = 500,
            UpdatedResources = 400,
            IgnoredResources = 80,
            FailedResources = 20,
            ProgressPercentage = 50.0,
            CurrentResourceType = "Patient"
        };

        // Act
        var result = new GetJobStatusResult
        {
            JobId = "job-123",
            JobType = "BulkPatch",
            Status = "Running",
            CreateDate = DateTimeOffset.UtcNow.AddHours(-1),
            StartDate = DateTimeOffset.UtcNow.AddMinutes(-30),
            EndDate = null,
            ProgressPercentage = 50.0,
            ProgressDescription = "50.00% complete - 500 processed, 400 updated, 80 ignored, 20 failed",
            Definition = JsonNode.Parse(JsonSerializer.Serialize(new { resourceType = "Patient" })),
            Result = null,
            ErrorMessage = null
        };

        // Assert
        result.JobId.ShouldBe("job-123");
        result.JobType.ShouldBe("BulkPatch");
        result.Status.ShouldBe("Running");
        result.ProgressPercentage.ShouldBe(50.0);
        result.ProgressDescription.ShouldContain("50.00%");
        result.Definition.ShouldNotBeNull();
        result.Result.ShouldBeNull();
    }

    [Fact]
    public void GivenGetJobStatusQuery_WhenCreating_ThenPropertiesSet()
    {
        // Arrange & Act
        var query = new GetJobStatusQuery
        {
            JobId = "job-123",
            JobType = "BulkPatch",
            TenantId = 1
        };

        // Assert
        query.JobId.ShouldBe("job-123");
        query.JobType.ShouldBe("BulkPatch");
        query.TenantId.ShouldBe(1);
    }

    [Fact]
    public void GivenBulkPatchOrchestrationOutput_WhenSerializing_ThenRoundTripsCorrectly()
    {
        // Arrange
        var output = new BulkPatchOrchestrationOutput
        {
            JobId = "job-123",
            Status = "Completed",
            TotalProcessed = 1000,
            TotalUpdated = 800,
            TotalIgnored = 150,
            TotalFailed = 50,
            UpdatedCounts = new Dictionary<string, int> { { "Patient", 800 } },
            IgnoredCounts = new Dictionary<string, int> { { "Patient", 150 } },
            FailedCounts = new Dictionary<string, int> { { "Patient", 50 } },
            Issues = new List<BulkPatchIssue>
            {
                new BulkPatchIssue("Patient", "patient-123", "Failed to patch")
            }
        };

        // Act
        var json = JsonSerializer.Serialize(output);
        var deserialized = JsonSerializer.Deserialize<BulkPatchOrchestrationOutput>(json);

        // Assert
        deserialized.JobId.ShouldBe("job-123");
        deserialized.Status.ShouldBe("Completed");
        deserialized.TotalProcessed.ShouldBe(1000);
        deserialized.TotalUpdated.ShouldBe(800);
        deserialized.TotalIgnored.ShouldBe(150);
        deserialized.TotalFailed.ShouldBe(50);
        deserialized.Issues.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenBackgroundJobWithBulkPatchDefinition_WhenCreating_ThenDefinitionAccessible()
    {
        // Arrange
        var definition = new BulkPatchJobDefinition
        {
            TenantId = 1,
            ResourceType = "Patient",
            SearchQuery = "active=true",
            Operations = new List<BulkPatchOperationDefinition>
            {
                new BulkPatchOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.active",
                    Value = false
                }
            }
        };

        // Act
        var job = new BackgroundJob<BulkPatchJobDefinition>
        {
            JobId = "job-123",
            JobType = (int)BackgroundJobType.BulkPatch,
            Status = "Running",
            Definition = definition,
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        // Assert
        job.Definition.TenantId.ShouldBe(1);
        job.Definition.ResourceType.ShouldBe("Patient");
        job.Definition.SearchQuery.ShouldBe("active=true");
        job.Definition.Operations.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenBackgroundJobWithProgress_WhenSettingProgress_ThenProgressAccessible()
    {
        // Arrange
        var progress = new BulkPatchJobProgress
        {
            ProcessedResources = 500,
            UpdatedResources = 400,
            IgnoredResources = 80,
            FailedResources = 20,
            ProgressPercentage = 50.0,
            CurrentResourceType = "Patient"
        };

        // Act
        var job = new BackgroundJob<BulkPatchJobDefinition>
        {
            JobId = "job-123",
            JobType = (int)BackgroundJobType.BulkPatch,
            Status = "Running",
            Definition = new BulkPatchJobDefinition
            {
                TenantId = 1,
                Operations = new List<BulkPatchOperationDefinition>()
            },
            Progress = JsonNode.Parse(JsonSerializer.Serialize(progress)),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        // Assert
        job.Progress.ShouldNotBeNull();
        var deserializedProgress = JsonSerializer.Deserialize<BulkPatchJobProgress>(job.Progress.ToJsonString());
        deserializedProgress.ProcessedResources.ShouldBe(500);
    }

    [Fact]
    public void GivenBackgroundJobWithResult_WhenSettingResult_ThenResultAccessible()
    {
        // Arrange
        var result = new BulkPatchJobResult
        {
            UpdatedCounts = new Dictionary<string, int> { { "Patient", 800 } },
            IgnoredCounts = new Dictionary<string, int>(),
            FailedCounts = new Dictionary<string, int>(),
            Issues = new List<BulkPatchIssue>()
        };

        // Act
        var job = new BackgroundJob<BulkPatchJobDefinition>
        {
            JobId = "job-123",
            JobType = (int)BackgroundJobType.BulkPatch,
            Status = "Completed",
            Definition = new BulkPatchJobDefinition
            {
                TenantId = 1,
                Operations = new List<BulkPatchOperationDefinition>()
            },
            Result = JsonNode.Parse(JsonSerializer.Serialize(result)),
            CreateDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        // Assert
        job.Result.ShouldNotBeNull();
        job.Status.ShouldBe("Completed");
        job.EndDate.ShouldNotBeNull();
    }

    [Fact]
    public void GivenBulkPatchJobDefinition_WhenTenantIdSet_ThenImplementsIJobDefinition()
    {
        // Arrange
        var definition = new BulkPatchJobDefinition
        {
            TenantId = 5,
            Operations = new List<BulkPatchOperationDefinition>()
        };

        // Assert - IJobDefinition interface requires TenantId
        definition.TenantId.ShouldBe(5);
    }

    [Fact]
    public void GivenCompletedStatusResult_WhenCreating_ThenEndDateSet()
    {
        // Arrange & Act
        var result = new GetJobStatusResult
        {
            JobId = "job-123",
            JobType = "BulkPatch",
            Status = "Completed",
            CreateDate = DateTimeOffset.UtcNow.AddHours(-2),
            StartDate = DateTimeOffset.UtcNow.AddHours(-1),
            EndDate = DateTimeOffset.UtcNow,
            ProgressPercentage = 100.0,
            ProgressDescription = "Completed - 1000 resources processed"
        };

        // Assert
        result.Status.ShouldBe("Completed");
        result.EndDate.ShouldNotBeNull();
        result.ProgressPercentage.ShouldBe(100.0);
    }

    [Fact]
    public void GivenFailedStatusResult_WhenCreating_ThenErrorMessageSet()
    {
        // Arrange & Act
        var result = new GetJobStatusResult
        {
            JobId = "job-123",
            JobType = "BulkPatch",
            Status = "Failed",
            CreateDate = DateTimeOffset.UtcNow.AddHours(-1),
            StartDate = DateTimeOffset.UtcNow.AddMinutes(-30),
            EndDate = DateTimeOffset.UtcNow,
            ErrorMessage = "Orchestration failed: Timeout"
        };

        // Assert
        result.Status.ShouldBe("Failed");
        result.ErrorMessage.ShouldBe("Orchestration failed: Timeout");
    }
}
