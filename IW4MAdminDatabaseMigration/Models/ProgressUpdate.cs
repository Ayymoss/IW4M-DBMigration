namespace IWDataMigration.Models;

/// <summary>
/// Represents a progress update for the migration UI.
/// </summary>
public sealed record ProgressUpdate
{
    public required ProgressUpdateType Type { get; init; }
    public required string TableName { get; init; }
    public int TotalRows { get; init; }
    public int ProcessedRows { get; init; }
    public bool IsIndeterminate { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Types of progress updates.
/// </summary>
public enum ProgressUpdateType
{
    TableStart,
    Progress,
    TableComplete,
    Error,
    Complete
}
