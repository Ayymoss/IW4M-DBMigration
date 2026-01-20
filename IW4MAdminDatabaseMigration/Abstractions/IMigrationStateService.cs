using IWDataMigration.Models;

namespace IWDataMigration.Abstractions;

/// <summary>
/// Service for managing migration checkpoint state.
/// </summary>
public interface IMigrationStateService
{
    /// <summary>
    /// Gets the current migration state (null if no state exists).
    /// </summary>
    MigrationState? CurrentState { get; }

    /// <summary>
    /// Checks if a previous migration state exists that can be resumed.
    /// </summary>
    Task<MigrationState?> LoadExistingStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new migration session.
    /// </summary>
    Task<MigrationState> CreateNewSessionAsync(
        string sourceType,
        string targetType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates progress and immediately saves checkpoint to disk.
    /// Called after each successful batch write.
    /// </summary>
    Task UpdateProgressAsync(
        string tableName,
        int processedRows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a table as fully completed.
    /// </summary>
    Task MarkTableCompleteAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks migration as complete and clears state file.
    /// </summary>
    Task CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the state file (for fresh start).
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
