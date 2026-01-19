using Data.Context;
using Data.MigrationContext;
using IWDataMigration.Abstractions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace IWDataMigration.Providers;

/// <summary>
/// Target database provider for PostgreSQL databases.
/// </summary>
public sealed class PostgresTargetProvider : ITargetDatabaseProvider
{
    private readonly Func<PostgresqlDatabaseContext> _contextFactory;
    private PostgresqlDatabaseContext? _schemaContext;

    public DatabaseType Type => DatabaseType.PostgreSql;

    public PostgresTargetProvider(string connectionString)
    {
        // Enable legacy timestamp behavior for compatibility
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var options = new DbContextOptionsBuilder<PostgresqlDatabaseContext>()
            .UseNpgsql(connectionString)
            .EnableDetailedErrors()
            .EnableSensitiveDataLogging()
            .Options;

        _contextFactory = () => new PostgresqlDatabaseContext(options);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = _contextFactory();
            return await context.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory();
        await context.Database.MigrateAsync(cancellationToken);
        _schemaContext = _contextFactory();
    }

    public async Task WriteBatchAsync<T>(IEnumerable<T> batch, CancellationToken cancellationToken = default) where T : class
    {
        await using var context = _contextFactory();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        context.Set<T>().AddRange(batch);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw new InvalidOperationException(
                "Data already exists in target database. Please target an empty database.", e);
        }
    }

    public async Task UpdateSequencesAsync(CancellationToken cancellationToken = default)
    {
        if (_schemaContext is null)
        {
            await using var context = _contextFactory();
            await UpdateSequencesInternalAsync(context, cancellationToken);
        }
        else
        {
            await UpdateSequencesInternalAsync(_schemaContext, cancellationToken);
        }
    }

    private static async Task UpdateSequencesInternalAsync(DatabaseContext context, CancellationToken cancellationToken)
    {
        var tableAndColumnNames = context.Model.GetEntityTypes()
            .Select(x => new
            {
                TableName = x.GetTableName(),
                PrimaryKeyColumnName = x.GetKeys()
                    .First()
                    .Properties
                    .Select(p => p.GetColumnName())
                    .FirstOrDefault()
            })
            .Where(x => x.TableName is not null && x.PrimaryKeyColumnName is not null)
            .ToList();

        foreach (var tableAndColumn in tableAndColumnNames)
        {
            var sqlCommand = $"""
                              DO
                              $$
                              DECLARE
                                  max_id INTEGER;
                                  next_val INTEGER;
                              BEGIN
                                  SELECT COALESCE(MAX("{tableAndColumn.PrimaryKeyColumnName}"), 0) INTO max_id FROM "{tableAndColumn.TableName}";
                              next_val := max_id + 1;

                              PERFORM setval(pg_get_serial_sequence(quote_ident('{tableAndColumn.TableName}'), '{tableAndColumn.PrimaryKeyColumnName}'), next_val, false);
                              END
                                  $$;
                              """;

            await context.Database.ExecuteSqlRawAsync(sqlCommand, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_schemaContext is not null)
        {
            await _schemaContext.DisposeAsync();
        }
    }
}
