namespace IWDataMigration.Abstractions;

/// <summary>
/// Transforms data during migration between different database types.
/// </summary>
public interface IDataTransformer
{
    /// <summary>
    /// Transforms a batch of entities for compatibility with the target database.
    /// </summary>
    /// <param name="batch">The batch of entities to transform.</param>
    /// <param name="targetType">The target database type.</param>
    /// <returns>Transformed batch of entities.</returns>
    IEnumerable<T> Transform<T>(IEnumerable<T> batch, DatabaseType targetType) where T : class;
}
