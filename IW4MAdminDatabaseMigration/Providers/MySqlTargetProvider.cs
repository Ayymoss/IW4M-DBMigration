using Data.Context;
using Data.MigrationContext;
using IWDataMigration.Abstractions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace IWDataMigration.Providers;

/// <summary>
/// Target database provider for MySQL/MariaDB databases.
/// </summary>
public sealed class MySqlTargetProvider : ITargetDatabaseProvider
{
    private readonly Func<MySqlDatabaseContext> _contextFactory;

    public DatabaseType Type => DatabaseType.MySql;

    public MySqlTargetProvider(string connectionString)
    {
        var serverVersion = ServerVersion.AutoDetect(connectionString);

        var options = new DbContextOptionsBuilder<MySqlDatabaseContext>()
            .UseMySql(connectionString, serverVersion)
            .EnableDetailedErrors()
            .EnableSensitiveDataLogging()
            .Options;

        _contextFactory = () => new MySqlDatabaseContext(options);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = _contextFactory();
            return await context.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory();
        await context.Database.MigrateAsync(cancellationToken);
    }

    public async Task WriteBatchAsync<T>(IEnumerable<T> batch, CancellationToken cancellationToken = default) where T : class
    {
        await using var context = _contextFactory();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        context.Set<T>().AddRange(batch);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            throw new InvalidOperationException(
                "Data already exists in target database. Please target an empty database.", e);
        }
    }

    public async Task WriteBatchIgnoreDuplicatesAsync<T>(IEnumerable<T> batch, CancellationToken cancellationToken = default) where T : class
    {
        var items = batch.ToList();
        if (items.Count == 0) return;

        await using var context = _contextFactory();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        // Insert one at a time to handle duplicates gracefully
        // This is slower but safe for resume scenarios
        foreach (var item in items)
        {
            try
            {
                context.Set<T>().Add(item);
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException e) when (e.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
            {
                // Duplicate key - silently skip (expected during resume)
                context.Entry(item).State = EntityState.Detached;
            }
        }
    }

    public Task UpdateSequencesAsync(CancellationToken cancellationToken = default)
    {
        // MySQL/MariaDB handles auto-increment automatically
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
