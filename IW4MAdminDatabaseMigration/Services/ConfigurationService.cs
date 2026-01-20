using System.Reflection;
using System.Text.Json;
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
        await LoadUserSettingsAsync();
    }

    public async Task InitializeAsync()
    {
        // Display loaded settings
        console.DisplayRule("Settings");
        var settings = _options.UserSettings;
        console.DisplayMessage($"Batch Size: [cyan]{settings.BatchSize:N0}[/] rows");
        console.DisplayMessage($"Checkpoint Interval: [cyan]{settings.CheckpointIntervalSeconds}[/] seconds");
        console.DisplayMessage($"Watchdog Timeout: [cyan]{settings.WatchdogTimeoutMinutes}[/] minutes");
        console.DisplayMessage($"Max Retries: [cyan]{settings.MaxBatchRetries}[/]");
        if (settings.VerboseLogging)
        {
            console.DisplayMessage("[yellow]Verbose logging enabled[/]");
        }
        Console.WriteLine();

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

    public async Task InitializeFromResumeAsync(DatabaseType sourceType, DatabaseType targetType)
    {
        // Set types from saved state
        _sourceType = sourceType;
        _targetType = targetType;

        // Display loaded settings
        console.DisplayRule("Settings (from previous session)");
        var settings = _options.UserSettings;
        console.DisplayMessage($"Batch Size: [cyan]{settings.BatchSize:N0}[/] rows");
        console.DisplayMessage($"Watchdog Timeout: [cyan]{settings.WatchdogTimeoutMinutes}[/] minutes");
        Console.WriteLine();

        // Load source connection based on type
        if (_sourceType == DatabaseType.Sqlite)
        {
            // For SQLite, we need to find the database file
            // Use the default name since we can't prompt
            var dbPath = Path.Join(GetDatabaseSourcePath(), _options.DefaultDatabaseFileName);
            if (!File.Exists(dbPath))
            {
                // Try to find any .db file
                var dbFiles = Directory.GetFiles(GetDatabaseSourcePath(), "*.db");
                if (dbFiles.Length == 0)
                {
                    console.DisplayError("No database file found in DatabaseSource directory.");
                    console.WaitForKey("Press any key to exit.");
                    Environment.Exit(1);
                }
                dbPath = dbFiles[0];
            }
            _sourceConnection = $"Data Source={dbPath}";
            console.DisplayMessage($"Source: [cyan]{Path.GetFileName(dbPath)}[/] (SQLite)");
        }
        else
        {
            // MySQL/MariaDB - load from connection string file
            var sourceConnPath = Path.Join(_executingDirectory, "_SourceConnectionString.txt");
            if (!File.Exists(sourceConnPath))
            {
                console.DisplayError("_SourceConnectionString.txt not found. Cannot resume.");
                console.WaitForKey("Press any key to exit.");
                Environment.Exit(1);
            }
            _sourceConnection = await File.ReadAllTextAsync(sourceConnPath);
            console.DisplayMessage($"Source: [cyan]MySQL/MariaDB[/] (from connection file)");
        }

        // Load target connection
        _targetConnection = await File.ReadAllTextAsync(GetConnectionStringPath());
        console.DisplayMessage($"Target: [cyan]{_targetType}[/] (from connection file)");
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

    private string GetSettingsPath() =>
        Path.Join(_executingDirectory, _options.SettingsFileName);

    private async Task LoadUserSettingsAsync()
    {
        var settingsPath = GetSettingsPath();

        if (!File.Exists(settingsPath))
        {
            // Create default settings file
            var defaultSettings = new UserSettings();
            var json = JsonSerializer.Serialize(defaultSettings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(settingsPath, json);
            console.DisplayMessage($"Created [cyan]{_options.SettingsFileName}[/] with default settings.");
            _options.UserSettings = defaultSettings;
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(settingsPath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json);
            if (settings is not null)
            {
                // Validate settings
                if (settings.BatchSize <= 0) settings.BatchSize = 25_000;
                if (settings.CheckpointIntervalSeconds <= 0) settings.CheckpointIntervalSeconds = 30;
                if (settings.WatchdogTimeoutMinutes <= 0) settings.WatchdogTimeoutMinutes = 5;
                if (settings.MaxBatchRetries <= 0) settings.MaxBatchRetries = 3;

                _options.UserSettings = settings;
            }
        }
        catch (JsonException ex)
        {
            console.DisplayError($"Failed to parse {_options.SettingsFileName}: {ex.Message}");
            console.DisplayMessage("Using default settings.");
        }
    }
}
