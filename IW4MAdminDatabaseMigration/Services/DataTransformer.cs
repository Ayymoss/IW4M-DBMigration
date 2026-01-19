using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using IWDataMigration.Abstractions;

namespace IWDataMigration.Services;

/// <summary>
/// Transforms data for compatibility between different database types.
/// </summary>
public sealed class DataTransformer : IDataTransformer
{
    public IEnumerable<T> Transform<T>(IEnumerable<T> batch, DatabaseType targetType) where T : class
    {
        var items = batch.ToList();

        // MySQL/MariaDB can't handle Infinity in double values
        if (targetType == DatabaseType.MySql)
        {
            foreach (var item in items)
            {
                SanitizeDoubleValues(item);
            }
        }

        return items;
    }

    private static void SanitizeDoubleValues(object item)
    {
        var properties = item.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(double) || p.PropertyType == typeof(double?))
            .Where(p => p.GetCustomAttribute<NotMappedAttribute>() is null)
            .Where(p => p.CanRead && p.CanWrite);

        foreach (var prop in properties)
        {
            var value = prop.GetValue(item);
            if (value is not double dValue) continue;

            if (double.IsPositiveInfinity(dValue))
            {
                prop.SetValue(item, double.MaxValue);
            }
            else if (double.IsNegativeInfinity(dValue))
            {
                prop.SetValue(item, double.MinValue);
            }
            else if (double.IsNaN(dValue))
            {
                prop.SetValue(item, 0.0);
            }
        }
    }
}
