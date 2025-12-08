using System.CommandLine;

namespace Ignixa.Cli.Commands;

/// <summary>
/// Command for displaying context-specific help.
/// </summary>
internal static class HelpCommand
{
    public static Command Create()
    {
        var helpCommand = new Command("help", "Display detailed help for commands and available options");

        helpCommand.SetHandler(() =>
        {
            ShowGeneralHelp();
        });

        return helpCommand;
    }

    private static void ShowGeneralHelp()
    {
        Console.WriteLine("Ignixa CLI - Manage and test your Ignixa FHIR server");
        Console.WriteLine();
        Console.WriteLine("Available commands:");
        Console.WriteLine("  ignixa push        - Push a FHIR resource or bundle to the server");
        Console.WriteLine("  ignixa search      - Search for FHIR resources on the server");
        Console.WriteLine("  ignixa job         - Manage import/export jobs");
        Console.WriteLine("  ignixa help        - Display this help message");
        Console.WriteLine();
        Console.WriteLine("For command-specific help, use:");
        Console.WriteLine("  ignixa push --help");
        Console.WriteLine("  ignixa search --help");
        Console.WriteLine("  ignixa job --help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ignixa push --url http://localhost:5000 --file mybundle.json --tenant 1");
        Console.WriteLine("  ignixa search patient --firstname=bob --surname=smith");
        Console.WriteLine("  ignixa job list");
        Console.WriteLine("  ignixa job import --input \"file.json\" --tenant 1");
        Console.WriteLine("  ignixa job export --type \"Patient\" --tenant 1");
    }
}
