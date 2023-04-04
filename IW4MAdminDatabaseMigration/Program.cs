using System.Reflection;
using Data.MigrationContext;
using Microsoft.EntityFrameworkCore;

namespace IW4MAdminDatabaseMigration;

public class IW4MAdminDatabaseMigration
{
    public static async Task Main()
    {
        var executingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var directory = Path.Join(executingDirectory, "DatabaseSource");
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        if (!File.Exists(Path.Join(directory, "Database.db")))
        {
            Console.WriteLine("Database.db doesn't exist in DatabaseSource directory.");
            Console.ReadKey();
            Environment.Exit(1);
        }

        if (!File.Exists(Path.Join(executingDirectory, "_ConnectionString.txt")))
        {
            const string defaultConnectionString = "Host=HOSTNAME;Port=PORT;Username=USERNAME;Password=PASSWORD;Database=DATABASE";
            await File.WriteAllTextAsync(Path.Join(executingDirectory, "_ConnectionString.txt"), defaultConnectionString);
            Console.WriteLine("Connection string file doesn't exist. _ConnectionString.txt has been created.");
            Console.ReadKey();
            Environment.Exit(1);
        }

        var connectionString = await File.ReadAllTextAsync(Path.Join(executingDirectory, "_ConnectionString.txt"));

        var sourceOptions = new DbContextOptionsBuilder<SqliteDatabaseContext>()
            .UseSqlite("Data Source=DatabaseSource\\Database.db")
            .Options;

        var targetOptions = new DbContextOptionsBuilder<PostgresqlDatabaseContext>()
            .UseNpgsql(connectionString)
            .Options;

        var sourceContext = new SqliteDatabaseContext(sourceOptions);
        var targetContext = new PostgresqlDatabaseContext(targetOptions);
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        await Migration.MigrateDataAsync(sourceContext, targetContext);
    }
}
