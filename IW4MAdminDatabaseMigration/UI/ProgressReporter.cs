using System.Collections.Concurrent;
using System.Threading.Channels;
using IWDataMigration.Abstractions;
using IWDataMigration.Models;
using Spectre.Console;

namespace IWDataMigration.UI;

/// <summary>
/// Channel-based progress reporter that fixes SpectreConsole freeze issues.
/// Uses System.Threading.Channels for thread-safe communication between
/// migration workers and the UI thread.
/// </summary>
public sealed class ProgressReporter : IProgressReporter
{
    private readonly Channel<ProgressUpdate> _channel;
    private readonly ConcurrentDictionary<string, (ProgressTask Task, int Total)> _tasks = new();
    private Task? _displayTask;
    private bool _isComplete;

    public ProgressReporter()
    {
        // Unbounded channel to prevent blocking on write
        _channel = Channel.CreateUnbounded<ProgressUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void ReportTableStart(string tableName, int totalRows, bool isIndeterminate = false)
    {
        _channel.Writer.TryWrite(new ProgressUpdate
        {
            Type = ProgressUpdateType.TableStart,
            TableName = tableName,
            TotalRows = totalRows,
            IsIndeterminate = isIndeterminate
        });
    }

    public void ReportProgress(string tableName, int processedRows)
    {
        _channel.Writer.TryWrite(new ProgressUpdate
        {
            Type = ProgressUpdateType.Progress,
            TableName = tableName,
            ProcessedRows = processedRows
        });
    }

    public void ReportTableComplete(string tableName)
    {
        _channel.Writer.TryWrite(new ProgressUpdate
        {
            Type = ProgressUpdateType.TableComplete,
            TableName = tableName
        });
    }

    public void ReportError(string message)
    {
        _channel.Writer.TryWrite(new ProgressUpdate
        {
            Type = ProgressUpdateType.Error,
            TableName = string.Empty,
            ErrorMessage = message
        });
    }

    public void Complete()
    {
        _isComplete = true;
        _channel.Writer.TryComplete();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _displayTask = AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(Spinner.Known.Dots2))
            .StartAsync(async ctx =>
            {
                // Read from channel on the UI thread - this is the key fix!
                // The UI thread owns the ProgressContext and reads updates from the channel.
                await foreach (var update in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    ProcessUpdate(ctx, update);

                    if (_isComplete)
                    {
                        break;
                    }
                }
            });

        await _displayTask;
    }

    private void ProcessUpdate(ProgressContext ctx, ProgressUpdate update)
    {
        switch (update.Type)
        {
            case ProgressUpdateType.TableStart:
                // Check if task already exists (update instead of create)
                if (_tasks.TryGetValue(update.TableName, out var existingTask))
                {
                    // Update existing task with new values
                    existingTask.Task.MaxValue = update.TotalRows > 0 ? update.TotalRows : 100;
                    existingTask.Task.IsIndeterminate = update.IsIndeterminate || update.TotalRows == 0;
                    _tasks[update.TableName] = (existingTask.Task, update.TotalRows);
                }
                else
                {
                    // Create new task
                    var task = ctx.AddTask(update.TableName, autoStart: true, maxValue: update.TotalRows > 0 ? update.TotalRows : 100);
                    if (update.IsIndeterminate || update.TotalRows == 0)
                    {
                        task.IsIndeterminate = true;
                    }
                    _tasks[update.TableName] = (task, update.TotalRows);
                }
                break;

            case ProgressUpdateType.Progress:
                if (_tasks.TryGetValue(update.TableName, out var progressInfo))
                {
                    progressInfo.Task.Value = update.ProcessedRows;
                }
                break;

            case ProgressUpdateType.TableComplete:
                if (_tasks.TryGetValue(update.TableName, out var completeInfo))
                {
                    completeInfo.Task.IsIndeterminate = false;
                    completeInfo.Task.Value = completeInfo.Total > 0 ? completeInfo.Total : 100;
                    completeInfo.Task.StopTask();
                }
                break;

            case ProgressUpdateType.Error:
                AnsiConsole.MarkupLine($"[red]Error: {update.ErrorMessage}[/]");
                break;

            case ProgressUpdateType.Complete:
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Complete();
        if (_displayTask is not null)
        {
            try
            {
                await _displayTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
        }
        _tasks.Clear();
    }
}
