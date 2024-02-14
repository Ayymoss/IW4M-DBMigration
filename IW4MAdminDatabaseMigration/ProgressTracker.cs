using System.Collections.Concurrent;
using Spectre.Console;

namespace IWDataMigration;

public class ProgressTracker
{
    private event Action<string, bool>? OnCreated;
    private event Action<string, double>? OnUpdated;
    private event Action<string>? OnStopped;

    private readonly ManualResetEventSlim _resetEvent = new(false);
    public CancellationTokenSource CancellationTokenSource { get; } = new();

    public void AddTask(string key, bool indeterminate = false) => OnCreated?.Invoke(key, indeterminate);
    public void UpdateProgress(string key, double value) => OnUpdated?.Invoke(key, value);
    public void StopTask(string key) => OnStopped?.Invoke(key);

    public void SetProgressDisplay()
    {
        var localTracker = new ConcurrentDictionary<string, ProgressTask>();

        AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(Spinner.Known.Dots2))
            .StartAsync(async ctx =>
            {
                OnCreated += (taskName, indeterminate) =>
                {
                    var task = ctx.AddTask(taskName, indeterminate);
                    localTracker.TryAdd(taskName, task);
                    if (indeterminate) task.IsIndeterminate = true;
                };

                OnUpdated += (taskName, progress) =>
                {
                    if (!localTracker.TryGetValue(taskName, out var existingTask)) return;
                    existingTask.StartTask();
                    existingTask.Value = progress;
                    _resetEvent.Set();
                };

                OnStopped += taskName =>
                {
                    if (!localTracker.TryGetValue(taskName, out var existingTask)) return;
                    existingTask.IsIndeterminate = false;
                    existingTask.Value = 100;
                    existingTask.StopTask();
                };

                _resetEvent.Wait(CancellationTokenSource.Token);

                while (!ctx.IsFinished && !CancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, CancellationTokenSource.Token);
                }
            });
    }
}
