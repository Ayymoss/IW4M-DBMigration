using System.Reflection;
using IWDataMigration.Abstractions;
using IWDataMigration.Models;
using Microsoft.Extensions.Options;

namespace IWDataMigration.Services;

/// <summary>
/// Manages migration configuration including connection strings and database type selection.
/// </summary>
public sealed class ConfigurationService(IConsoleService console, IOptions<MigrationOptions> options) : IConfigurationService
{
    private readonly MigrationOptions _options = options.Value;
    private readonly string _executingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

    private string _sourceConnection = string.Empty;
    private string _targetConnection = string.Empty;
    private DatabaseType _sourceType;
    private DatabaseType _targetType;

    public string SourceConnection => _sourceConnection;
    public string TargetConnection => _targetConnection;
    public DatabaseType SourceType => _sourceType;
    public DatabaseType TargetType => _targetType;
    
    public async Task SetupStructureAsync()
    {
        EnsureDirectoriesExist();
        await EnsureConnectionStringFileExistsAsync();
    }

    public async Task InitializeAsync()
    {
        console.DisplayRule("Locations");
        console.DisplayMessage($"Database Source Directory: {GetDatabaseSourcePath()}");
        console.DisplayMessage($"Connection String Location: {GetConnectionStringPath()}");
        Console.WriteLine();

        // Select source database type
        console.DisplayRule("Source Database");
        _sourceType = console.Prompt(
            "Select [green]Source[/] Database Type",
            new[] { DatabaseType.Sqlite, DatabaseType.MySql });

        // Configure source connection
        if (_sourceType == DatabaseType.Sqlite)
        {
            var databaseName = console.PromptText(
                "[grey][[Default: Database.db]][/] What is your [green]database file name[/]?",
                _options.DefaultDatabaseFileName);

            var dbPath = Path.Join(GetDatabaseSourcePath(), databaseName);
            if (!File.Exists(dbPath))
            {
                console.DisplayError($"{databaseName} doesn't exist in DatabaseSource directory.");
                console.WaitForKey("Press any key to exit.");
                Environment.Exit(1);
            }

            _sourceConnection = $"Data Source={dbPath}";
            await BackupDatabaseAsync(dbPath);
        }
        else
        {
            var sourceConnPath = Path.Join(_executingDirectory, "_SourceConnectionString.txt");
            if (!File.Exists(sourceConnPath))
            {
                await File.WriteAllTextAsync(sourceConnPath, _options.DefaultConnectionStringTemplate);
                console.DisplayError("_SourceConnectionString.txt created. Please configure it and restart.");
                console.WaitForKey("Press any key to exit.");
                Environment.Exit(1);
            }

            _sourceConnection = await File.ReadAllTextAsync(sourceConnPath);
            if (_sourceConnection == _options.DefaultConnectionStringTemplate)
            {
                console.DisplayError("_SourceConnectionString.txt is unmodified. Please update it.");
                console.WaitForKey("Press any key to exit.");
                Environment.Exit(1);
            }
        }

        // Select target database type
        console.DisplayRule("Target Database");
        _targetType = console.Prompt(
            "Select [green]Target[/] Database Type",
            new[] { DatabaseType.PostgreSql, DatabaseType.MySql });

        // Get target connection string
        _targetConnection = await File.ReadAllTextAsync(GetConnectionStringPath());
        if (_targetConnection == _options.DefaultConnectionStringTemplate)
        {
            console.DisplayError("_TargetConnectionString.txt is unmodified. Please update it with target connection.");
            console.WaitForKey("Press any key to exit.");
            Environment.Exit(1);
        }

        console.DisplayMessage($"[green]Source:[/] {_sourceType} -> [green]Target:[/] {_targetType}");
        console.DisplayMessage("[yellow]Close the application now if this is a mistake.[/]");
        Console.WriteLine();
    }

    public bool Validate()
    {
        return !string.IsNullOrWhiteSpace(_sourceConnection) &&
               !string.IsNullOrWhiteSpace(_targetConnection);
    }

    private void EnsureDirectoriesExist()
    {
        var dbSourcePath = GetDatabaseSourcePath();
        if (!Directory.Exists(dbSourcePath))
        {
            Directory.CreateDirectory(dbSourcePath);
        }
    }

    private async Task EnsureConnectionStringFileExistsAsync()
    {
        var path = GetConnectionStringPath();
        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(path, _options.DefaultConnectionStringTemplate);
            console.DisplayMessage("_TargetConnectionString.txt file created. Please configure it.");
        }
    }

    private async Task BackupDatabaseAsync(string dbPath)
    {
#if !DEBUG
        console.DisplayMessage($"Backing up {Path.GetFileName(dbPath)}...");
        try
        {
            var backupPath = $"{dbPath}-{DateTimeOffset.UtcNow.ToFileTime()}.bak";
            await Task.Run(() => File.Copy(dbPath, backupPath, true));
        }
        catch (Exception e)
        {
            console.DisplayException(e);
        }
#endif
        await Task.CompletedTask;
    }

    private string GetDatabaseSourcePath() =>
        Path.Join(_executingDirectory, _options.DatabaseSourceDirectory);

    private string GetConnectionStringPath() =>
        Path.Join(_executingDirectory, _options.ConnectionStringFileName);
}
