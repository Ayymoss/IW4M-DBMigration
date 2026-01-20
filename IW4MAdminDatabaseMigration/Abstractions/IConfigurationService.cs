namespace IWDataMigration.Abstractions;

/// <summary>
/// Manages migration configuration including connection strings.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Gets the source database connection string or file path.
    /// </summary>
    string SourceConnection { get; }
    /// <summary>
    /// Sets up the necessary structure in the target database.
    /// </summary>
    Task SetupStructureAsync();

    /// <summary>
    /// Gets the target database connection string.
    /// </summary>
    string TargetConnection { get; }

    /// <summary>
    /// Gets the source database type.
    /// </summary>
    DatabaseType SourceType { get; }

    /// <summary>
    /// Gets the target database type.
    /// </summary>
    DatabaseType TargetType { get; }

    /// <summary>
    /// Initializes configuration by prompting user for settings.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Initializes configuration from saved resume state without prompting.
    /// </summary>
    Task InitializeFromResumeAsync(DatabaseType sourceType, DatabaseType targetType);

    /// <summary>
    /// Validates the current configuration.
    /// </summary>
    bool Validate();
}
