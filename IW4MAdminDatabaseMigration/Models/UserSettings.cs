namespace IWDataMigration.Models;

/// <summary>
/// User-configurable settings loaded from _settings.json.
/// </summary>
public sealed class UserSettings
{
    /// <summary>
    /// Number of rows to process in each batch.
    /// Higher values = faster migration but more memory usage.
    /// Default: 25,000
    /// </summary>
    public int BatchSize { get; set; } = 25_000;

    /// <summary>
    /// Seconds between checkpoint saves.
    /// Lower values = more I/O but safer resume points.
    /// Default: 30 seconds
    /// </summary>
    public int CheckpointIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Minutes before watchdog considers an operation hung.
    /// If no progress for this duration, diagnostics are logged.
    /// Default: 5 minutes
    /// </summary>
    public int WatchdogTimeoutMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum retry attempts per batch before failing.
    /// Default: 3 attempts
    /// </summary>
    public int MaxBatchRetries { get; set; } = 3;

    /// <summary>
    /// Enable verbose logging for debugging.
    /// Default: false
    /// </summary>
    public bool VerboseLogging { get; set; } = false;
}
