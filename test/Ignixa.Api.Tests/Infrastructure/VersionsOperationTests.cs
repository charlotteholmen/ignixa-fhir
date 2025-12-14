// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Api.Endpoints;
using Ignixa.Application.Features.Search;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Specification.Generated;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Ignixa.Api.Tests.Infrastructure;

/// <summary>
/// Unit tests for the $versions operation endpoint logic.
/// Tests the BuildVersionsParameters method via the MetadataEndpoints class.
/// </summary>
public class VersionsOperationTests
{
    [Fact]
    public void BuildVersionsParameters_ReturnsParametersResource()
    {
        // Arrange
        var versionContext = CreateMockVersionContext();

        // Act - Use reflection to call private method (or make it internal)
        var parameters = InvokeBuildVersionsParameters(versionContext);

        // Assert
        parameters.Should().NotBeNull();
        parameters.ResourceType.Should().Be("Parameters");
    }

    [Fact]
    public void BuildVersionsParameters_IncludesAllSupportedVersions()
    {
        // Arrange
        var versionContext = CreateMockVersionContext();

        // Act
        var parameters = InvokeBuildVersionsParameters(versionContext);

        // Assert
        var versionParams = parameters.Parameter.Where(p => p.Name == "version").ToList();
        versionParams.Should().HaveCount(5, "should include R4, R4B, R5, R6, and Stu3");
    }

    [Fact]
    public void BuildVersionsParameters_IncludesCorrectVersionCodes()
    {
        // Arrange
        var versionContext = CreateMockVersionContext();

        // Act
        var parameters = InvokeBuildVersionsParameters(versionContext);

        // Extract version codes
        var versionCodes = ExtractVersionCodes(parameters);

        // Assert
        versionCodes.Should().Contain("4.0.1", "R4 version code");
        versionCodes.Should().Contain("4.3.0", "R4B version code");
        versionCodes.Should().Contain("5.0.0", "R5 version code");
        versionCodes.Should().Contain("6.0.0-ballot2", "R6 version code");
        versionCodes.Should().Contain("3.0.2", "Stu3 version code");
    }

    [Fact]
    public void BuildVersionsParameters_MarksR4AsDefault()
    {
        // Arrange
        var versionContext = CreateMockVersionContext();

        // Act
        var parameters = InvokeBuildVersionsParameters(versionContext);

        // Find default version
        var defaultVersions = new List<string>();
        foreach (var versionParam in parameters.Parameter.Where(p => p.Name == "version"))
        {
            var defaultPart = versionParam.Part.FirstOrDefault(p => p.Name == "default");
            if (defaultPart != null)
            {
                var isDefault = defaultPart.GetValueAs<bool>("valueBoolean");
                if (isDefault)
                {
                    var codePart = versionParam.Part.FirstOrDefault(p => p.Name == "code");
                    var code = codePart?.GetValueAs<string>("valueCode");
                    if (!string.IsNullOrEmpty(code))
                    {
                        defaultVersions.Add(code);
                    }
                }
            }
        }

        // Assert
        defaultVersions.Should().HaveCount(1, "exactly one version should be default");
        defaultVersions[0].Should().Be("4.0.1", "R4 should be the default version");
    }

    [Fact]
    public void BuildVersionsParameters_SerializesToValidJson()
    {
        // Arrange
        var versionContext = CreateMockVersionContext();

        // Act
        var parameters = InvokeBuildVersionsParameters(versionContext);
        var json = parameters.SerializeToString();

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("\"resourceType\":\"Parameters\"");
        json.Should().Contain("\"version\"");
        json.Should().Contain("\"code\"");
        json.Should().Contain("\"valueCode\"");
    }

    private static IFhirVersionContext CreateMockVersionContext()
    {
        var mock = Substitute.For<IFhirVersionContext>();

        mock.GetBaseSchemaProvider(FhirVersion.R4).Returns(new R4CoreSchemaProvider());
        mock.GetBaseSchemaProvider(FhirVersion.R4B).Returns(new R4BCoreSchemaProvider());
        mock.GetBaseSchemaProvider(FhirVersion.R5).Returns(new R5CoreSchemaProvider());
        mock.GetBaseSchemaProvider(FhirVersion.R6).Returns(new R6CoreSchemaProvider());
        mock.GetBaseSchemaProvider(FhirVersion.Stu3).Returns(new STU3CoreSchemaProvider());

        return mock;
    }

    private static ParametersJsonNode InvokeBuildVersionsParameters(IFhirVersionContext versionContext)
    {
        // We need to access the private BuildVersionsParameters method
        // Use reflection to invoke it
        var method = typeof(MetadataEndpoints).GetMethod(
            "BuildVersionsParameters",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (method == null)
        {
            throw new InvalidOperationException("BuildVersionsParameters method not found");
        }

        return (ParametersJsonNode)method.Invoke(null, [versionContext])!;
    }

    private static List<string> ExtractVersionCodes(ParametersJsonNode parameters)
    {
        var versionCodes = new List<string>();
        foreach (var versionParam in parameters.Parameter.Where(p => p.Name == "version"))
        {
            var codePart = versionParam.Part.FirstOrDefault(p => p.Name == "code");
            if (codePart != null)
            {
                var code = codePart.GetValueAs<string>("valueCode");
                if (!string.IsNullOrEmpty(code))
                {
                    versionCodes.Add(code);
                }
            }
        }

        return versionCodes;
    }
}
