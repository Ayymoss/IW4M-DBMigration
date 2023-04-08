using System.Reflection;
using Data.Context;
using Data.MigrationContext;
using Microsoft.EntityFrameworkCore;

namespace IWDataMigration;

public static class IwDataMigration
{
    private const string DefaultConnectionString = "Host=HOSTNAME;Port=PORT;Username=USERNAME;Password=PASSWORD;Database=DATABASE";

    public static async Task Main()
    {
        try
        {
            PrintHeader();

            var executingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var directory = Path.Join(executingDirectory, "DatabaseSource");
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            SetConnectionString(executingDirectory!);

            Console.WriteLine($"Database Source Directory: {directory}");
            Console.WriteLine($"Connection String Location: {Path.Join(executingDirectory, "_ConnectionString.txt")}");

            Instructions();

            var connectionString = await GetConnectionString(executingDirectory!);
            var databaseName = GetDatabaseName();
            ValidateDatabaseDirectory(directory, databaseName);
            BackupDatabaseIfExists(directory, databaseName);

            var sourceOptions = new DbContextOptionsBuilder<SqliteDatabaseContext>()
                .UseSqlite($"Data Source={Path.Join(directory, databaseName)}")
                .Options;

            var sourceContext = new SqliteDatabaseContext(sourceOptions);
            await sourceContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=OFF;");
            await sourceContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;");
            sourceContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

            Console.WriteLine();
            Console.WriteLine("Migration to:");
            Console.WriteLine("1) PostgreSQL");
            Console.WriteLine("2) MariaDB");
            Console.Write("Enter [1 or 2]: ");
            var input = Console.ReadLine();
            Console.WriteLine();

            switch (input)
            {
                case "1":
                    Console.WriteLine("PostgreSQL selected. Enter 'y' to continue migration...");
                    Console.Write("If this is a mistake, press any key to exit. ");
                    var pKey = Console.ReadLine();
                    if (pKey != "y") Environment.Exit(1);
                    await MigrateToPostgreSql(connectionString, sourceContext);
                    break;
                case "2":
                    Console.WriteLine("MySQL/MariaDB selected. Enter 'y' to continue migration...");
                    Console.Write("If this is a mistake, press any key to exit. ");
                    var mKey = Console.ReadLine();
                    if (mKey != "y") Environment.Exit(1);
                    await MigrateToMySql(connectionString, sourceContext);
                    break;
                default:
                    Console.WriteLine("Invalid input. Exiting...");
                    Console.ReadKey();
                    Environment.Exit(1);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}\n{ex.StackTrace ?? "No trace"}");
            Console.WriteLine(
                $"Inner exception: {(ex.InnerException?.Message is null ? "None" : $"{ex.InnerException?.Message}\n{ex.StackTrace ?? "No trace"}")}");
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }

    private static void Instructions()
    {
        Console.WriteLine();
        Console.WriteLine("Instructions:");
        Console.WriteLine("1) Place your database file in the DatabaseSource directory.");
        Console.WriteLine("2) Edit the _ConfigurationString.txt file with your connection string.");
        Console.Write("3) Enter 'y' to continue migration. ");
        var key = Console.ReadLine();
        if (key != "y") Environment.Exit(1);
    }

    private static string GetDatabaseName()
    {
        Console.WriteLine();
        Console.WriteLine("Enter the name of your Database file. (Enter to accept default)");
        Console.Write("Database name [Database.db]: ");
        var databaseName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(databaseName)) databaseName = "Database.db";
        return databaseName;
    }

    private static void PrintHeader()
    {
        Console.WriteLine();
        Console.WriteLine("=====================================================");
        Console.WriteLine(" IW4MAdmin Database Migration Utility");
        Console.WriteLine(" by Ayymoss#8334 ");
        Console.WriteLine($" Version {Assembly.GetExecutingAssembly().GetName().Version}");
        Console.WriteLine("=====================================================");
        Console.WriteLine();
    }

    private static void ValidateDatabaseDirectory(string directory, string databaseName)
    {
        if (File.Exists(Path.Join(directory, databaseName))) return;
        Console.WriteLine($"{databaseName} doesn't exist in DatabaseSource directory.");
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
        Environment.Exit(1);
    }

    private static void BackupDatabaseIfExists(string directory, string databaseName)
    {
        Console.WriteLine($"Backing up {databaseName}...");
        try
        {
            File.Copy(Path.Join(directory, databaseName), Path.Join(directory, $"{databaseName}-{DateTimeOffset.UtcNow.ToFileTime()}.db"),
                true);
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception: {0}", e);
        }
    }

    private static async void SetConnectionString(string executingDirectory)
    {
        if (File.Exists(Path.Join(executingDirectory, "_ConnectionString.txt"))) return;
        await File.WriteAllTextAsync(Path.Join(executingDirectory, "_ConnectionString.txt"), DefaultConnectionString);
        Console.WriteLine("_ConnectionString.txt file doesn't exist. _ConnectionString.txt has been created.");
    }

    private static async Task<string> GetConnectionString(string executingDirectory)
    {
        var connectionString = await File.ReadAllTextAsync(Path.Join(executingDirectory, "_ConnectionString.txt"));
        if (connectionString != DefaultConnectionString) return connectionString;

        Console.WriteLine("_ConnectionString.txt is unmodified. Please update _ConnectionString.txt to reflect your target!");
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
        Environment.Exit(1);

        return connectionString;
    }

    private static async Task MigrateToPostgreSql(string connectionString, DatabaseContext sourceContext)
    {
        var postgresTargetOptions = new DbContextOptionsBuilder<PostgresqlDatabaseContext>()
            .UseNpgsql(connectionString)
            .EnableDetailedErrors()
            .EnableSensitiveDataLogging()
            .Options;

        await Migration.MigrateDataAsync(sourceContext, () => new PostgresqlDatabaseContext(postgresTargetOptions));
    }

    private static async Task MigrateToMySql(string connectionString, DatabaseContext sourceContext)
    {
        var mySqlTargetOptions = new DbContextOptionsBuilder<MySqlDatabaseContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
            .EnableDetailedErrors()
            .EnableSensitiveDataLogging()
            .Options;

        await Migration.MigrateDataAsync(sourceContext, () => new MySqlDatabaseContext(mySqlTargetOptions));
    }
}
