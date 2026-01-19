namespace IWDataMigration.Models;

/// <summary>
/// Configuration options for migration.
/// </summary>
public sealed class MigrationOptions
{
    /// <summary>
    /// Number of records to process in each batch.
    /// </summary>
    public int BatchSize { get; set; } = 25_000;

    /// <summary>
    /// Directory where source database files are located.
    /// </summary>
    public string DatabaseSourceDirectory { get; set; } = "DatabaseSource";

    /// <summary>
    /// Connection string configuration file name.
    /// </summary>
    public string ConnectionStringFileName { get; set; } = "_TargetConnectionString.txt";

    /// <summary>
    /// Default connection string template.
    /// </summary>
    public string DefaultConnectionStringTemplate { get; set; } =
        "Host=HOSTNAME;Port=PORT;Username=USERNAME;Password=PASSWORD;Database=DATABASE";

    /// <summary>
    /// Default source database file name.
    /// </summary>
    public string DefaultDatabaseFileName { get; set; } = "Database.db";
}
