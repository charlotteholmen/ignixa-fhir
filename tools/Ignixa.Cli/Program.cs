using System.CommandLine;
using Ignixa.Cli.Commands;

namespace Ignixa.Cli;

/// <summary>
/// Entry point for the Ignixa CLI tool.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Create root command
        var rootCommand = new RootCommand("Ignixa CLI - Manage and test your Ignixa FHIR server");

        // Add help command for discoverability
        rootCommand.AddCommand(HelpCommand.Create());

        // Add tenants command (root-level)
        rootCommand.AddCommand(TenantsCommand.Create());

        // Add push command
        rootCommand.AddCommand(PushCommand.Create());

        // Add search command
        rootCommand.AddCommand(SearchCommand.Create());

        // Add job command group
        rootCommand.AddCommand(JobCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }
}
