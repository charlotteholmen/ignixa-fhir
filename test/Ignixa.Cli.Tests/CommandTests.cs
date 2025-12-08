using FluentAssertions;

namespace Ignixa.Cli.Tests;

/// <summary>
/// Basic tests for the Ignixa CLI tool.
/// </summary>
public class CommandTests
{
    [Fact]
    public void CLI_ShouldCompile()
    {
        // This test verifies that the CLI project compiles successfully
        // The fact that this test runs means the project was built
        true.Should().BeTrue();
    }
}
