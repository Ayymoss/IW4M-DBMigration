using System.Diagnostics;
using System.Reflection;
using IWDataMigration.Abstractions;
using IWDataMigration.Models;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace IWDataMigration.Services;

/// <summary>
/// Monitors for hangs and provides diagnostics when operations stall.
/// Also writes to a log file since console output during Progress is not visible.
/// </summary>
public sealed class WatchdogService : IWatchdogService
{
    private readonly TimeSpan _timeout;
    private readonly bool _verboseLogging;
    private readonly Timer _timer;
    private readonly string _logPath;
    private readonly Lock _lock = new();

    private DateTime _lastHeartbeat;
    private string _lastContext = string.Empty;
    private bool _isRunning;
    private bool _alertRaised;

    public event EventHandler<WatchdogAlertEventArgs>? HangDetected;

    public WatchdogService(IOptions<MigrationOptions> options)
    {
        var settings = options.Value.UserSettings;
        _timeout = TimeSpan.FromMinutes(settings.WatchdogTimeoutMinutes);
        _verboseLogging = settings.VerboseLogging;

        // Log file in same directory as executable
        var executingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        _logPath = Path.Combine(executingDir, "_watchdog.log");

        // Check every 30 seconds
        _timer = new Timer(CheckForHang, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(string initialContext)
    {
        lock (_lock)
        {
            _lastHeartbeat = DateTime.UtcNow;
            _lastContext = initialContext;
            _isRunning = true;
            _alertRaised = false;
        }

        // Start checking every 30 seconds
        _timer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        Log($"Watchdog started with {_timeout.TotalMinutes} minute timeout");
    }

    public void Heartbeat(string context)
    {
        lock (_lock)
        {
            _lastHeartbeat = DateTime.UtcNow;
            _lastContext = context;
            _alertRaised = false; // Reset alert on new activity
        }

        if (_verboseLogging)
        {
            Log($"Heartbeat: {context}");
        }
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);

        lock (_lock)
        {
            _isRunning = false;
        }

        Log("Watchdog stopped");
    }

    private void CheckForHang(object? state)
    {
        DateTime lastBeat;
        string context;
        bool shouldAlert;

        lock (_lock)
        {
            if (!_isRunning) return;

            lastBeat = _lastHeartbeat;
            context = _lastContext;
            var elapsed = DateTime.UtcNow - lastBeat;
            shouldAlert = elapsed > _timeout && !_alertRaised;

            if (shouldAlert)
            {
                _alertRaised = true;
            }
        }

        if (shouldAlert)
        {
            var elapsed = DateTime.UtcNow - lastBeat;
            RaiseHangDetected(context, elapsed, lastBeat);
        }
    }

    private void RaiseHangDetected(string context, TimeSpan elapsed, DateTime lastBeat)
    {
        // Log to file (always visible)
        Log("=== WATCHDOG ALERT ===");
        Log($"No activity for: {elapsed.TotalMinutes:F1} minutes");
        Log($"Last activity: {lastBeat:HH:mm:ss} UTC");
        Log($"Context: {context}");
        Log($"Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
        Log($"Threads: {Process.GetCurrentProcess().Threads.Count}");
        Log("The migration appears to be stuck.");
        Log("======================");

        // Try to display to console - this will work after progress completes
        // or may interrupt progress display (which is fine for critical alert)
        try
        {
            AnsiConsole.WriteLine();
            
            var panel = new Panel(
                new Rows(
                    new Markup($"[yellow]No activity for:[/] [red]{elapsed.TotalMinutes:F1} minutes[/]"),
                    new Markup($"[yellow]Last activity:[/]   [cyan]{lastBeat:HH:mm:ss} UTC[/]"),
                    new Markup($"[yellow]Context:[/]         [cyan]{Markup.Escape(context)}[/]"),
                    new Rule().RuleStyle("dim"),
                    new Markup($"[yellow]Memory Usage:[/]    [cyan]{GC.GetTotalMemory(false) / 1024 / 1024} MB[/]"),
                    new Markup($"[yellow]Thread Count:[/]    [cyan]{Process.GetCurrentProcess().Threads.Count}[/]"),
                    new Rule().RuleStyle("dim"),
                    new Markup("[dim]The migration appears to be stuck.[/]"),
                    new Markup("[dim]Check [cyan]_watchdog.log[/] for details.[/]"),
                    new Text(""),
                    new Markup("[green]Press Ctrl+C to cancel. Restart to resume.[/]")
                ))
            {
                Header = new PanelHeader("[bold red] WATCHDOG ALERT [/]"),
                Border = BoxBorder.Double,
                BorderStyle = new Style(Color.Red)
            };
            
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }
        catch
        {
            // Console output failed - log file is the backup
        }

        HangDetected?.Invoke(this, new WatchdogAlertEventArgs
        {
            LastContext = context,
            ElapsedSinceLastHeartbeat = elapsed,
            LastHeartbeatTime = lastBeat
        });
    }

    private void Log(string message)
    {
        try
        {
            var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}";
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch
        {
            // Ignore log write failures
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        await _timer.DisposeAsync();
    }
}
