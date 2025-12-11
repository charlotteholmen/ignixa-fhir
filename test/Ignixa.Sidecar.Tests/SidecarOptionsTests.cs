// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Sidecar.Configuration;

namespace Ignixa.Sidecar.Tests;

public class SidecarOptionsTests
{
    [Fact]
    public void SidecarOptions_DefaultValues_ShouldBeConfigured()
    {
        // Arrange & Act
        var options = new SidecarOptions();

        // Assert
        options.ProviderMode.Should().Be(ProviderMode.Local);
        options.Endpoint.Should().Be("http://localhost:5050");
        options.TimeoutMs.Should().Be(5000);
        options.RetryCount.Should().Be(3);
        options.FailOpen.Should().BeFalse();
    }

    [Fact]
    public void CircuitBreakerOptions_DefaultValues_ShouldBeConfigured()
    {
        // Arrange & Act
        var options = new CircuitBreakerOptions();

        // Assert
        options.FailureThreshold.Should().Be(5);
        options.SamplingDurationSeconds.Should().Be(30);
        options.MinimumThroughput.Should().Be(10);
        options.BreakDurationSeconds.Should().Be(30);
    }

    [Fact]
    public void HybridOptions_DefaultValues_ShouldBeLocal()
    {
        // Arrange & Act
        var options = new HybridOptions();

        // Assert
        options.Authorization.Should().Be(ProviderMode.Local);
        options.AuditLogging.Should().Be(ProviderMode.Local);
        options.Logging.Should().Be(ProviderMode.Local);
    }

    [Fact]
    public void SidecarOptions_SectionName_ShouldBeCorrect()
    {
        // Assert
        SidecarOptions.SectionName.Should().Be("Sidecar");
    }

    [Fact]
    public void ProviderMode_AllValues_ShouldBeDefined()
    {
        // Assert
        Enum.GetValues<ProviderMode>().Should().HaveCount(3);
        Enum.IsDefined(ProviderMode.Local).Should().BeTrue();
        Enum.IsDefined(ProviderMode.Sidecar).Should().BeTrue();
        Enum.IsDefined(ProviderMode.Hybrid).Should().BeTrue();
    }

    [Theory]
    [InlineData(ProviderMode.Local)]
    [InlineData(ProviderMode.Sidecar)]
    [InlineData(ProviderMode.Hybrid)]
    public void SidecarOptions_CanSetProviderMode(ProviderMode mode)
    {
        // Arrange & Act
        var options = new SidecarOptions { ProviderMode = mode };

        // Assert
        options.ProviderMode.Should().Be(mode);
    }

    [Fact]
    public void SidecarOptions_CanConfigureCircuitBreaker()
    {
        // Arrange & Act
        var options = new SidecarOptions
        {
            CircuitBreaker = new CircuitBreakerOptions
            {
                FailureThreshold = 10,
                SamplingDurationSeconds = 60,
                MinimumThroughput = 20,
                BreakDurationSeconds = 60
            }
        };

        // Assert
        options.CircuitBreaker.FailureThreshold.Should().Be(10);
        options.CircuitBreaker.SamplingDurationSeconds.Should().Be(60);
        options.CircuitBreaker.MinimumThroughput.Should().Be(20);
        options.CircuitBreaker.BreakDurationSeconds.Should().Be(60);
    }
}
