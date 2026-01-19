using Data.Context;
using Data.Models;
using Data.Models.Client;
using Data.Models.Client.Stats;
using Data.Models.Client.Stats.Reference;
using Data.Models.Misc;
using Data.Models.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IWDataMigration.Services;

/// <summary>
/// Resolves the order in which tables should be migrated based on foreign key dependencies.
/// </summary>
public sealed class TableDependencyResolver
{
    /// <summary>
    /// All entity types that should be migrated.
    /// </summary>
    private static readonly Type[] AllTableTypes =
    [
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
        typeof(EFClientConnectionHistory)
    ];

    /// <summary>
    /// Resolves the migration order based on foreign key dependencies.
    /// Tables with dependencies are ordered after their dependencies.
    /// </summary>
    public IReadOnlyList<Type> Resolve(DatabaseContext context)
    {
        var orderedTypes = new List<Type>();
        var entityTypes = context.Model.GetEntityTypes().ToArray();

        foreach (var tableType in AllTableTypes)
        {
            AddTableWithDependencies(orderedTypes, entityTypes, tableType, []);
        }

        return orderedTypes;
    }

    private static void AddTableWithDependencies(
        ICollection<Type> orderedTypes,
        IEntityType[] entityTypes,
        Type tableType,
        HashSet<Type> visitedTypes)
    {
        // Skip if already processed or in current path (circular reference)
        if (orderedTypes.Contains(tableType) || !visitedTypes.Add(tableType))
        {
            return;
        }

        var entityType = entityTypes.FirstOrDefault(t => t.ClrType == tableType);
        if (entityType is null)
        {
            return;
        }

        // Process dependencies first
        foreach (var foreignKey in entityType.GetForeignKeys())
        {
            var principalType = foreignKey.PrincipalEntityType.ClrType;
            AddTableWithDependencies(orderedTypes, entityTypes, principalType, new HashSet<Type>(visitedTypes));
        }

        orderedTypes.Add(tableType);
    }
}
