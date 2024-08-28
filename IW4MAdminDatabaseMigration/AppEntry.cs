using System.Reflection;
using Data.MigrationContext;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace IWDataMigration;

public class AppEntry
{
    private const string DefaultConnectionString = "Host=HOSTNAME;Port=PORT;Username=USERNAME;Password=PASSWORD;Database=DATABASE";
    private string? _connectionString;
    private SqliteDatabaseContext? _sourceContext;

    private readonly Dictionary<string, DatabaseTypes> _migrationChoices = new()
    {
        {"PostgreSQL", DatabaseTypes.Postgres},
        {"MariaDB", DatabaseTypes.MariaDb}
    };

    public async Task App()
    {
        try
        {
            PrintHeader();

            var executingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var directory = Path.Join(executingDirectory, "DatabaseSource");
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            SetConnectionString(executingDirectory!);

            var locRule = new Rule("[yellow]Locations[/]") {Justification = Justify.Left};
            AnsiConsole.Write(locRule);
            AnsiConsole.MarkupLine($"Database Source Directory: {directory}");
            AnsiConsole.MarkupLine($"Connection String Location: {Path.Join(executingDirectory, "_ConnectionString.txt")}");
            Console.WriteLine();

            var instRule = new Rule("[yellow]Instructions[/]") {Justification = Justify.Left};
            AnsiConsole.Write(instRule);
            Instructions();

            var rule = new Rule("[yellow]Database Configuration[/]") {Justification = Justify.Left};
            AnsiConsole.Write(rule);
            _connectionString = await GetConnectionString(executingDirectory!);
            var databaseName = GetDatabaseName();
            ValidateDatabaseDirectory(directory, databaseName);
            BackupDatabaseIfExists(directory, databaseName);

            var sourceOptions = new DbContextOptionsBuilder<SqliteDatabaseContext>()
                .UseSqlite($"Data Source={Path.Join(directory, databaseName)}")
                .Options;

            _sourceContext = new SqliteDatabaseContext(sourceOptions);
            await _sourceContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=OFF;");
            await _sourceContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;");
            _sourceContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

            var promptSelection = AnsiConsole.Prompt(new SelectionPrompt<string> {HighlightStyle = new Style().Foreground(Color.Cyan1)}
                .Title("Select Migration Target Database")
                .AddChoices(_migrationChoices.Keys));
            AnsiConsole.MarkupLine($"{promptSelection} selected. Close the application now if this is a mistake.");
            Console.WriteLine();

            var procRule = new Rule("[red]Migration Started[/]") {Justification = Justify.Left};
            AnsiConsole.Write(procRule);

            var selectedDatabase = _migrationChoices[promptSelection];
            switch (selectedDatabase)
            {
                case DatabaseTypes.Postgres:
                    await MigrateToPostgreSql();
                    break;
                case DatabaseTypes.MariaDb:
                    await MigrateToMySql();
                    break;
                default:
                    AnsiConsole.MarkupLine("Invalid input. Exiting...");
                    Console.ReadKey();
                    Environment.Exit(1);
                    break;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        DisplayFinalMessages();
    }

    private static void Instructions()
    {
        AnsiConsole.MarkupLine("1) Place your database file in the DatabaseSource directory.");
        AnsiConsole.MarkupLine("2) Edit the _ConnectionString.txt file with your connection string.");
        var key = AnsiConsole.Confirm("Continue to migration?");
        if (!key) Environment.Exit(1);
        Console.WriteLine();
    }

    private static void DisplayFinalMessages()
    {
        Console.WriteLine();
        AnsiConsole.MarkupLine("=====================================================");
        AnsiConsole.MarkupLine(" All tables migrated successfully.");
        AnsiConsole.MarkupLine(" Change IW4MAdminSettings.json to reflect the new database.");
        AnsiConsole.MarkupLine(" If you need further help, please ask in Discord.");
        AnsiConsole.MarkupLine(" IW4MAdmin Support: https://discord.gg/ZZFK5p3");
        AnsiConsole.MarkupLine("=====================================================");
        Console.WriteLine();
        AnsiConsole.MarkupLine("Press any key to exit.");
        Console.ReadKey();
    }

    private static string GetDatabaseName()
    {
        AnsiConsole.MarkupLine("Enter the name of your Database file. (Enter to accept default)");
        var databaseName =
            AnsiConsole.Prompt(
                new TextPrompt<string>("[grey][[Default: Database.db]][/] What is your [green]database name[/]?").AllowEmpty());
        if (string.IsNullOrWhiteSpace(databaseName)) databaseName = "Database.db";
        return databaseName;
    }

    private static void PrintHeader()
    {
        Console.WriteLine();
        AnsiConsole.MarkupLine("=====================================================");
        AnsiConsole.MarkupLine(" IW4MAdmin Database Migration Utility");
        AnsiConsole.MarkupLine(" by Ayymoss#8334 ");
        AnsiConsole.MarkupLine($" Version {Assembly.GetExecutingAssembly().GetName().Version}");
        AnsiConsole.MarkupLine("=====================================================");
        Console.WriteLine();
    }

    private static void ValidateDatabaseDirectory(string directory, string databaseName)
    {
        if (File.Exists(Path.Join(directory, databaseName))) return;
        AnsiConsole.MarkupLine($"{databaseName} doesn't exist in DatabaseSource directory.");
        AnsiConsole.MarkupLine("Press any key to exit.");
        Console.ReadKey();
        Environment.Exit(1);
    }

    private void BackupDatabaseIfExists(string directory, string databaseName)
    {
        AnsiConsole.MarkupLine($"Backing up {databaseName}...");
        try
        {
#if !DEBUG
            File.Copy(Path.Join(directory, databaseName), Path.Join(directory, $"{databaseName}-{DateTimeOffset.UtcNow.ToFileTime()}.db"),
                true);
#endif
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
        }
    }

    private async void SetConnectionString(string executingDirectory)
    {
        if (File.Exists(Path.Join(executingDirectory, "_ConnectionString.txt"))) return;
        await File.WriteAllTextAsync(Path.Join(executingDirectory, "_ConnectionString.txt"), DefaultConnectionString);
        AnsiConsole.MarkupLine("_ConnectionString.txt file doesn't exist. _ConnectionString.txt has been created.");
    }

    private async Task<string> GetConnectionString(string executingDirectory)
    {
        var connectionString = await File.ReadAllTextAsync(Path.Join(executingDirectory, "_ConnectionString.txt"));
        if (connectionString != DefaultConnectionString) return connectionString;

        AnsiConsole.MarkupLine("_ConnectionString.txt is unmodified. Please update _ConnectionString.txt to reflect your target!");
        AnsiConsole.MarkupLine("Press any key to exit.");
        Console.ReadKey();
        Environment.Exit(1);

        return connectionString;
    }

    private async Task MigrateToPostgreSql()
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || _sourceContext is null) return;
        var postgresTargetOptions = new DbContextOptionsBuilder<PostgresqlDatabaseContext>()
            .UseNpgsql(_connectionString)
            .EnableDetailedErrors()
            .EnableSensitiveDataLogging()
            .Options;

        var migrationHelper = new MigrationHelper(_sourceContext, () => new PostgresqlDatabaseContext(postgresTargetOptions));
        await migrationHelper.MigrateDataAsync();
    }

    private async Task MigrateToMySql()
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || _sourceContext is null) return;
        var mySqlTargetOptions = new DbContextOptionsBuilder<MySqlDatabaseContext>()
            .UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString))
            .EnableDetailedErrors()
            .EnableSensitiveDataLogging()
            .Options;

        var migrationHelper = new MigrationHelper(_sourceContext, () => new MySqlDatabaseContext(mySqlTargetOptions));
        await migrationHelper.MigrateDataAsync();
    }
}
// TODO: use inheritance for post and mysql
// TODO: use migration factory (selection user made -> specific implementation inherit from interface -> factory returns specific implementation)
