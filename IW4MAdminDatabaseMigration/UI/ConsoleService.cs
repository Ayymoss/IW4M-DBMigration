using System.Reflection;
using IWDataMigration.Abstractions;
using Spectre.Console;

namespace IWDataMigration.UI;

/// <summary>
/// Handles all console UI operations using Spectre.Console.
/// </summary>
public sealed class ConsoleService : IConsoleService
{
    public void DisplayHeader()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("=====================================================");
        AnsiConsole.MarkupLine(" [bold cyan]IW4MAdmin Database Migration Utility[/]");
        AnsiConsole.MarkupLine(" [dim]by Ayymoss[/]");
        AnsiConsole.MarkupLine($" [dim]Version {Assembly.GetExecutingAssembly().GetName().Version}[/]");
        AnsiConsole.MarkupLine("=====================================================");
        AnsiConsole.WriteLine();
    }

    public bool DisplayInstructions()
    {
        AnsiConsole.MarkupLine("1) Place your source database file in the [green]DatabaseSource[/] directory.");
        AnsiConsole.MarkupLine("   [dim](SQLite: .db file, MariaDB/MySQL: configure connection string)[/]");
        AnsiConsole.MarkupLine("2) Edit the [green]_TargetConnectionString.txt[/] file with your [bold]target[/] connection string.");
        AnsiConsole.MarkupLine("3) For MariaDB/MySQL sources, create [green]_SourceConnectionString.txt[/].");
        AnsiConsole.WriteLine();
        return Confirm("Continue to migration?");
    }

    public void DisplayRule(string title, string color = "yellow")
    {
        var rule = new Rule($"[{color}]{title}[/]") { Justification = Justify.Left };
        AnsiConsole.Write(rule);
    }

    public void DisplayMessage(string message)
    {
        AnsiConsole.MarkupLine(message);
    }

    public void DisplayError(string message)
    {
        AnsiConsole.MarkupLine($"[red]ERROR:[/] {message}");
    }

    public void DisplayFinalMessages()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("=====================================================");
        AnsiConsole.MarkupLine(" [green]All tables migrated successfully.[/]");
        AnsiConsole.MarkupLine(" Change IW4MAdminSettings.json to reflect the new database.");
        AnsiConsole.MarkupLine(" If you need further help, please ask in Discord.");
        AnsiConsole.MarkupLine(" IW4MAdmin Support: [link]https://discord.gg/ZZFK5p3[/]");
        AnsiConsole.MarkupLine("=====================================================");
        AnsiConsole.WriteLine();
    }

    public T Prompt<T>(string title, IEnumerable<T> choices) where T : notnull
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<T>()
                .Title(title)
                .HighlightStyle(new Style().Foreground(Color.Cyan1))
                .AddChoices(choices));
    }

    public string PromptText(string prompt, string? defaultValue = null)
    {
        var textPrompt = new TextPrompt<string>(prompt).AllowEmpty();
        if (defaultValue is not null)
        {
            textPrompt.DefaultValue(defaultValue);
        }

        var result = AnsiConsole.Prompt(textPrompt);
        return string.IsNullOrWhiteSpace(result) && defaultValue is not null ? defaultValue : result;
    }

    public bool Confirm(string message)
    {
        return AnsiConsole.Confirm(message);
    }

    public void WaitForKey(string? message = null)
    {
        if (message is not null)
        {
            AnsiConsole.MarkupLine(message);
        }

        Console.ReadKey(true);
    }

    public void DisplayException(Exception ex)
    {
        AnsiConsole.WriteException(ex);
    }

    public bool PromptResume(string tableName, int rowsCompleted, long totalMigrated, DateTime lastUpdated)
    {
        AnsiConsole.WriteLine();
        DisplayRule("Previous Migration Found", "yellow");
        AnsiConsole.MarkupLine($"  Last active table: [cyan]{tableName}[/]");
        AnsiConsole.MarkupLine($"  Rows in current table: [cyan]{rowsCompleted:N0}[/]");
        AnsiConsole.MarkupLine($"  Total rows migrated: [cyan]{totalMigrated:N0}[/]");
        AnsiConsole.MarkupLine($"  Last updated: [cyan]{lastUpdated:g}[/] UTC");
        AnsiConsole.WriteLine();

        return Confirm("Resume from this checkpoint?");
    }
}
