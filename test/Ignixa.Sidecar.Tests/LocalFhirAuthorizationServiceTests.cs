// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Ignixa.Sidecar.Tests;

public class LocalFhirAuthorizationServiceTests
{
    private readonly ILogger<LocalFhirAuthorizationService> _logger;
    private readonly LocalFhirAuthorizationService _sut;

    public LocalFhirAuthorizationServiceTests()
    {
        _logger = Substitute.For<ILogger<LocalFhirAuthorizationService>>();
        _sut = new LocalFhirAuthorizationService(_logger);
    }

    [Fact]
    public async Task AuthorizeAsync_WithResourceAction_ShouldAlwaysPermit()
    {
        // Arrange
        var user = CreateClaimsPrincipal("user123");

        // Act
        var result = await _sut.AuthorizeAsync(
            user,
            tenantId: 1,
            resourceType: "Patient",
            resourceId: "123",
            action: "read");

        // Assert
        result.IsAuthorized.Should().BeTrue();
        result.Reason.Should().Contain("Local mode");
    }

    [Fact]
    public async Task AuthorizeAsync_WithPolicy_ShouldAlwaysPermit()
    {
        // Arrange
        var user = CreateClaimsPrincipal("user456");

        // Act
        var result = await _sut.AuthorizeAsync(
            user,
            tenantId: 2,
            policyName: "FhirUserPolicy");

        // Assert
        result.IsAuthorized.Should().BeTrue();
        result.Reason.Should().Contain("FhirUserPolicy");
    }

    [Fact]
    public async Task AuthorizeAsync_WithAnonymousUser_ShouldAlwaysPermit()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // No claims

        // Act
        var result = await _sut.AuthorizeAsync(
            user,
            tenantId: 1,
            resourceType: "Observation",
            resourceId: null,
            action: "search");

        // Assert
        result.IsAuthorized.Should().BeTrue();
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("delete")]
    [InlineData("search")]
    public async Task AuthorizeAsync_AllActions_ShouldPermit(string action)
    {
        // Arrange
        var user = CreateClaimsPrincipal("test-user");

        // Act
        var result = await _sut.AuthorizeAsync(
            user,
            tenantId: 1,
            resourceType: "Patient",
            resourceId: "123",
            action: action);

        // Assert
        result.IsAuthorized.Should().BeTrue();
    }

    private static ClaimsPrincipal CreateClaimsPrincipal(string userId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId)
        };
        var identity = new ClaimsIdentity(claims, "Test");
        return new ClaimsPrincipal(identity);
    }
}
