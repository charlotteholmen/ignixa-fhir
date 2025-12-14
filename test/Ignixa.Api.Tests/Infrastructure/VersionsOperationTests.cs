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

        // Act
        var parameters = MetadataEndpoints.BuildVersionsParameters(versionContext, FhirVersion.R4);

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
        var parameters = MetadataEndpoints.BuildVersionsParameters(versionContext, FhirVersion.R4);

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
        var parameters = MetadataEndpoints.BuildVersionsParameters(versionContext, FhirVersion.R4);

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
    public void BuildVersionsParameters_MarksR4AsDefaultWhenSpecified()
    {
        // Arrange
        var versionContext = CreateMockVersionContext();

        // Act
        var parameters = MetadataEndpoints.BuildVersionsParameters(versionContext, FhirVersion.R4);

        // Find default version
        var defaultVersionCode = FindDefaultVersion(parameters);

        // Assert
        defaultVersionCode.Should().Be("4.0.1", "R4 should be the default version when specified");
    }

    [Fact]
    public void BuildVersionsParameters_MarksR5AsDefaultWhenTenantConfiguredForR5()
    {
        // Arrange
        var versionContext = CreateMockVersionContext();

        // Act - tenant configured for R5
        var parameters = MetadataEndpoints.BuildVersionsParameters(versionContext, FhirVersion.R5);

        // Find default version
        var defaultVersionCode = FindDefaultVersion(parameters);

        // Assert
        defaultVersionCode.Should().Be("5.0.0", "R5 should be the default version when tenant is configured for R5");
    }

    [Fact]
    public void BuildVersionsParameters_MarksStu3AsDefaultWhenTenantConfiguredForStu3()
    {
        // Arrange
        var versionContext = CreateMockVersionContext();

        // Act - tenant configured for Stu3
        var parameters = MetadataEndpoints.BuildVersionsParameters(versionContext, FhirVersion.Stu3);

        // Find default version
        var defaultVersionCode = FindDefaultVersion(parameters);

        // Assert
        defaultVersionCode.Should().Be("3.0.2", "Stu3 should be the default version when tenant is configured for Stu3");
    }

    [Fact]
    public void BuildVersionsParameters_NoDefaultWhenNullSpecified()
    {
        // Arrange
        var versionContext = CreateMockVersionContext();

        // Act - no default specified
        var parameters = MetadataEndpoints.BuildVersionsParameters(versionContext, null);

        // Find default version
        var defaultVersionCode = FindDefaultVersion(parameters);

        // Assert
        defaultVersionCode.Should().BeNull("no version should be marked as default when null is specified");
    }

    [Fact]
    public void BuildVersionsParameters_SerializesToValidJson()
    {
        // Arrange
        var versionContext = CreateMockVersionContext();

        // Act
        var parameters = MetadataEndpoints.BuildVersionsParameters(versionContext, FhirVersion.R4);
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

    private static string? FindDefaultVersion(ParametersJsonNode parameters)
    {
        foreach (var versionParam in parameters.Parameter.Where(p => p.Name == "version"))
        {
            var defaultPart = versionParam.Part.FirstOrDefault(p => p.Name == "default");
            if (defaultPart != null)
            {
                var isDefault = defaultPart.GetValueAs<bool>("valueBoolean");
                if (isDefault)
                {
                    var codePart = versionParam.Part.FirstOrDefault(p => p.Name == "code");
                    return codePart?.GetValueAs<string>("valueCode");
                }
            }
        }

        return null;
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
