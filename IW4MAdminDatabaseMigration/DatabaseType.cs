namespace IWDataMigration;

/// <summary>
/// Supported database types for migration.
/// </summary>
public enum DatabaseType
{
    /// <summary>
    /// SQLite (source only).
    /// </summary>
    Sqlite,

    /// <summary>
    /// MySQL/MariaDB (source and target).
    /// </summary>
    MySql,

    /// <summary>
    /// PostgreSQL (target only).
    /// </summary>
    PostgreSql
}
