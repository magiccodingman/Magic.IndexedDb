using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Helpers
{
    public static class PropertyMappingCache
    {
        private static readonly ConcurrentDictionary<Type, Dictionary<PropertyInfo, MagicPropertyEntry>> _propertyCache = new();
        private static readonly ConcurrentDictionary<Type, MagicTableAttribute?> _schemaCache = new(); // Now storing the attribute reference

        /// <summary>
        /// Gets the schema name (table name) for the given type <typeparamref name="T"/>.
        /// Ensures all properties and schema information are cached together.
        /// </summary>
        public static string GetSchemaName<T>() where T : class
        {
            EnsureTypeIsCached<T>();

            if (!_schemaCache.TryGetValue(typeof(T), out var schemaAttribute) || schemaAttribute == null)
                throw new InvalidOperationException($"Type {typeof(T).Name} does not have a [MagicTable] attribute.");

            return schemaAttribute.SchemaName;
        }

        /// <summary>
        /// Gets the C# property name given a JavaScript property name.
        /// </summary>
        public static string GetCsharpPropertyName<T>(string jsPropertyName)
        {
            EnsureTypeIsCached<T>();

            var properties = _propertyCache[typeof(T)];
            return properties.FirstOrDefault(kvp => kvp.Value.JsPropertyName == jsPropertyName).Value.CsharpPropertyName ?? jsPropertyName;
        }

        /// <summary>
        /// Gets the C# property name given a PropertyInfo reference.
        /// </summary>
        public static string GetCsharpPropertyName<T>(PropertyInfo property)
        {
            return GetCsharpPropertyName<T>(property.Name);
        }

        /// <summary>
        /// Gets the JavaScript property name (ColumnName) given a C# property name.
        /// </summary>
        public static string GetJsPropertyName<T>(string csharpPropertyName)
        {
            EnsureTypeIsCached<T>();

            var properties = _propertyCache[typeof(T)];
            return properties.FirstOrDefault(kvp => kvp.Key.Name == csharpPropertyName).Value.JsPropertyName ?? csharpPropertyName;
        }

        /// <summary>
        /// Gets the JavaScript property name (ColumnName) given a PropertyInfo reference.
        /// </summary>
        public static string GetJsPropertyName<T>(PropertyInfo property)
        {
            EnsureTypeIsCached<T>();

            var properties = _propertyCache[typeof(T)];
            return properties.TryGetValue(property, out var propertyEntry) ? propertyEntry.JsPropertyName : property.Name;
        }

        /// <summary>
        /// Gets the cached MagicPropertyEntry for a given property name.
        /// </summary>
        public static MagicPropertyEntry GetPropertyEntry<T>(string propertyName)
        {
            EnsureTypeIsCached<T>();

            var properties = _propertyCache[typeof(T)];
            return properties.FirstOrDefault(kvp => kvp.Key.Name == propertyName).Value;
        }

        /// <summary>
        /// Gets the cached MagicPropertyEntry for a given PropertyInfo reference.
        /// </summary>
        public static MagicPropertyEntry GetPropertyEntry<T>(PropertyInfo property)
        {
            EnsureTypeIsCached<T>();

            var properties = _propertyCache[typeof(T)];
            return properties.TryGetValue(property, out var propertyEntry) ? propertyEntry : new MagicPropertyEntry();
        }


        /// <summary>
        /// Ensures that both schema and property caches are built for the given type.
        /// </summary>
        private static void EnsureTypeIsCached<T>()
        {
            Type type = typeof(T);

            // Ensures both caches are built in a single operation
            if (!_propertyCache.ContainsKey(type) || !_schemaCache.ContainsKey(type))
            {
                _schemaCache.GetOrAdd(type, t => t.GetCustomAttribute<MagicTableAttribute>());

                _propertyCache.GetOrAdd(type, t =>
                {
                    var propertyEntries = new Dictionary<PropertyInfo, MagicPropertyEntry>();

                    foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var columnAttribute = property.GetCustomAttributes()
                                                      .FirstOrDefault(attr => attr is IColumnNamed) as IColumnNamed;

                        if (columnAttribute != null && string.IsNullOrWhiteSpace(columnAttribute.ColumnName))
                        {
                            columnAttribute = null;
                        }

                        var magicEntry = new MagicPropertyEntry(
                            property,
                            columnAttribute,
                            property.IsDefined(typeof(MagicIndexAttribute), inherit: true),
                            property.IsDefined(typeof(MagicUniqueIndexAttribute), inherit: true),
                            property.IsDefined(typeof(MagicPrimaryKeyAttribute), inherit: true),
                            property.IsDefined(typeof(MagicNotMappedAttribute), inherit: true)
                        );

                        propertyEntries[property] = magicEntry;
                    }

                    return propertyEntries;
                });
            }
        }
    }
}
