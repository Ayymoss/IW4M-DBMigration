using System.Collections;
using IWDataMigration.Abstractions;
using IWDataMigration.Models;
using IWDataMigration.Providers;
using IWDataMigration.Services;
using IWDataMigration.UI;
using Microsoft.Extensions.Options;

namespace IWDataMigration;

/// <summary>
/// Orchestrates the database migration process.
/// </summary>
public sealed class MigrationOrchestrator(
    IConsoleService console,
    IConfigurationService configuration,
    IDataTransformer dataTransformer,
    TableDependencyResolver dependencyResolver,
    IOptions<MigrationOptions> options)
{
    private readonly MigrationOptions _options = options.Value;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            console.DisplayHeader();
            await configuration.SetupStructureAsync();

            if (!console.DisplayInstructions())
            {
                return;
            }

            await configuration.InitializeAsync();

            if (!configuration.Validate())
            {
                console.DisplayError("Configuration is invalid.");
                console.WaitForKey("Press any key to exit.");
                return;
            }

            console.DisplayRule("Migration Started", "red");
            console.DisplayMessage("Please wait until the program completes before closing... This will take time!");

            await using var sourceProvider = CreateSourceProvider();
            await using var targetProvider = CreateTargetProvider();

            // Apply migrations with progress
            await using var migrationProgress = new ProgressReporter();
            var migrationTask = migrationProgress.StartAsync(cancellationToken);

            migrationProgress.ReportTableStart("Applying Source Migrations", 0, true);
            await sourceProvider.ApplyMigrationsAsync(cancellationToken);
            migrationProgress.ReportTableComplete("Applying Source Migrations");

            migrationProgress.ReportTableStart("Applying Target Migrations", 0, true);
            await targetProvider.ApplyMigrationsAsync(cancellationToken);
            migrationProgress.ReportTableComplete("Applying Target Migrations");

            migrationProgress.Complete();
            await migrationTask;

            // Get migration order
            var tableOrder = sourceProvider.GetMigrationOrder();
            console.DisplayMessage($"Migrating [cyan]{tableOrder.Count}[/] tables...");

            // Calculate total rows
            var totalRows = await CalculateTotalRowsAsync(sourceProvider, tableOrder, cancellationToken);
            console.DisplayMessage($"Total rows to migrate: [cyan]{totalRows:N0}[/]");

            // Migrate tables
            await MigrateTablesAsync(sourceProvider, targetProvider, tableOrder, cancellationToken);

            // Finalize
            console.DisplayRule("Finalization", "green");
            if (targetProvider.Type == DatabaseType.PostgreSql)
            {
                console.DisplayMessage("Setting PostgreSQL sequences...");
                await targetProvider.UpdateSequencesAsync(cancellationToken);
            }

            console.DisplayFinalMessages();
        }
        catch (Exception ex)
        {
            console.DisplayException(ex);
        }
        finally
        {
            console.WaitForKey("Press any key to exit.");
        }
    }

    private ISourceDatabaseProvider CreateSourceProvider()
    {
        return configuration.SourceType switch
        {
            DatabaseType.Sqlite => new SqliteSourceProvider(configuration.SourceConnection, dependencyResolver),
            DatabaseType.MySql => new MySqlSourceProvider(configuration.SourceConnection, dependencyResolver),
            _ => throw new NotSupportedException($"Source type {configuration.SourceType} is not supported.")
        };
    }

    private ITargetDatabaseProvider CreateTargetProvider()
    {
        return configuration.TargetType switch
        {
            DatabaseType.PostgreSql => new PostgresTargetProvider(configuration.TargetConnection),
            DatabaseType.MySql => new MySqlTargetProvider(configuration.TargetConnection),
            _ => throw new NotSupportedException($"Target type {configuration.TargetType} is not supported.")
        };
    }

    private async Task<long> CalculateTotalRowsAsync(
        ISourceDatabaseProvider source,
        IReadOnlyList<Type> tableOrder,
        CancellationToken cancellationToken)
    {
        long total = 0;
        var method = typeof(ISourceDatabaseProvider).GetMethod(nameof(ISourceDatabaseProvider.GetCountAsync))!;

        foreach (var tableType in tableOrder)
        {
            var genericMethod = method.MakeGenericMethod(tableType);
            var task = (Task<int>)genericMethod.Invoke(source, [cancellationToken])!;
            total += await task;
        }

        return total;
    }

    private async Task MigrateTablesAsync(
        ISourceDatabaseProvider source,
        ITargetDatabaseProvider target,
        IReadOnlyList<Type> tableOrder,
        CancellationToken cancellationToken)
    {
        await using var progress = new ProgressReporter();

        // Start all table tasks upfront
        foreach (var tableType in tableOrder)
        {
            progress.ReportTableStart(tableType.Name, 0, true);
        }

        var progressTask = progress.StartAsync(cancellationToken);

        foreach (var tableType in tableOrder)
        {
            await MigrateTableAsync(source, target, tableType, progress, cancellationToken);
        }

        progress.Complete();
        await progressTask;
    }

    private async Task MigrateTableAsync(
        ISourceDatabaseProvider source,
        ITargetDatabaseProvider target,
        Type tableType,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var tableName = tableType.Name;

        // Use reflection to call the generic migration method
        var migrateMethod = GetType()
            .GetMethod(nameof(MigrateTableGenericAsync),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(tableType);

        var task = (Task)migrateMethod.Invoke(this, [source, target, progress, cancellationToken])!;
        await task;
    }

    private async Task MigrateTableGenericAsync<T>(
        ISourceDatabaseProvider source,
        ITargetDatabaseProvider target,
        IProgressReporter progress,
        CancellationToken cancellationToken) where T : class
    {
        var tableName = typeof(T).Name;

        var totalCount = await source.GetCountAsync<T>(cancellationToken);

        if (totalCount == 0)
        {
            progress.ReportTableComplete(tableName);
            return;
        }

        // Update with actual count
        progress.ReportTableStart(tableName, totalCount, false);

        var processedCount = 0;

        await foreach (var batch in source.ReadBatchesAsync<T>(_options.BatchSize, cancellationToken))
        {
            // Transform if needed
            var transformed = dataTransformer.Transform(batch, target.Type);

            // Write batch
            await target.WriteBatchAsync(transformed, cancellationToken);

            // Update progress
            processedCount += batch.Count;
            progress.ReportProgress(tableName, processedCount);
        }

        progress.ReportTableComplete(tableName);
    }
}
