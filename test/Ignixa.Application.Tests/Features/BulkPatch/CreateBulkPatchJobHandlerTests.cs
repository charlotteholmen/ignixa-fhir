// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using Ignixa.Application.BackgroundOperations.BulkPatch;
using Ignixa.Application.Features.Patch;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.BulkPatch;

public class CreateBulkPatchJobHandlerTests
{
    private readonly FhirPatchParametersParser _patchParametersParser;

    public CreateBulkPatchJobHandlerTests()
    {
        _patchParametersParser = new FhirPatchParametersParser();
    }

    [Fact]
    public void GivenValidReplaceParameters_WhenParsing_ThenOperationsExtracted()
    {
        // Arrange
        var parametersJson = CreateValidReplaceParametersJson();
        var patchParameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);

        // Act
        var operations = _patchParametersParser.Parse(patchParameters);

        // Assert
        operations.ShouldNotBeNull();
        operations.Length.ShouldBe(1);
        operations[0].Type.ShouldBe(FhirPatchOperationType.Replace);
        operations[0].Path.ShouldBe("Patient.active");
    }

    [Fact]
    public void GivenAddOperation_WhenParsing_ThenOperationTypeIsAdd()
    {
        // Arrange
        var parametersJson = @"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""add""},
                        {""name"": ""path"", ""valueString"": ""Patient.telecom""},
                        {""name"": ""value"", ""valueString"": ""555-1234""}
                    ]
                }
            ]
        }";
        var patchParameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);

        // Act
        var operations = _patchParametersParser.Parse(patchParameters);

        // Assert
        operations.ShouldNotBeNull();
        operations.Length.ShouldBe(1);
        operations[0].Type.ShouldBe(FhirPatchOperationType.Add);
    }

    [Fact]
    public void GivenMixedOperations_WhenParsing_ThenAllOperationsReturned()
    {
        // Arrange
        var parametersJson = @"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""replace""},
                        {""name"": ""path"", ""valueString"": ""Patient.active""},
                        {""name"": ""value"", ""valueBoolean"": true}
                    ]
                },
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""delete""},
                        {""name"": ""path"", ""valueString"": ""Patient.gender""}
                    ]
                },
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""add""},
                        {""name"": ""path"", ""valueString"": ""Patient.telecom""},
                        {""name"": ""value"", ""valueString"": ""555-1234""}
                    ]
                }
            ]
        }";
        var patchParameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);

        // Act
        var operations = _patchParametersParser.Parse(patchParameters);

        // Assert
        operations.ShouldNotBeNull();
        operations.Length.ShouldBe(3);
        operations[0].Type.ShouldBe(FhirPatchOperationType.Replace);
        operations[1].Type.ShouldBe(FhirPatchOperationType.Delete);
        operations[2].Type.ShouldBe(FhirPatchOperationType.Add);
    }

    [Fact]
    public void GivenNoOperationsInParameters_WhenParsing_ThenThrowsException()
    {
        // Arrange
        var parametersJson = @"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [
                {
                    ""name"": ""someOtherParameter"",
                    ""valueString"": ""value""
                }
            ]
        }";
        var patchParameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);

        // Act & Assert
        var exception = Should.Throw<FhirPatchException>(
            () => _patchParametersParser.Parse(patchParameters));

        exception.Message.ShouldContain("must contain at least one 'operation' parameter");
    }

    [Fact]
    public void GivenNullPatchParameters_WhenParsing_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(
            () => _patchParametersParser.Parse(null));
    }

    [Fact]
    public void GivenCreateBulkPatchJobCommand_WhenCreating_ThenPropertiesSet()
    {
        // Arrange
        var parametersJson = CreateValidReplaceParametersJson();
        var patchParameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);

        // Act
        var command = new CreateBulkPatchJobCommand
        {
            TenantId = 1,
            ResourceType = "Patient",
            SearchQuery = "active=true",
            PatchParameters = patchParameters
        };

        // Assert
        command.TenantId.ShouldBe(1);
        command.ResourceType.ShouldBe("Patient");
        command.SearchQuery.ShouldBe("active=true");
        command.PatchParameters.ShouldNotBeNull();
    }

    [Fact]
    public void GivenCreateBulkPatchJobCommand_WhenResourceTypeNull_ThenAllResourceTypesTargeted()
    {
        // Arrange
        var parametersJson = CreateValidReplaceParametersJson();
        var patchParameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);

        // Act
        var command = new CreateBulkPatchJobCommand
        {
            TenantId = 1,
            ResourceType = null,
            SearchQuery = null,
            PatchParameters = patchParameters
        };

        // Assert
        command.ResourceType.ShouldBeNull();
        command.SearchQuery.ShouldBeNull();
    }

    [Fact]
    public void GivenCreateBulkPatchJobResult_WhenCreating_ThenAllPropertiesSet()
    {
        // Arrange & Act
        var result = new CreateBulkPatchJobResult
        {
            JobId = "job-123",
            Status = "Queued",
            OrchestrationInstanceId = "orchestration-456",
            CreateDate = DateTimeOffset.UtcNow
        };

        // Assert
        result.JobId.ShouldBe("job-123");
        result.Status.ShouldBe("Queued");
        result.OrchestrationInstanceId.ShouldBe("orchestration-456");
        result.CreateDate.ShouldNotBe(default);
    }

    [Fact]
    public void GivenBulkPatchJobDefinition_WhenConvertingOperations_ThenCorrectTypeMapping()
    {
        var operations = new List<BulkPatchOperationDefinition>
        {
            new BulkPatchOperationDefinition
            {
                Type = "replace",
                Path = "Patient.active",
                Value = true
            },
            new BulkPatchOperationDefinition
            {
                Type = "upsert",
                Path = "Patient.extension",
                Value = new { url = "http://example.org", valueString = "test" }
            }
        };

        operations.Count.ShouldBe(2);
        operations[0].Type.ShouldBe("replace");
        operations[1].Type.ShouldBe("upsert");
    }

    [Fact]
    public void GivenValidDeleteOperation_WhenParsing_ThenOperationExtracted()
    {
        // Arrange
        var parametersJson = @"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""delete""},
                        {""name"": ""path"", ""valueString"": ""Patient.gender""}
                    ]
                }
            ]
        }";
        var patchParameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);

        // Act
        var operations = _patchParametersParser.Parse(patchParameters);

        // Assert
        operations.ShouldNotBeNull();
        operations.Length.ShouldBe(1);
        operations[0].Type.ShouldBe(FhirPatchOperationType.Delete);
        operations[0].Path.ShouldBe("Patient.gender");
    }

    [Fact]
    public void GivenInsertOperation_WhenParsing_ThenIndexIncluded()
    {
        // Arrange
        var parametersJson = @"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""insert""},
                        {""name"": ""path"", ""valueString"": ""Patient.name""},
                        {""name"": ""index"", ""valueInteger"": 0},
                        {""name"": ""value"", ""valueString"": ""NewName""}
                    ]
                }
            ]
        }";
        var patchParameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);

        // Act
        var operations = _patchParametersParser.Parse(patchParameters);

        // Assert
        operations.ShouldNotBeNull();
        operations.Length.ShouldBe(1);
        operations[0].Type.ShouldBe(FhirPatchOperationType.Insert);
        operations[0].Index.ShouldBe(0);
    }

    private static string CreateValidReplaceParametersJson()
    {
        return @"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""replace""},
                        {""name"": ""path"", ""valueString"": ""Patient.active""},
                        {""name"": ""value"", ""valueBoolean"": true}
                    ]
                }
            ]
        }";
    }
}
