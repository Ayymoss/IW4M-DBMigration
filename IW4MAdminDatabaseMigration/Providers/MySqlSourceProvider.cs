using System.Runtime.CompilerServices;
using Data.Context;
using Data.MigrationContext;
using IWDataMigration.Abstractions;
using IWDataMigration.Services;
using Microsoft.EntityFrameworkCore;

namespace IWDataMigration.Providers;

/// <summary>
/// Source database provider for MySQL/MariaDB databases.
/// </summary>
public sealed class MySqlSourceProvider : ISourceDatabaseProvider
{
    private readonly MySqlDatabaseContext _context;
    private readonly TableDependencyResolver _dependencyResolver;
    private IReadOnlyList<Type>? _migrationOrder;

    public DatabaseType Type => DatabaseType.MySql;

    public MySqlSourceProvider(string connectionString, TableDependencyResolver dependencyResolver)
    {
        _dependencyResolver = dependencyResolver;

        // Auto-detect server version for MySQL/MariaDB compatibility
        var serverVersion = ServerVersion.AutoDetect(connectionString);

        var options = new DbContextOptionsBuilder<MySqlDatabaseContext>()
            .UseMySql(connectionString, serverVersion)
            .Options;

        _context = new MySqlDatabaseContext(options);
        _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        // Disable foreign key checks for faster reads
        await _context.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS=0;", cancellationToken);
        await _context.Database.MigrateAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync<T>(CancellationToken cancellationToken = default) where T : class
    {
        return await _context.Set<T>().CountAsync(cancellationToken);
    }

    public async IAsyncEnumerable<IReadOnlyList<T>> ReadBatchesAsync<T>(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        await foreach (var batch in ReadBatchesFromOffsetAsync<T>(batchSize, 0, cancellationToken))
        {
            yield return batch;
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<T>> ReadBatchesFromOffsetAsync<T>(
        int batchSize,
        int startOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        var totalCount = await GetCountAsync<T>(cancellationToken);
        if (totalCount == 0 || startOffset >= totalCount) yield break;

        for (var offset = startOffset; offset < totalCount; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await _context.Set<T>()
                .AsNoTracking()
                .Skip(offset)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            yield return batch;
        }
    }

    public IReadOnlyList<Type> GetMigrationOrder()
    {
        return _migrationOrder ??= _dependencyResolver.Resolve(_context);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
    }
}
