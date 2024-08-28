using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Data.Context;
using Data.MigrationContext;
using Data.Models;
using Data.Models.Client;
using Data.Models.Client.Stats;
using Data.Models.Client.Stats.Reference;
using Data.Models.Misc;
using Data.Models.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MySqlConnector;
using Npgsql;
using Spectre.Console;

namespace IWDataMigration;

public class MigrationHelper(DatabaseContext sourceContext, Func<DatabaseContext> targetContextFunc)
{
    private ProgressTracker _progressTracker = new();

    private const int BatchSize = 25_000;

    public async Task MigrateDataAsync()
    {
        await using var dbContextInstance = targetContextFunc();

        AnsiConsole.MarkupLine("Please wait until the program completes before closing... This will take time!");
        await ApplyMigrations();

        await Task.Delay(500); // Give the progress display time to catch up
        await _progressTracker.CancellationTokenSource.CancelAsync();
        await Task.Delay(500); // Let's wait for the token to go through before we try to create a new tracker.
        _progressTracker = new ProgressTracker();

        var tableDependencyOrder = GetTableDependencyOrder();
        AnsiConsole.MarkupLine($"Migrating {tableDependencyOrder.Count} tables... Please wait...");

        await MigrateTables(tableDependencyOrder);
        await Task.Delay(500); // Give the progress display time to catch up
        await _progressTracker.CancellationTokenSource.CancelAsync();

        var finalRule = new Rule("[green]Finalization[/]") {Justification = Justify.Left};
        AnsiConsole.Write(finalRule);
        if (dbContextInstance is PostgresqlDatabaseContext) await UpdatePostgreSqlIndexing(dbContextInstance);
    }

    private async Task ApplyMigrations()
    {
        const string migrateSource = "Applying Migrations to Source DB";
        const string migrateTarget = "Applying Migrations to Target DB";

        _ = Task.Run(() => _progressTracker.SetProgressDisplay());
        await Task.Delay(500);

        _progressTracker.AddTask(migrateSource, true);
        _progressTracker.AddTask(migrateTarget, true);

        await sourceContext.Database.MigrateAsync();
        _progressTracker.StopTask(migrateSource);

        await using var targetContext = targetContextFunc();
        await targetContext.Database.MigrateAsync();
        _progressTracker.StopTask(migrateTarget);
    }

    private async Task MigrateTables(IReadOnlyList<Type> tableDependencyOrder)
    {
        await using var dbContextInstance = targetContextFunc();
        var dbContextType = dbContextInstance.GetType();

        var totalTableRows = await GetTotalTableRows(tableDependencyOrder);
        AnsiConsole.MarkupLine($"Total rows to migrate: {totalTableRows:N0}");

        _ = Task.Run(() => _progressTracker.SetProgressDisplay());
        await Task.Delay(500);
        foreach (var type in tableDependencyOrder)
        {
            _progressTracker.AddTask(type.Name);
        }

        for (var tableIndex = 0; tableIndex < tableDependencyOrder.Count; tableIndex++)
        {
            var tableType = tableDependencyOrder[tableIndex];
            var data = sourceContext.GetType()
                .GetMethod("Set", Array.Empty<Type>())?
                .MakeGenericMethod(tableType)
                .Invoke(sourceContext, null) as IQueryable<object>;
            if (data is null) continue;
            var totalCount = await data.CountAsync();

            if (totalCount is 0)
            {
                _progressTracker.StopTask(tableType.Name);
                continue;
            }

            await MigrateTableData(dbContextType, tableDependencyOrder, tableIndex, totalCount);
        }
    }

    private async Task<int> GetTotalTableRows(IEnumerable<Type> tableDependencyOrder)
    {
        var totalRowCount = 0;
        foreach (var table in tableDependencyOrder)
        {
            if (sourceContext.GetType()
                    .GetMethod("Set", Array.Empty<Type>())?
                    .MakeGenericMethod(table)
                    .Invoke(sourceContext, null) is not IQueryable<object> data) continue;
            totalRowCount += await data.CountAsync();
        }

        return totalRowCount;
    }

    private async Task MigrateTableData(Type dbContextType, IReadOnlyList<Type> tableDependencyOrder, int tableIndex, int totalTableRows)
    {
        int count;
        var tableType = tableDependencyOrder[tableIndex];
        var data = sourceContext.GetType()
            .GetMethod("Set", Array.Empty<Type>())?
            .MakeGenericMethod(tableType)
            .Invoke(sourceContext, null) as IQueryable<object>;

        _progressTracker.UpdateProgress(tableType.Name, 0);

        for (count = 0; count < totalTableRows; count += BatchSize)
        {
            var batch = await data!.AsNoTracking()
                .Skip(count)
                .Take(BatchSize)
                .ToListAsync();

            if (dbContextType == typeof(MySqlDatabaseContext))
            {
                batch = HandleMySqlDoubleValues(batch);
            }

            await using var targetContext = targetContextFunc();
            targetContext.ChangeTracker.AutoDetectChangesEnabled = false;
            targetContext.AddRange(batch);
            try
            {
                await targetContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)when (e.InnerException is PostgresException {SqlState: "23505"}
                                                 or MySqlException {ErrorCode: MySqlErrorCode.DuplicateKeyEntry})
            {
                await _progressTracker.CancellationTokenSource.CancelAsync();
                AnsiConsole.MarkupLine("ERROR: Data already exists in target database. Please target another database...");
                AnsiConsole.MarkupLine("Press any key to exit.");
                Console.ReadKey();
                Environment.Exit(1);
            }

            var percentageComplete = count / (double)totalTableRows * 100;
            _progressTracker.UpdateProgress(tableType.Name, percentageComplete);
        }

        _progressTracker.StopTask(tableType.Name);
    }

    private static List<object> HandleMySqlDoubleValues(List<object> batch)
    {
        foreach (var item in batch)
        {
            var properties = item.GetType()
                .GetProperties()
                .Where(p => p.PropertyType == typeof(double) || p.PropertyType == typeof(double?))
                .Where(p => p.GetCustomAttribute<NotMappedAttribute>() is null);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(item);
                if (value is null) continue;
                var dValue = (double)value;
                if (double.IsPositiveInfinity(dValue)) prop.SetValue(item, double.MaxValue);
                if (double.IsNegativeInfinity(dValue)) prop.SetValue(item, double.MinValue);
            }
        }

        return batch;
    }

    private List<Type> GetTableDependencyOrder()
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
        var entityTypes = targetContextFunc().Model.GetEntityTypes().ToArray();

        foreach (var tableType in allTableTypes)
        {
            AddTableWithDependencies(orderedTableTypes, entityTypes, tableType, new HashSet<Type>());
        }

        return orderedTableTypes;
    }

    private static void AddTableWithDependencies(ICollection<Type> orderedTableTypes, IEnumerable<IEntityType> entityTypes, Type tableType,
        ISet<Type> visitedTableTypes)
    {
        if (orderedTableTypes.Contains(tableType) || !visitedTableTypes.Add(tableType)) return;

        var enumerable = entityTypes as IEntityType[] ?? entityTypes.ToArray();
        var table = enumerable.FirstOrDefault(t => t.ClrType == tableType);
        if (table == null) return;

        var foreignKeys = table.GetForeignKeys();

        foreach (var foreignKey in foreignKeys)
        {
            var principalType = foreignKey.PrincipalEntityType.ClrType;
            AddTableWithDependencies(orderedTableTypes, enumerable, principalType, new HashSet<Type>(visitedTableTypes));
        }

        orderedTableTypes.Add(tableType);
    }


    private static async Task UpdatePostgreSqlIndexing(DatabaseContext dbContextInstance)
    {
        Console.WriteLine();
        AnsiConsole.MarkupLine("Setting PostgreSQL's index...");
        var tableAndColumnNames = dbContextInstance.Model.GetEntityTypes()
            .Select(x => new
            {
                TableName = x.GetTableName(),
                PrimaryKeyColumnName = x.GetKeys()
                    .First()
                    .Properties
                    .Select(p => p.GetColumnName())
                    .FirstOrDefault()
            })
            .ToList();

        foreach (var tableAndColumn in tableAndColumnNames)
        {
            var sqlCommand = $"""
                              DO
                              $$
                              DECLARE
                                  max_id INTEGER;
                                  next_val INTEGER;
                              BEGIN
                                  SELECT COALESCE(MAX("{tableAndColumn.PrimaryKeyColumnName}"), 0) INTO max_id FROM "{tableAndColumn.TableName}";
                              next_val := max_id + 1;

                              PERFORM setval(pg_get_serial_sequence(quote_ident('{tableAndColumn.TableName}'), '{tableAndColumn.PrimaryKeyColumnName}'), next_val, false);
                              END
                                  $$;
                              """;

            await dbContextInstance.Database.ExecuteSqlRawAsync(sqlCommand);
        }
    }
}
