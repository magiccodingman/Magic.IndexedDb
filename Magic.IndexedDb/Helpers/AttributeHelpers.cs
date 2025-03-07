using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Helpers
{
    public static class AttributeHelpers
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo?> _primaryKeyCache = new();

        public static object? GetPrimaryKeyValue<T>(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            var primaryKeyProperty = GetPrimaryKeyProperty(typeof(T));
            return primaryKeyProperty?.GetValue(item);
        }

        public static Type GetPrimaryKeyType<T>() where T : class
        {
            var primaryKeyProperty = GetPrimaryKeyProperty(typeof(T));
            if (primaryKeyProperty == null)
                throw new InvalidOperationException($"Type '{typeof(T).Name}' does not have a primary key with [MagicPrimaryKeyAttribute].");

            return primaryKeyProperty.PropertyType;
        }

        public static void ValidatePrimaryKey<T>(object key) where T : class
        {
            var expectedType = GetPrimaryKeyType<T>();

            if (key == null || !expectedType.IsInstanceOfType(key))
            {
                throw new ArgumentException($"Invalid key type. Expected: {expectedType}, received: {key?.GetType()}.");
            }
        }

        private static PropertyInfo? GetPrimaryKeyProperty(Type type)
        {
            return _primaryKeyCache.GetOrAdd(type, t =>
                t.GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute))));
        }
    }
}
