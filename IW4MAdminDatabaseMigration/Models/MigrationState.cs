namespace IWDataMigration.Models;

/// <summary>
/// Checkpoint state persisted to disk for resume capability.
/// </summary>
public sealed class MigrationState
{
    /// <summary>
    /// Unique identifier for this migration session.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// When this migration session started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Last time the checkpoint was updated.
    /// </summary>
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>
    /// Name of the table currently being migrated.
    /// </summary>
    public string CurrentTable { get; set; } = string.Empty;

    /// <summary>
    /// Number of rows already processed in the current table.
    /// This is the offset to resume from.
    /// </summary>
    public int CurrentTableOffset { get; set; }

    /// <summary>
    /// Tables that have been fully migrated.
    /// </summary>
    public List<string> CompletedTables { get; set; } = [];

    /// <summary>
    /// Running total of all rows migrated across all tables.
    /// </summary>
    public long TotalRowsMigrated { get; set; }

    /// <summary>
    /// Source database type for this migration.
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Target database type for this migration.
    /// </summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>
    /// Whether the migration completed successfully.
    /// </summary>
    public bool IsComplete { get; set; }
}
