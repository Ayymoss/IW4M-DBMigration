namespace IWDataMigration.Abstractions;

/// <summary>
/// Represents a source database provider for reading data during migration.
/// </summary>
public interface ISourceDatabaseProvider : IAsyncDisposable
{
    /// <summary>
    /// Gets the database type this provider handles.
    /// </summary>
    DatabaseType Type { get; }

    /// <summary>
    /// Tests the connection to the source database.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies EF Core migrations to the source database to ensure schema compatibility.
    /// </summary>
    Task ApplyMigrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of entities in a table.
    /// </summary>
    Task<int> GetCountAsync<T>(CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Reads entities from the source database in batches.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<T>> ReadBatchesAsync<T>(int batchSize, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Reads entities from the source database in batches, starting from a specific offset.
    /// Used for resuming a migration from a checkpoint.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<T>> ReadBatchesFromOffsetAsync<T>(
        int batchSize,
        int startOffset,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Gets the list of entity types to migrate in dependency order.
    /// </summary>
    IReadOnlyList<Type> GetMigrationOrder();
}
