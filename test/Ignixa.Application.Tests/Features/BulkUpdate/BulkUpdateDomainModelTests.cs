// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.BulkUpdate;

public class BulkUpdateDomainModelTests
{
    [Fact]
    public void GivenBulkUpdateJobDefinition_WhenCreating_ThenRequiredFieldsAreSet()
    {
        // Arrange & Act
        var definition = new BulkUpdateJobDefinition
        {
            TenantId = 1,
            ResourceType = "Patient",
            SearchQuery = "active=true",
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.active",
                    Value = false
                }
            }
        };

        // Assert
        definition.TenantId.ShouldBe(1);
        definition.ResourceType.ShouldBe("Patient");
        definition.SearchQuery.ShouldBe("active=true");
        definition.Operations.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenBulkUpdateJobDefinition_WhenResourceTypeNull_ThenAllResourceTypesTargeted()
    {
        // Arrange & Act
        var definition = new BulkUpdateJobDefinition
        {
            TenantId = 1,
            ResourceType = null,
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Resource.meta.tag",
                    Value = new { system = "http://example.org", code = "processed" }
                }
            }
        };

        // Assert
        definition.ResourceType.ShouldBeNull();
    }

    [Fact]
    public void GivenBulkUpdateOperationDefinition_WhenReplaceType_ThenPathAndValueRequired()
    {
        // Arrange & Act
        var operation = new BulkUpdateOperationDefinition
        {
            Type = "replace",
            Path = "Patient.name[0].family",
            Name = null,
            Value = "NewLastName"
        };

        // Assert
        operation.Type.ShouldBe("replace");
        operation.Path.ShouldBe("Patient.name[0].family");
        operation.Name.ShouldBeNull();
        operation.Value.ShouldBe("NewLastName");
    }

    [Fact]
    public void GivenBulkUpdateOperationDefinition_WhenUpsertType_ThenNameCanBeProvided()
    {
        // Arrange & Act
        var operation = new BulkUpdateOperationDefinition
        {
            Type = "upsert",
            Path = "Patient.extension",
            Name = "customExtension",
            Value = new { url = "http://example.org/ext", valueString = "test" }
        };

        // Assert
        operation.Type.ShouldBe("upsert");
        operation.Name.ShouldBe("customExtension");
    }

    [Fact]
    public void GivenBulkUpdateJobProgress_WhenCreating_ThenAllFieldsSet()
    {
        // Arrange & Act
        var progress = new BulkUpdateJobProgress
        {
            ProcessedResources = 500,
            UpdatedResources = 400,
            IgnoredResources = 80,
            FailedResources = 20,
            ProgressPercentage = 50.0,
            CurrentResourceType = "Patient"
        };

        // Assert
        progress.ProcessedResources.ShouldBe(500);
        progress.UpdatedResources.ShouldBe(400);
        progress.IgnoredResources.ShouldBe(80);
        progress.FailedResources.ShouldBe(20);
        progress.ProgressPercentage.ShouldBe(50.0);
        progress.CurrentResourceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenBulkUpdateJobProgress_WhenSumCounts_ThenEqualsProcessed()
    {
        // Arrange
        var progress = new BulkUpdateJobProgress
        {
            ProcessedResources = 1000,
            UpdatedResources = 700,
            IgnoredResources = 250,
            FailedResources = 50,
            ProgressPercentage = 100.0,
            CurrentResourceType = null
        };

        // Act
        var sum = progress.UpdatedResources + progress.IgnoredResources + progress.FailedResources;

        // Assert
        sum.ShouldBe(progress.ProcessedResources);
    }

    [Fact]
    public void GivenBulkUpdateJobResult_WhenCreating_ThenCountDictionariesAreSet()
    {
        // Arrange & Act
        var result = new BulkUpdateJobResult
        {
            UpdatedCounts = new Dictionary<string, int>
            {
                { "Patient", 500 },
                { "Observation", 300 }
            },
            IgnoredCounts = new Dictionary<string, int>
            {
                { "Patient", 100 },
                { "Observation", 50 }
            },
            FailedCounts = new Dictionary<string, int>
            {
                { "Patient", 20 },
                { "Observation", 10 }
            },
            Issues = new List<BulkUpdateIssue>
            {
                new BulkUpdateIssue("Patient", "patient-123", "Invalid path")
            }
        };

        // Assert
        result.UpdatedCounts["Patient"].ShouldBe(500);
        result.UpdatedCounts["Observation"].ShouldBe(300);
        result.IgnoredCounts["Patient"].ShouldBe(100);
        result.FailedCounts["Observation"].ShouldBe(10);
        result.Issues.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenBulkUpdateJobResult_WhenCalculatingTotals_ThenSumsAreCorrect()
    {
        // Arrange
        var result = new BulkUpdateJobResult
        {
            UpdatedCounts = new Dictionary<string, int>
            {
                { "Patient", 500 },
                { "Observation", 300 }
            },
            IgnoredCounts = new Dictionary<string, int>
            {
                { "Patient", 100 }
            },
            FailedCounts = new Dictionary<string, int>
            {
                { "Patient", 20 }
            },
            Issues = new List<BulkUpdateIssue>()
        };

        // Act
        var totalUpdated = result.UpdatedCounts.Values.Sum();
        var totalIgnored = result.IgnoredCounts.Values.Sum();
        var totalFailed = result.FailedCounts.Values.Sum();

        // Assert
        totalUpdated.ShouldBe(800);
        totalIgnored.ShouldBe(100);
        totalFailed.ShouldBe(20);
    }

    [Fact]
    public void GivenBulkUpdateIssue_WhenCreating_ThenRecordPropertiesSet()
    {
        // Arrange & Act
        var issue = new BulkUpdateIssue(
            ResourceType: "Patient",
            ResourceId: "patient-12345",
            ErrorMessage: "Failed to apply patch: path not found");

        // Assert
        issue.ResourceType.ShouldBe("Patient");
        issue.ResourceId.ShouldBe("patient-12345");
        issue.ErrorMessage.ShouldBe("Failed to apply patch: path not found");
    }

    [Fact]
    public void GivenBulkUpdateIssue_WhenComparing_ThenValueEqualityUsed()
    {
        // Arrange
        var issue1 = new BulkUpdateIssue("Patient", "patient-123", "Error message");
        var issue2 = new BulkUpdateIssue("Patient", "patient-123", "Error message");
        var issue3 = new BulkUpdateIssue("Patient", "patient-456", "Error message");

        // Assert
        issue1.ShouldBe(issue2);
        issue1.ShouldNotBe(issue3);
    }

    [Fact]
    public void GivenBulkUpdateOperationDefinition_WhenValueIsComplexObject_ThenStoredCorrectly()
    {
        // Arrange
        var complexValue = new Dictionary<string, object>
        {
            { "system", "http://terminology.hl7.org/CodeSystem/v2-0131" },
            { "code", "C" },
            { "display", "Emergency Contact" }
        };

        // Act
        var operation = new BulkUpdateOperationDefinition
        {
            Type = "replace",
            Path = "Patient.contact[0].relationship[0].coding[0]",
            Value = complexValue
        };

        // Assert
        operation.Value.ShouldNotBeNull();
        operation.Value.ShouldBeOfType<Dictionary<string, object>>();
    }

    [Fact]
    public void GivenBulkUpdateJobDefinition_WhenMultipleOperations_ThenAllPreserved()
    {
        // Arrange & Act
        var definition = new BulkUpdateJobDefinition
        {
            TenantId = 1,
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.active",
                    Value = false
                },
                new BulkUpdateOperationDefinition
                {
                    Type = "upsert",
                    Path = "Patient.meta.tag",
                    Value = new { system = "http://example.org", code = "migrated" }
                },
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.contact[0].name.family",
                    Value = "UpdatedName"
                }
            }
        };

        // Assert
        definition.Operations.Count.ShouldBe(3);
        definition.Operations[0].Type.ShouldBe("replace");
        definition.Operations[1].Type.ShouldBe("upsert");
        definition.Operations[2].Path.ShouldContain("contact");
    }

    [Fact]
    public void GivenBackgroundJobType_WhenBulkUpdate_ThenValueIs5()
    {
        // Assert
        ((int)BackgroundJobType.BulkUpdate).ShouldBe(5);
    }

    [Fact]
    public void GivenBulkUpdateJobProgress_WhenNoCurrentResourceType_ThenNullable()
    {
        // Arrange & Act
        var progress = new BulkUpdateJobProgress
        {
            ProcessedResources = 0,
            UpdatedResources = 0,
            IgnoredResources = 0,
            FailedResources = 0,
            ProgressPercentage = 0,
            CurrentResourceType = null
        };

        // Assert
        progress.CurrentResourceType.ShouldBeNull();
    }
}
