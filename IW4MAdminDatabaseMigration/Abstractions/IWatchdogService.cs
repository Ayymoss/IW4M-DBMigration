namespace IWDataMigration.Abstractions;

/// <summary>
/// Watchdog service that monitors for hangs and provides diagnostics.
/// </summary>
public interface IWatchdogService : IAsyncDisposable
{
    /// <summary>
    /// Starts the watchdog monitoring.
    /// </summary>
    void Start(string initialContext);

    /// <summary>
    /// Signals activity to prevent timeout. Call this regularly during operations.
    /// </summary>
    void Heartbeat(string context);

    /// <summary>
    /// Stops the watchdog monitoring.
    /// </summary>
    void Stop();

    /// <summary>
    /// Event raised when a potential hang is detected.
    /// </summary>
    event EventHandler<WatchdogAlertEventArgs>? HangDetected;
}

/// <summary>
/// Event args for watchdog hang detection.
/// </summary>
public sealed class WatchdogAlertEventArgs : EventArgs
{
    public required string LastContext { get; init; }
    public required TimeSpan ElapsedSinceLastHeartbeat { get; init; }
    public required DateTime LastHeartbeatTime { get; init; }
}
