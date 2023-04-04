using System.Reflection;
using Data.Context;
using Data.MigrationContext;
using Microsoft.EntityFrameworkCore;

namespace IW4MAdminDatabaseMigration;

public static class IW4MAdminDatabaseMigration
{
    public static async Task Main()
    {
        PrintHeader();

        var executingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var directory = Path.Join(executingDirectory, "DatabaseSource");

        ValidateDatabaseDirectory(directory);
        BackupDatabaseIfExists(directory);

        var connectionString = await GetConnectionString(executingDirectory!);

        var sourceOptions = new DbContextOptionsBuilder<SqliteDatabaseContext>()
            .UseSqlite("Data Source=DatabaseSource\\Database.db")
            .Options;

        var sourceContext = new SqliteDatabaseContext(sourceOptions);
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        Console.WriteLine();
        Console.WriteLine("Migration to:");
        Console.WriteLine("1) PostgreSQL");
        Console.WriteLine("2) MySQL/MariaDB");
        Console.Write("Select: [1 or 2] ");
        var input = Console.ReadLine();
        Console.WriteLine();

        switch (input)
        {
            case "1":
                await MigrateToPostgreSql(connectionString, sourceContext);
                break;
            case "2":
                await MigrateToMySql(connectionString, sourceContext);
                break;
            default:
                Console.WriteLine("Invalid input. Exiting...");
                Console.ReadKey();
                Environment.Exit(1);
                break;
        }
    }

    private static void PrintHeader()
    {
        Console.WriteLine();
        Console.WriteLine("=====================================================");
        Console.WriteLine(" IW4MAdmin Database Migration");
        Console.WriteLine(" by Ayymoss#8334 ");
        Console.WriteLine($" Version {Assembly.GetExecutingAssembly().GetName().Version}");
        Console.WriteLine("=====================================================");
        Console.WriteLine();
    }

    private static void ValidateDatabaseDirectory(string directory)
    {
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        if (File.Exists(Path.Join(directory, "Database.db"))) return;
        Console.WriteLine("Database.db doesn't exist in DatabaseSource directory.");
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
        Environment.Exit(1);
    }

    private static void BackupDatabaseIfExists(string directory)
    {
        Console.WriteLine("Backing up Database.db...");
        try
        {
            File.Copy(Path.Join(directory, "Database.db"), Path.Join(directory, $"Database-{DateTimeOffset.UtcNow.ToFileTime()}.db"), true);
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception: {0}", e);
        }
    }

    private static async Task<string> GetConnectionString(string executingDirectory)
    {
        const string defaultConnectionString = "Host=HOSTNAME;Port=PORT;Username=USERNAME;Password=PASSWORD;Database=DATABASE";
        if (!File.Exists(Path.Join(executingDirectory, "_ConnectionString.txt")))
        {
            await File.WriteAllTextAsync(Path.Join(executingDirectory, "_ConnectionString.txt"), defaultConnectionString);
            Console.WriteLine("Connection string file doesn't exist. _ConnectionString.txt has been created.\nPress any key to exit.");
            Console.ReadKey();
            Environment.Exit(1);
        }

        var connectionString = await File.ReadAllTextAsync(Path.Join(executingDirectory, "_ConnectionString.txt"));
        if (connectionString != defaultConnectionString) return connectionString;

        Console.WriteLine("Connection string file unmodified. Please update _ConnectionString.txt to reflect your target!");
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
        Environment.Exit(1);

        return connectionString;
    }

    private static async Task MigrateToPostgreSql(string connectionString, DatabaseContext sourceContext)
    {
        var postgresTargetOptions = new DbContextOptionsBuilder<PostgresqlDatabaseContext>()
            .UseNpgsql(connectionString)
            .Options;

        await Migration.MigrateDataAsync(sourceContext, () => new PostgresqlDatabaseContext(postgresTargetOptions));
    }

    private static async Task MigrateToMySql(string connectionString, DatabaseContext sourceContext)
    {
        var mySqlTargetOptions = new DbContextOptionsBuilder<MySqlDatabaseContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
            .Options;

        await Migration.MigrateDataAsync(sourceContext, () => new MySqlDatabaseContext(mySqlTargetOptions));
    }
}
