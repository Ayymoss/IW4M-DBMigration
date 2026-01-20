using System.Text.Json;
using IWDataMigration.Abstractions;
using IWDataMigration.Models;
using Microsoft.Extensions.Options;

namespace IWDataMigration.Services;

/// <summary>
/// Manages migration checkpoint state persistence.
/// </summary>
public sealed class MigrationStateService : IMigrationStateService
{
    private readonly string _statePath;
    private readonly object _lock = new();
    private MigrationState? _currentState;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public MigrationState? CurrentState => _currentState;

    public MigrationStateService(IOptions<MigrationOptions> options)
    {
        var executingDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
        _statePath = Path.Combine(executingDir, options.Value.StateFileName);
    }

    public async Task<MigrationState?> LoadExistingStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_statePath, cancellationToken);
            var state = JsonSerializer.Deserialize<MigrationState>(json, JsonOptions);

            if (state is { IsComplete: false })
            {
                _currentState = state;
                return state;
            }

            // Completed states shouldn't trigger resume
            return null;
        }
        catch (JsonException)
        {
            // Corrupted state file - treat as no state
            return null;
        }
    }

    public async Task<MigrationState> CreateNewSessionAsync(
        string sourceType,
        string targetType,
        CancellationToken cancellationToken = default)
    {
        _currentState = new MigrationState
        {
            SessionId = Guid.NewGuid().ToString("N"),
            StartedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            SourceType = sourceType,
            TargetType = targetType,
            CompletedTables = [],
            IsComplete = false
        };

        await SaveStateAsync(cancellationToken);
        return _currentState;
    }

    public async Task UpdateProgressAsync(
        string tableName,
        int processedRows,
        CancellationToken cancellationToken = default)
    {
        if (_currentState is null)
        {
            throw new InvalidOperationException("No active migration session.");
        }

        lock (_lock)
        {
            _currentState.CurrentTable = tableName;
            _currentState.CurrentTableOffset = processedRows;
            _currentState.LastUpdatedAt = DateTime.UtcNow;
        }

        // Immediately persist to disk after each batch
        await SaveStateAsync(cancellationToken);
    }

    public async Task MarkTableCompleteAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_currentState is null)
        {
            throw new InvalidOperationException("No active migration session.");
        }

        lock (_lock)
        {
            if (!_currentState.CompletedTables.Contains(tableName))
            {
                _currentState.CompletedTables.Add(tableName);
            }

            _currentState.TotalRowsMigrated += _currentState.CurrentTableOffset;
            _currentState.CurrentTable = string.Empty;
            _currentState.CurrentTableOffset = 0;
            _currentState.LastUpdatedAt = DateTime.UtcNow;
        }

        await SaveStateAsync(cancellationToken);
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_currentState is not null)
        {
            _currentState.IsComplete = true;
            _currentState.LastUpdatedAt = DateTime.UtcNow;
            await SaveStateAsync(cancellationToken);
        }

        // Delete the state file on successful completion
        await ClearAsync(cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _currentState = null;

        if (File.Exists(_statePath))
        {
            File.Delete(_statePath);
        }

        return Task.CompletedTask;
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        if (_currentState is null) return;

        MigrationState stateCopy;
        lock (_lock)
        {
            // Create a copy to avoid serialization issues with concurrent access
            stateCopy = new MigrationState
            {
                SessionId = _currentState.SessionId,
                StartedAt = _currentState.StartedAt,
                LastUpdatedAt = _currentState.LastUpdatedAt,
                CurrentTable = _currentState.CurrentTable,
                CurrentTableOffset = _currentState.CurrentTableOffset,
                CompletedTables = [.. _currentState.CompletedTables],
                TotalRowsMigrated = _currentState.TotalRowsMigrated,
                SourceType = _currentState.SourceType,
                TargetType = _currentState.TargetType,
                IsComplete = _currentState.IsComplete
            };
        }

        var json = JsonSerializer.Serialize(stateCopy, JsonOptions);

        // Write to temp file first, then rename for atomicity
        var tempPath = _statePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        File.Move(tempPath, _statePath, overwrite: true);
    }
}
