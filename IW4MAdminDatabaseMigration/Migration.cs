using Data.MigrationContext;
using Data.Models;
using Data.Models.Client;
using Data.Models.Client.Stats;
using Data.Models.Client.Stats.Reference;
using Data.Models.Misc;
using Data.Models.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IW4MAdminDatabaseMigration;

internal class Migration
{
    private const int BatchSize = 1_000;

    public static async Task MigrateDataAsync(SqliteDatabaseContext sourceContext, PostgresqlDatabaseContext targetContext)
    {
        Console.WriteLine("\n\nPlease wait until the program exists before closing... This will take time!\n\n");
        Console.WriteLine("Applying IW4MAdmin database migrations...");
        await targetContext.Database.MigrateAsync();

        var tableDependencyOrder = GetTableDependencyOrder(targetContext);
        Console.WriteLine($"Migrating {tableDependencyOrder.Count} tables... Please wait...");

        var index = 0;
        foreach (var dbSetProperty in tableDependencyOrder.Select(tableType => sourceContext.GetType().GetProperties()
                         .FirstOrDefault(p => p.PropertyType.IsGenericType &&
                                              p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>) &&
                                              p.PropertyType.GenericTypeArguments[0] == tableType))
                     .Where(dbSetProperty => dbSetProperty != null))
        {
            if (dbSetProperty?.GetValue(sourceContext) is not IQueryable<object> data) continue;
            index++;
            var count = 0;
            var totalCount = await data.CountAsync();

            while (true)
            {
                var batch = await data.AsNoTracking().Skip(count).Take(BatchSize).ToListAsync();
                if (!batch.Any()) break;

                await targetContext.AddRangeAsync(batch);
                await targetContext.SaveChangesAsync();

                count += batch.Count;
                Console.WriteLine(
                    $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}] {dbSetProperty.Name} ({index}/{tableDependencyOrder.Count}): " +
                    $"Completed ({count:N0}/{totalCount:N0})");
            }
        }

        Console.WriteLine("All tables migrated.");
        Console.ReadKey();
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
        var tables = context.Model.GetEntityTypes();
        var entityTypes = tables as IEntityType[] ?? tables.ToArray();
        foreach (var tableType in allTableTypes)
        {
            AddTableWithDependencies(orderedTableTypes, entityTypes, tableType, new HashSet<Type>());
        }

        return orderedTableTypes;
    }

    private static void AddTableWithDependencies(ICollection<Type> orderedTableTypes, IEnumerable<IEntityType> tables, Type tableType,
        ISet<Type> visitedTableTypes)
    {
        if (orderedTableTypes.Contains(tableType) || visitedTableTypes.Contains(tableType)) return;

        visitedTableTypes.Add(tableType);

        var entityTypes = tables as IEntityType[] ?? tables.ToArray();
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
