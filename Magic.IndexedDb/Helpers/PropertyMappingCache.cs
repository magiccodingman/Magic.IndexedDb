using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections;
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
        internal static readonly ConcurrentDictionary<string, Dictionary<string, MagicPropertyEntry>> _propertyCache = new();


        public static Dictionary<string, MagicPropertyEntry> GetTypeOfTProperties(Type type)
        {
            EnsureTypeIsCached(type);
            if (_propertyCache.TryGetValue(type.FullName!, out var properties))
            {
                return properties;
            }
            throw new Exception("Something went very wrong getting GetTypeOfTProperties");
        }

        public static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive ||
                   type.IsEnum ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(DateTimeOffset) ||
                   type == typeof(Guid) ||
                   type == typeof(Uri) ||
                   type == typeof(TimeSpan);
        }


        public static IEnumerable<Type> GetAllNestedComplexTypes(IEnumerable<PropertyInfo> properties)
        {
            HashSet<Type> complexTypes = new();
            Stack<Type> typeStack = new();

            // Initial population of the stack
            foreach (var property in properties)
            {
                if (IsComplexType(property.PropertyType))
                {
                    typeStack.Push(property.PropertyType);
                    complexTypes.Add(property.PropertyType);
                }
            }

            // Process all nested complex types
            while (typeStack.Count > 0)
            {
                var currentType = typeStack.Pop();
                var nestedProperties = currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var nestedProperty in nestedProperties)
                {
                    if (IsComplexType(nestedProperty.PropertyType) && !complexTypes.Contains(nestedProperty.PropertyType))
                    {
                        complexTypes.Add(nestedProperty.PropertyType);
                        typeStack.Push(nestedProperty.PropertyType);
                    }
                }
            }

            return complexTypes;
        }

        /*public static bool IsComplexType(Type type)
        {
            return !(IsSimpleType(type)
                  || type == typeof(string)
                  || typeof(IEnumerable).IsAssignableFrom(type) // Non-generic IEnumerable
                  || (type.IsGenericType && typeof(IEnumerable<>).IsAssignableFrom(type.GetGenericTypeDefinition())) // Generic IEnumerable<T>
                  || type.IsArray); // Arrays are collections too
        }*/

        public static bool IsComplexType(Type type)
        {
            if (IsSimpleType(type) || type == typeof(string))
                return false;

            // Handle generic collections like List<T>, Dictionary<TKey, TValue>
            if (type.IsGenericType)
            {
                Type genericTypeDef = type.GetGenericTypeDefinition();

                // If it's a generic IEnumerable<T>, get the type argument and check if it's complex
                if (typeof(IEnumerable<>).IsAssignableFrom(genericTypeDef))
                {
                    Type itemType = type.GetGenericArguments()[0];
                    return IsComplexType(itemType);
                }

                // Otherwise, it might be a generic class like StoreRecord<T>
                return type.GetGenericArguments().Any(IsComplexType);
            }

            // Handle non-generic collections like arrays
            if (typeof(IEnumerable).IsAssignableFrom(type) || type.IsArray)
                return false;

            return true; // Consider anything else a complex object
        }



        /// <summary>
        /// Gets the C# property name given a JavaScript property name.
        /// </summary>
        public static string GetCsharpPropertyName<T>(string jsPropertyName)
        {
            return GetCsharpPropertyName(jsPropertyName, typeof(T));
        }

        /// <summary>
        /// Gets the C# property name given a JavaScript property name.
        /// </summary>
        public static string GetCsharpPropertyName(string jsPropertyName, Type type)
        {
            EnsureTypeIsCached(type);
            string typeKey = type.FullName!;

            try
            {
                if (_propertyCache.TryGetValue(typeKey, out var properties) &&
                    properties.TryGetValue(jsPropertyName, out var entry))
                {
                    return entry.CsharpPropertyName;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error retrieving C# property name for JS property '{jsPropertyName}' in type {type.FullName}.", ex);
            }

            return jsPropertyName; // Fallback to original name if not found
        }

        /// <summary>
        /// Gets the JavaScript property name (ColumnName) given a C# property name.
        /// </summary>
        public static string GetJsPropertyName<T>(string csharpPropertyName)
        {
            return GetJsPropertyName(csharpPropertyName, typeof(T));
        }

        public static string GetJsPropertyName<T>(PropertyInfo prop)
        {
            return GetJsPropertyName(prop.Name, typeof(T));
        }

        public static string GetJsPropertyName(PropertyInfo prop, Type type)
        {
            return GetJsPropertyName(prop.Name, type);
        }

        /// <summary>
        /// Gets the JavaScript property name (ColumnName) given a C# property name.
        /// </summary>
        public static string GetJsPropertyName(string csharpPropertyName, Type type)
        {
            EnsureTypeIsCached(type);
            string typeKey = type.FullName!;

            try
            {
                if (_propertyCache.TryGetValue(typeKey, out var properties) &&
                    properties.TryGetValue(csharpPropertyName, out var entry))
                {
                    return entry.JsPropertyName;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error retrieving JS property name for C# property '{csharpPropertyName}' in type {type.FullName}.", ex);
            }

            return csharpPropertyName; // Fallback to original name if not found
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
            string typeKey = type.FullName!;

            try
            {
                if (_propertyCache.TryGetValue(typeKey, out var properties) &&
                    properties.TryGetValue(propertyName, out var entry))
                {
                    return entry;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error retrieving property entry for '{propertyName}' in type {type.FullName}.", ex);
            }

            throw new Exception($"Error: Property '{propertyName}' not found in type {type.FullName}.");
        }

        /// <summary>
        /// Gets the cached MagicPropertyEntry for a given PropertyInfo reference.
        /// </summary>
        public static MagicPropertyEntry GetPropertyEntry<T>(PropertyInfo property)
        {
            return GetPropertyEntry(property.Name, typeof(T));
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
            string typeKey = type.FullName!;

            // Avoid re-registering types if the typeKey already exists
            if (_propertyCache.ContainsKey(typeKey))
                return;

            // Ensure schema metadata is cached
            SchemaHelper.EnsureSchemaIsCached(type);

            // Initialize the dictionary for this type
            var propertyEntries = new Dictionary<string, MagicPropertyEntry>();

            List<MagicPropertyEntry> newMagicPropertyEntry = new List<MagicPropertyEntry>();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
            {
                if (property.GetIndexParameters().Length > 0)
                    continue; // 🔥 Skip indexers entirely

                string propertyKey = property.Name; // Now stored as string, not PropertyInfo

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
                newMagicPropertyEntry.Add(magicEntry);
                propertyEntries[propertyKey] = magicEntry; // Store property entry with string key
            }

            // Cache the properties for this type
            _propertyCache[typeKey] = propertyEntries;

            var complexTypes = GetAllNestedComplexTypes(newMagicPropertyEntry.Select(x => x.Property));
            if (complexTypes != null && complexTypes.Any())
            {
                foreach (var comp in complexTypes)
                {
                    EnsureTypeIsCached(comp);
                }
            }
        }
    }
}
