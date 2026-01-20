using System.Reflection;
using IWDataMigration.Abstractions;
using IWDataMigration.Models;
using IWDataMigration.Providers;
using IWDataMigration.Services;
using IWDataMigration.UI;
using Microsoft.Extensions.Options;

namespace IWDataMigration;

/// <summary>
/// Orchestrates the database migration process with checkpoint and watchdog support.
/// </summary>
public sealed class MigrationOrchestrator(
    IConsoleService console,
    IConfigurationService configuration,
    IDataTransformer dataTransformer,
    TableDependencyResolver dependencyResolver,
    IMigrationStateService stateService,
    IWatchdogService watchdog,
    IOptions<MigrationOptions> options)
{
    private readonly MigrationOptions _options = options.Value;
    private bool _isResuming;
    private MigrationState? _resumeState;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            console.DisplayHeader();
            await configuration.SetupStructureAsync();

            // Check for existing migration state FIRST
            _resumeState = await stateService.LoadExistingStateAsync(cancellationToken);
            if (_resumeState is not null)
            {
                _isResuming = console.PromptResume(
                    _resumeState.CurrentTable,
                    _resumeState.CurrentTableOffset,
                    _resumeState.TotalRowsMigrated,
                    _resumeState.LastUpdatedAt);

                if (!_isResuming)
                {
                    // User chose fresh start
                    await stateService.ClearAsync(cancellationToken);
                    _resumeState = null;
                }
            }

            // Skip setup prompts if resuming - we already have the configuration from the saved state
            if (_isResuming && _resumeState is not null)
            {
                console.DisplayMessage("[yellow]Using saved configuration from previous session...[/]");
                
                // Load connection info from saved state without prompting
                await configuration.InitializeFromResumeAsync(
                    Enum.Parse<DatabaseType>(_resumeState.SourceType),
                    Enum.Parse<DatabaseType>(_resumeState.TargetType));
            }
            else
            {
                // Normal flow - show instructions and prompt for configuration
                if (!console.DisplayInstructions())
                {
                    return;
                }

                await configuration.InitializeAsync();
            }

            if (!configuration.Validate())
            {
                console.DisplayError("Configuration is invalid.");
                console.WaitForKey("Press any key to exit.");
                return;
            }

            console.DisplayRule("Migration Started", "red");
            console.DisplayMessage("Please wait until the program completes before closing... This will take time!");
            if (_isResuming)
            {
                console.DisplayMessage("[yellow]Resuming from checkpoint (THIS WILL TAKE TIME TO RESUME!)...[/]");
                console.DisplayMessage($"[dim]Skipping {_resumeState!.CompletedTables.Count} completed tables[/]");
                console.DisplayMessage($"[dim]Resuming {_resumeState.CurrentTable} from row {_resumeState.CurrentTableOffset:N0}[/]");
            }

            await using var sourceProvider = CreateSourceProvider();
            await using var targetProvider = CreateTargetProvider();

            // Apply migrations with progress (skip if resuming - already done)
            if (!_isResuming)
            {
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
            }

            // Create new session if not resuming
            if (!_isResuming)
            {
                await stateService.CreateNewSessionAsync(
                    configuration.SourceType.ToString(),
                    configuration.TargetType.ToString(),
                    cancellationToken);
            }

            // Get migration order
            var tableOrder = sourceProvider.GetMigrationOrder();
            
            // Show what we're actually going to migrate
            var completedCount = _resumeState?.CompletedTables.Count ?? 0;
            var remainingCount = tableOrder.Count - completedCount;
            console.DisplayMessage($"Tables: [cyan]{remainingCount}[/] remaining of [cyan]{tableOrder.Count}[/] total");

            // Calculate remaining rows only (not already completed)
            var totalRows = await CalculateRemainingRowsAsync(sourceProvider, tableOrder, cancellationToken);
            console.DisplayMessage($"Rows to migrate: [cyan]{totalRows:N0}[/]");

            // Start watchdog
            watchdog.Start("Starting migration");

            // Migrate tables
            await MigrateTablesAsync(sourceProvider, targetProvider, tableOrder, cancellationToken);

            // Stop watchdog
            watchdog.Stop();

            // Mark migration complete
            await stateService.CompleteAsync(cancellationToken);

            // Finalize
            console.DisplayRule("Finalization", "green");
            if (targetProvider.Type == DatabaseType.PostgreSql)
            {
                console.DisplayMessage("Setting PostgreSQL sequences...");
                await targetProvider.UpdateSequencesAsync(cancellationToken);
            }

            console.DisplayFinalMessages();
        }
        catch (OperationCanceledException)
        {
            console.DisplayMessage("[yellow]Migration cancelled. Progress has been saved.[/]");
            console.DisplayMessage("Restart the tool to resume from the last checkpoint.");
        }
        catch (Exception ex)
        {
            console.DisplayException(ex);
            console.DisplayMessage("[yellow]Progress has been saved. Restart to resume.[/]");
        }
        finally
        {
            watchdog.Stop();
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

    private async Task<long> CalculateRemainingRowsAsync(
        ISourceDatabaseProvider source,
        IReadOnlyList<Type> tableOrder,
        CancellationToken cancellationToken)
    {
        long total = 0;
        var method = typeof(ISourceDatabaseProvider).GetMethod(nameof(ISourceDatabaseProvider.GetCountAsync))!;
        var completedTables = _resumeState?.CompletedTables ?? [];

        foreach (var tableType in tableOrder)
        {
            // Skip completed tables
            if (completedTables.Contains(tableType.Name))
            {
                continue;
            }

            var genericMethod = method.MakeGenericMethod(tableType);
            var task = (Task<int>)genericMethod.Invoke(source, [cancellationToken])!;
            var count = await task;

            // Subtract already processed rows for current table
            if (_resumeState?.CurrentTable == tableType.Name)
            {
                count = Math.Max(0, count - _resumeState.CurrentTableOffset);
            }

            total += count;
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

        // Determine which tables to process
        var completedTables = _resumeState?.CompletedTables ?? [];
        
        // Only add non-completed tables to progress display
        var tablesToMigrate = tableOrder.Where(t => !completedTables.Contains(t.Name)).ToList();
        
        foreach (var tableType in tablesToMigrate)
        {
            progress.ReportTableStart(tableType.Name, 0, true);
        }

        var progressTask = progress.StartAsync(cancellationToken);

        foreach (var tableType in tablesToMigrate)
        {
            // Determine starting offset
            var startOffset = 0;
            var useIgnoreDuplicates = false;

            if (_isResuming && _resumeState?.CurrentTable == tableType.Name)
            {
                startOffset = _resumeState.CurrentTableOffset;
                useIgnoreDuplicates = true; // First batch might have partial duplicates
            }

            await MigrateTableAsync(source, target, tableType, progress, startOffset, useIgnoreDuplicates, cancellationToken);
        }

        progress.Complete();
        await progressTask;
    }

    private async Task MigrateTableAsync(
        ISourceDatabaseProvider source,
        ITargetDatabaseProvider target,
        Type tableType,
        IProgressReporter progress,
        int startOffset,
        bool useIgnoreDuplicates,
        CancellationToken cancellationToken)
    {
        // Use reflection to call the generic migration method
        var migrateMethod = GetType()
            .GetMethod(nameof(MigrateTableGenericAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(tableType);

        var task = (Task)migrateMethod.Invoke(this, [source, target, progress, startOffset, useIgnoreDuplicates, cancellationToken])!;
        await task;
    }

    private async Task MigrateTableGenericAsync<T>(
        ISourceDatabaseProvider source,
        ITargetDatabaseProvider target,
        IProgressReporter progress,
        int startOffset,
        bool useIgnoreDuplicates,
        CancellationToken cancellationToken) where T : class
    {
        var tableName = typeof(T).Name;
        var settings = _options.UserSettings;

        var totalCount = await source.GetCountAsync<T>(cancellationToken);

        if (totalCount == 0 || startOffset >= totalCount)
        {
            progress.ReportTableComplete(tableName);
            await stateService.MarkTableCompleteAsync(tableName, cancellationToken);
            return;
        }

        // Calculate remaining rows for this table
        var remainingRows = totalCount - startOffset;
        
        // Update progress with remaining count (not total) for accurate ETA
        progress.ReportTableStart(tableName, remainingRows, false);

        var processedCount = 0; // Track processed in this session (not total offset)
        var isFirstBatch = true;
        var retryCount = 0;

        await foreach (var batch in source.ReadBatchesFromOffsetAsync<T>(_options.BatchSize, startOffset, cancellationToken))
        {
            // Heartbeat watchdog
            watchdog.Heartbeat($"Table: {tableName}, Processed: {startOffset + processedCount:N0}/{totalCount:N0}");

            try
            {
                // Transform if needed
                var transformed = dataTransformer.Transform(batch, target.Type);

                // Write batch - use ignore duplicates for first batch when resuming
                if (useIgnoreDuplicates && isFirstBatch)
                {
                    await target.WriteBatchIgnoreDuplicatesAsync(transformed, cancellationToken);
                    isFirstBatch = false;
                }
                else
                {
                    await target.WriteBatchAsync(transformed, cancellationToken);
                }

                // Update progress (using session count for accurate display)
                processedCount += batch.Count;
                progress.ReportProgress(tableName, processedCount);

                // Checkpoint with actual offset (startOffset + session processed)
                await stateService.UpdateProgressAsync(tableName, startOffset + processedCount, cancellationToken);

                // Reset retry count on success
                retryCount = 0;
            }
            catch (Exception ex) when (retryCount < settings.MaxBatchRetries && !cancellationToken.IsCancellationRequested)
            {
                retryCount++;
                progress.ReportError($"Batch failed ({retryCount}/{settings.MaxBatchRetries}): {ex.Message}");

                // Exponential backoff
                var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                await Task.Delay(delay, cancellationToken);

                // Re-throw to exit and let user retry
                if (retryCount >= settings.MaxBatchRetries)
                {
                    throw;
                }
            }
        }

        progress.ReportTableComplete(tableName);
        await stateService.MarkTableCompleteAsync(tableName, cancellationToken);
    }
}
