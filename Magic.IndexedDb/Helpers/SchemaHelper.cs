using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Helpers
{
    public static class SchemaHelper
    {
        internal static readonly ConcurrentDictionary<string, MagicTableAttribute?> _schemaCache = new();
        private static readonly ConcurrentDictionary<string, List<StoreSchema>> _databaseSchemasCache = new();
        private static bool _schemasScanned = false;
        private static readonly object _lock = new();

        /// <summary>
        /// Determines whether the given type is a complex entity registered in the schema cache.
        /// </summary>
        public static bool IsMagicEntity<T>()
        {
            return _schemaCache.ContainsKey(typeof(T).FullName!);
        }

        public static bool IsMagicEntity(Type type)
        {
            return _schemaCache.ContainsKey(type.FullName!);
        }

        /// <summary>
        /// Ensures the schema information for a given type is cached.
        /// </summary>
        internal static void EnsureSchemaIsCached(Type type)
        {
            string typeKey = type.FullName!;

            _schemaCache.GetOrAdd(typeKey, _ => type.GetCustomAttribute<MagicTableAttribute>());
        }

        /// <summary>
        /// Gets the schema name (table name) for the given type <typeparamref name="T"/>.
        /// Ensures all properties and schema information are cached together.
        /// </summary>
        public static string GetSchemaName<T>() where T : class
        {
            PropertyMappingCache.EnsureTypeIsCached<T>();
            EnsureSchemaIsCached(typeof(T));

            if (!_schemaCache.TryGetValue(typeof(T).FullName!, out var schemaAttribute) || schemaAttribute == null)
                throw new InvalidOperationException($"Type {typeof(T).Name} does not have a [MagicTable] attribute.");

            return schemaAttribute.SchemaName;
        }

        /// <summary>
        /// Retrieves all schemas for a given database name.
        /// </summary>
        public static List<StoreSchema> GetAllSchemas(string databaseName = null)
        {
            lock (_lock)
            {
                // If we've already scanned all schemas, return the cached list.
                if (_schemasScanned && _databaseSchemasCache.TryGetValue(databaseName ?? "DefaultedNone", out var cachedSchemas))
                    return cachedSchemas;

                var schemas = new List<StoreSchema>();
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (var assembly in assemblies)
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!type.IsClass || type.IsAbstract) continue;

                        // 🚀 **Only process classes that actually have the [MagicTable] attribute**
                        var schemaAttribute = type.GetCustomAttribute<MagicTableAttribute>();
                        if (schemaAttribute == null) continue;

                        // 🚀 Now that we confirmed it's a schema, ensure it's cached
                        PropertyMappingCache.EnsureTypeIsCached(type);
                        EnsureSchemaIsCached(type);

                        // Determine if the schema belongs to the target database
                        string dbName = !string.IsNullOrWhiteSpace(databaseName) ? databaseName : "DefaultedNone";
                        if (schemaAttribute.DatabaseName.Equals(dbName, StringComparison.OrdinalIgnoreCase))
                        {
                            var schema = GetStoreSchema(type);
                            schemas.Add(schema);
                        }
                    }
                }

                // Cache results for future calls
                _databaseSchemasCache[databaseName ?? "DefaultedNone"] = schemas;
                _schemasScanned = true;

                return schemas;
            }
        }


        /// <summary>
        /// Retrieves the store schema from a given type.
        /// Now fully optimized by leveraging cached attributes and property mappings.
        /// </summary>
        public static StoreSchema GetStoreSchema(Type type)
        {
            PropertyMappingCache.EnsureTypeIsCached(type);
            EnsureSchemaIsCached(type);

            if (!_schemaCache.TryGetValue(type.FullName!, out var schemaAttribute) || schemaAttribute == null)
                throw new InvalidOperationException($"Type {type.Name} does not have a [MagicTable] attribute.");

            var schema = new StoreSchema
            {
                Name = schemaAttribute.SchemaName,
                PrimaryKeyAuto = true
            };

            // Get the primary key property
            var primaryKeyProperty = type.GetProperties()
                .FirstOrDefault(prop => PropertyMappingCache.GetPropertyEntry(prop, type).PrimaryKey);

            if (primaryKeyProperty == null)
                throw new InvalidOperationException($"The entity {type.Name} does not have a primary key attribute.");

            schema.PrimaryKey = PropertyMappingCache.GetJsPropertyName(primaryKeyProperty, type);

            // Get unique index properties
            schema.UniqueIndexes = type.GetProperties()
                .Where(prop => PropertyMappingCache.GetPropertyEntry(prop, type).UniqueIndex)
                .Select(prop => PropertyMappingCache.GetJsPropertyName(prop, type))
                .ToList();

            // Get index properties
            schema.Indexes = type.GetProperties()
                .Where(prop => PropertyMappingCache.GetPropertyEntry(prop, type).Indexed)
                .Select(prop => PropertyMappingCache.GetJsPropertyName(prop, type))
                .ToList();

            return schema;
        }
    }
}
