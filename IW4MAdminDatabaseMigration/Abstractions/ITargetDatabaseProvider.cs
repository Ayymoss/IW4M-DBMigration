namespace IWDataMigration.Abstractions;

/// <summary>
/// Represents a target database provider for writing data during migration.
/// </summary>
public interface ITargetDatabaseProvider : IAsyncDisposable
{
    /// <summary>
    /// Gets the database type this provider writes to.
    /// </summary>
    DatabaseType Type { get; }

    /// <summary>
    /// Tests the connection to the target database.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies EF Core migrations to ensure the target schema is ready.
    /// </summary>
    Task ApplyMigrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a batch of entities to the target database.
    /// </summary>
    Task WriteBatchAsync<T>(IEnumerable<T> batch, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Updates auto-increment sequences after migration (PostgreSQL-specific).
    /// </summary>
    Task UpdateSequencesAsync(CancellationToken cancellationToken = default);
}
