// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;

namespace Ignixa.Api.E2ETests;

/// <summary>
/// E2E tests for the $versions operation.
/// Tests the FHIR $versions operation that returns supported FHIR versions.
/// https://build.fhir.org/capabilitystatement-operation-versions.html
/// </summary>
public class VersionsOperationTests : IClassFixture<IgnixaApiFixture>
{
    private readonly IgnixaApiFixture _fixture;

    public VersionsOperationTests(IgnixaApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenServer_WhenCallingVersions_ThenReturnsParametersResource()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/$versions");

        // Assert
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var parameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(json);

        parameters.ResourceType.Should().Be("Parameters");
        parameters.Parameter.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GivenServer_WhenCallingVersions_ThenReturnsMultipleFhirVersions()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/$versions");

        // Assert
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var parameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(json);

        // Should have multiple version parameters
        var versionParams = parameters.Parameter.Where(p => p.Name == "version").ToList();
        versionParams.Should().HaveCountGreaterThanOrEqualTo(4, "should support at least R4, R4B, R5, Stu3");
    }

    [Fact]
    public async Task GivenServer_WhenCallingVersions_ThenIncludesExpectedVersionCodes()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/$versions");

        // Assert
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var parameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(json);

        // Extract version codes from nested parts
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

        // Should include well-known FHIR version codes
        versionCodes.Should().Contain("4.0.1", "R4 should be supported");
        versionCodes.Should().Contain("4.3.0", "R4B should be supported");
        versionCodes.Should().Contain("5.0.0", "R5 should be supported");
        versionCodes.Should().Contain("3.0.2", "Stu3 should be supported");
    }

    [Fact]
    public async Task GivenServer_WhenCallingVersions_ThenIndicatesDefaultVersion()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/$versions");

        // Assert
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var parameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(json);

        // Find the version marked as default
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

        // Should have exactly one default version
        defaultVersions.Should().HaveCount(1, "exactly one version should be marked as default");
        defaultVersions[0].Should().Be("4.0.1", "R4 should be the default version");
    }

    [Fact]
    public async Task GivenServer_WhenCallingVersionsOnTenantEndpoint_ThenReturnsSameVersions()
    {
        // Act - Call both agnostic and tenant-specific endpoints
        var agnosticResponse = await _fixture.Client.GetAsync("/$versions");
        var tenantResponse = await _fixture.Client.GetAsync("/tenant/1/$versions");

        // Assert - Both should succeed
        agnosticResponse.EnsureSuccessStatusCode();
        tenantResponse.EnsureSuccessStatusCode();

        var agnosticJson = await agnosticResponse.Content.ReadAsStringAsync();
        var tenantJson = await tenantResponse.Content.ReadAsStringAsync();

        // Parse both responses
        var agnosticParameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(agnosticJson);
        var tenantParameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(tenantJson);

        // Extract version codes from both
        var agnosticVersions = ExtractVersionCodes(agnosticParameters);
        var tenantVersions = ExtractVersionCodes(tenantParameters);

        // Should have the same versions
        agnosticVersions.Should().BeEquivalentTo(tenantVersions);
    }

    [Fact]
    public async Task GivenServer_WhenCallingVersionsWithPrettyParam_ThenReturnsFormattedResponse()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/$versions?_pretty=true");

        // Assert
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        // Pretty-printed JSON should contain newlines and indentation
        json.Should().Contain("\n", "pretty output should contain newlines");
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
