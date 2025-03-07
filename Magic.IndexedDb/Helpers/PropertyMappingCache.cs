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
        internal static readonly ConcurrentDictionary<Type, Dictionary<PropertyInfo, MagicPropertyEntry>> _propertyCache = new();
        
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
        public static string GetJsPropertyName(string csharpPropertyName, Type type)
        {
            EnsureTypeIsCached(type);

            var properties = _propertyCache[type];
            return properties.FirstOrDefault(kvp => kvp.Key.Name == csharpPropertyName).Value.JsPropertyName ?? csharpPropertyName;
        }


        /// <summary>
        /// Gets the JavaScript property name (ColumnName) given a C# property name.
        /// </summary>
        public static string GetJsPropertyName<T>(string csharpPropertyName)
        {
            return GetJsPropertyName(csharpPropertyName, typeof(T));
        }

        /// <summary>
        /// Gets the JavaScript property name (ColumnName) given a PropertyInfo reference.
        /// </summary>
        public static string GetJsPropertyName(PropertyInfo property, Type type)
        {
            return GetJsPropertyName(property.Name, type);
        }

        /// <summary>
        /// Gets the JavaScript property name (ColumnName) given a PropertyInfo reference.
        /// </summary>
        public static string GetJsPropertyName<T>(PropertyInfo property)
        {
            return GetJsPropertyName<T>(property.Name);
        }

        /// <summary>
        /// Gets the cached MagicPropertyEntry for a given property name.
        /// </summary>
        public static MagicPropertyEntry GetPropertyEntry<T>(string propertyName)
        {
            return GetPropertyEntry(propertyName, typeof(T));
        }

        /// <summary>
        /// Gets the cached MagicPropertyEntry for a given property name.
        /// </summary>
        public static MagicPropertyEntry GetPropertyEntry(string propertyName, Type type)
        {
            EnsureTypeIsCached(type);

            var properties = _propertyCache[type];
            return properties.FirstOrDefault(kvp => kvp.Key.Name == propertyName).Value;
        }

        /// <summary>
        /// Gets the cached MagicPropertyEntry for a given PropertyInfo reference.
        /// </summary>
        public static MagicPropertyEntry GetPropertyEntry<T>(PropertyInfo property)
        {
            return GetPropertyEntry<T>(property.Name);
        }

        /// <summary>
        /// Gets the cached MagicPropertyEntry for a given PropertyInfo reference.
        /// </summary>
        public static MagicPropertyEntry GetPropertyEntry(PropertyInfo property, Type type)
        {
            return GetPropertyEntry(property.Name, type);
        }

        /// <summary>
        /// Ensures that both schema and property caches are built for the given type.
        /// </summary>
        internal static void EnsureTypeIsCached<T>()
        {
            Type type = typeof(T);
        }

        internal static void EnsureTypeIsCached(Type type)
        {
            // Ensures both caches are built in a single operation
            if (!_propertyCache.ContainsKey(type) || !SchemaHelper._schemaCache.ContainsKey(type))
            {
                SchemaHelper._schemaCache.GetOrAdd(type, t => t.GetCustomAttribute<MagicTableAttribute>());

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
