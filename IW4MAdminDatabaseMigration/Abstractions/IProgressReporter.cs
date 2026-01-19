using IWDataMigration.Models;

namespace IWDataMigration.Abstractions;

/// <summary>
/// Provides thread-safe progress reporting for migration operations.
/// </summary>
public interface IProgressReporter : IAsyncDisposable
{
    /// <summary>
    /// Reports that a table migration has started.
    /// </summary>
    void ReportTableStart(string tableName, int totalRows, bool isIndeterminate = false);

    /// <summary>
    /// Reports progress for a table migration.
    /// </summary>
    void ReportProgress(string tableName, int processedRows);

    /// <summary>
    /// Reports that a table migration has completed.
    /// </summary>
    void ReportTableComplete(string tableName);

    /// <summary>
    /// Reports an error during migration.
    /// </summary>
    void ReportError(string message);

    /// <summary>
    /// Starts the progress display.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals that all migration work is complete.
    /// </summary>
    void Complete();
}
