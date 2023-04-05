using Data.Context;
using Data.Models;
using Data.Models.Client;
using Data.Models.Client.Stats;
using Data.Models.Client.Stats.Reference;
using Data.Models.Misc;
using Data.Models.Server;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MySqlConnector;
using Npgsql;

namespace IW4MAdminDatabaseMigration;

internal static class Migration
{
    private const int BatchSize = 25_000;
    private const int AverageSampleSize = 25;

    public static async Task MigrateDataAsync(DatabaseContext sourceContext, Func<DatabaseContext> targetContextFunc, DatabaseType databaseType)
    {
        sourceContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        Console.WriteLine();
        Console.WriteLine("Please wait until the program exits before closing... This will take time!");
        Console.WriteLine();

        await ApplyMigrations(sourceContext, targetContextFunc);

        var tableDependencyOrder = GetTableDependencyOrder(targetContextFunc());

        Console.WriteLine($"Migrating {tableDependencyOrder.Count} tables... Please wait...");
        await MigrateTables(sourceContext, targetContextFunc, tableDependencyOrder, databaseType);

        Console.WriteLine();
        Console.WriteLine("=====================================================");
        Console.WriteLine(" All tables migrated successfully.");
        Console.WriteLine(" Change IW4MAdminConfigurationSettings.json to reflect the new database.");
        Console.WriteLine(" If you need further help, please ask in Discord.");
        Console.WriteLine(" IW4MAdmin Support: https://discord.gg/kGKusEzUJp");
        Console.WriteLine("=====================================================");
        Console.WriteLine();
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
    }

    private static async Task ApplyMigrations(DbContext sourceContext, Func<DatabaseContext> targetContextFunc)
    {
        Console.WriteLine("Applying migrations to SOURCE database...");
        await sourceContext.Database.MigrateAsync();

        Console.WriteLine("Applying migrations to TARGET database...");
        await using var targetContext = targetContextFunc();
        await targetContext.Database.MigrateAsync();
    }

    private static async Task MigrateTables(IAsyncDisposable sourceContext, Func<DatabaseContext> targetContextFunc,
        IReadOnlyList<Type> tableDependencyOrder, DatabaseType databaseType)
    {
        var batchTimes = new Queue<double>(10);

        for (var i = 0; i < tableDependencyOrder.Count; i++)
        {
            var tableType = tableDependencyOrder[i];
            var data = sourceContext.GetType().GetMethod("Set", Array.Empty<Type>())?
                .MakeGenericMethod(tableType).Invoke(sourceContext, null) as IQueryable<object>;
            if (data is null) continue;
            var totalCount = await data.CountAsync();

            if (totalCount is 0)
            {
                Console.WriteLine($"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}] [Table {tableType.Name} " +
                                  $"({i + 1}/{tableDependencyOrder.Count})]: Skipped - No data to migrate.");
                continue;
            }

            int count, processedCount = 0;
            for (count = 0; count < totalCount; count += BatchSize)
            {
                var startTime = DateTimeOffset.UtcNow;

                var batch = await data.AsNoTracking()
                    .Skip(count)
                    .Take(BatchSize)
                    .ToListAsync();

                if (databaseType is DatabaseType.MySql)
                {
                    foreach (var item in batch)
                    {
                        var properties = item.GetType().GetProperties()
                            .Where(p => p.PropertyType == typeof(double) || p.PropertyType == typeof(double?));
                        foreach (var prop in properties)
                        {
                            var value = prop.GetValue(item);
                            if (value is not double dValue) continue;

                            if (double.IsPositiveInfinity(dValue))
                            {
                                prop.SetValue(item, double.MaxValue);
                            }
                            else if (double.IsNegativeInfinity(dValue))
                            {
                                prop.SetValue(item, double.MinValue);
                            }
                        }
                    }
                }

                processedCount += batch.Count;
                await using var targetContext = targetContextFunc();
                targetContext.ChangeTracker.AutoDetectChangesEnabled = false;
                targetContext.AddRange(batch);
                try
                {
                    await targetContext.SaveChangesAsync();
                }
                catch (DbUpdateException e) when (e.InnerException is
                                                      PostgresException {SqlState: "23505"}
                                                      or MySqlException {ErrorCode: MySqlErrorCode.DuplicateKeyEntry})
                {
                    Console.WriteLine("ERROR: Data already exists in target database. Please target another database...");
                    Console.WriteLine("Press any key to exit.");
                    Console.ReadKey();
                    Environment.Exit(1);
                }

                var endTime = DateTimeOffset.UtcNow;
                var timeTaken = (endTime - startTime).TotalSeconds;
                if (batchTimes.Count >= AverageSampleSize)
                {
                    batchTimes.Dequeue();
                }

                batchTimes.Enqueue(timeTaken);

                var averageBatchTime = batchTimes.Count > 0 ? batchTimes.Average() : 0;
                var remainingBatches = (totalCount - processedCount) / BatchSize;
                var estimatedRemainingTime = TimeSpan.FromSeconds(remainingBatches * averageBatchTime);
                var averageText = batchTimes.Count >= AverageSampleSize
                    ? estimatedRemainingTime < TimeSpan.FromSeconds(10)
                        ? "Soon..."
                        : estimatedRemainingTime.Humanize()
                    : $"Calculating... ({batchTimes.Count}/{AverageSampleSize})";

                Console.WriteLine(
                    $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}] [Table {tableType.Name} " +
                    $"({i + 1}/{tableDependencyOrder.Count}): Rows ({processedCount:N0}/{totalCount:N0})] " +
                    $"- ETA: {averageText}");
            }
        }
    }

    private static List<Type> GetTableDependencyOrder(DbContext context)
    {
        var allTableTypes = new List<Type>
        {
            typeof(EFClient),
            typeof(EFAlias),
            typeof(EFAliasLink),
            typeof(EFPenalty),
            typeof(EFPenaltyIdentifier),
            typeof(EFMeta),
            typeof(EFChangeHistory),
            typeof(Vector3),
            typeof(EFACSnapshotVector3),
            typeof(EFACSnapshot),
            typeof(EFServer),
            typeof(EFClientKill),
            typeof(EFClientMessage),
            typeof(EFServerStatistics),
            typeof(EFClientStatistics),
            typeof(EFHitLocation),
            typeof(EFClientHitStatistic),
            typeof(EFWeapon),
            typeof(EFWeaponAttachment),
            typeof(EFMap),
            typeof(EFInboxMessage),
            typeof(EFServerSnapshot),
            typeof(EFClientConnectionHistory),
        };

        var orderedTableTypes = new List<Type>();
        var entityTypes = context.Model.GetEntityTypes().ToArray();

        foreach (var tableType in allTableTypes)
        {
            AddTableWithDependencies(orderedTableTypes, entityTypes, tableType, new HashSet<Type>());
        }

        return orderedTableTypes;
    }

    private static void AddTableWithDependencies(ICollection<Type> orderedTableTypes, IEntityType[] entityTypes, Type tableType,
        ISet<Type> visitedTableTypes)
    {
        if (orderedTableTypes.Contains(tableType) || visitedTableTypes.Contains(tableType)) return;

        visitedTableTypes.Add(tableType);

        var table = entityTypes.FirstOrDefault(t => t.ClrType == tableType);
        if (table == null) return;

        var foreignKeys = table.GetForeignKeys();

        foreach (var foreignKey in foreignKeys)
        {
            var principalType = foreignKey.PrincipalEntityType.ClrType;
            AddTableWithDependencies(orderedTableTypes, entityTypes, principalType, new HashSet<Type>(visitedTableTypes));
        }

        orderedTableTypes.Add(tableType);
    }
}

public enum DatabaseType
{
    Postgres,
    MySql
}
