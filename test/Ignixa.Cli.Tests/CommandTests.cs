using FluentAssertions;

namespace Ignixa.Cli.Tests;

/// <summary>
/// Basic tests for the Ignixa CLI tool.
/// </summary>
public class CommandTests
{
    [Fact]
    public void HelpCommand_ShouldExist()
    {
        // This test verifies that the help command can be instantiated
        var helpCommand = Commands.HelpCommand.Create();
        helpCommand.Should().NotBeNull();
        helpCommand.Name.Should().Be("help");
    }

    [Fact]
    public void PushCommand_ShouldExist()
    {
        // This test verifies that the push command can be instantiated
        var pushCommand = Commands.PushCommand.Create();
        pushCommand.Should().NotBeNull();
        pushCommand.Name.Should().Be("push");
    }

    [Fact]
    public void SearchCommand_ShouldExist()
    {
        // This test verifies that the search command can be instantiated
        var searchCommand = Commands.SearchCommand.Create();
        searchCommand.Should().NotBeNull();
        searchCommand.Name.Should().Be("search");
    }

    [Fact]
    public void JobCommand_ShouldExist()
    {
        // This test verifies that the job command can be instantiated
        var jobCommand = Commands.JobCommand.Create();
        jobCommand.Should().NotBeNull();
        jobCommand.Name.Should().Be("job");
    }
}
