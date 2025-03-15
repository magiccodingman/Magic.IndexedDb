﻿using Magic.IndexedDb.Interfaces;
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
        //private static readonly ConcurrentDictionary<string, List<StoreSchema>> _databaseSchemasCache = new();
        internal static readonly ConcurrentDictionary<Type, IMagicTableBase?> _schemaCache = new();

        internal static void EnsureSchemaIsCached(Type type)
        {
            _schemaCache.GetOrAdd(type, _ =>
            {
                // Check if the type implements IMagicTableBase
                return typeof(IMagicTableBase).IsAssignableFrom(type)
                    ? (Activator.CreateInstance(type) as IMagicTableBase) // Store the instance if valid
                    : null; // Store null if it doesn’t implement IMagicTableBase
            });
        }


        public static bool ImplementsIMagicTable(Type type)
        {
            return type.GetInterfaces().Any(i => i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IMagicTable<>));
        }

        public static List<Type>? GetAllMagicTables()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            return assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && SchemaHelper.ImplementsIMagicTable(t))
                .ToList();
        }

        private static readonly ConcurrentDictionary<Type, string> _tableNameCache = new();
        private static readonly ConcurrentDictionary<Type, string> _databaseNameCache = new();

        public static string GetDefaultDatabaseName<T>() where T : IMagicTableBase, new()
        {
            Type type = typeof(T);

            if (_databaseNameCache.TryGetValue(type, out string cachedDatabaseName))
            {
                return cachedDatabaseName;
            }

            // Create an instance of T and retrieve database name
            var instance = new T();
            string databaseName = instance.GetDefaultDatabase().DatabaseName;

            _databaseNameCache[type] = databaseName;
            return databaseName;
        }

        public static string GetTableName<T>() where T : class
        {
            Type type = typeof(T);

            if (_tableNameCache.TryGetValue(type, out string cachedTableName))
            {
                return cachedTableName;
            }

            // Check if the type implements IMagicTableBase
            if (!typeof(IMagicTableBase).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"Type {type.Name} does not implement IMagicTableBase.");
            }

            // Create an instance dynamically and retrieve the table name
            if (Activator.CreateInstance(type) is not IMagicTableBase instance)
            {
                throw new InvalidOperationException($"Failed to create an instance of {type.Name}.");
            }

            string tableName = instance.GetTableName();

            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new InvalidOperationException($"Type {type.Name} returned an invalid table name.");
            }

            _tableNameCache[type] = tableName;
            return tableName;
        }

        public static bool HasMagicTableInterface(Type type)
        {
            return typeof(IMagicTableBase).IsAssignableFrom(type);
        }

        /// <summary>
        /// Retrieves all schemas for a given database name.
        /// </summary>
        public static List<StoreSchema> GetAllSchemas(string? databaseName = null)
        {
                var schemas = new List<StoreSchema>();

                // 🔹 Retrieve all valid magic tables
                var magicTables = GetAllMagicTables();

                foreach (var type in magicTables)
                {
                    // Ensure schema is cached
                    EnsureSchemaIsCached(type);

                    // Retrieve cached entry
                    if (!_schemaCache.TryGetValue(type, out var instance) || instance == null)
                        continue; // Skip if the type is not a valid IMagicTableBase

                    // 🚀 Get the store schema
                    var schema = GetStoreSchema(type);
                    schemas.Add(schema);
                }

                return schemas;
        }


        /// <summary>
        /// Retrieves the store schema from a given type.
        /// Now fully optimized by leveraging cached attributes and property mappings.
        /// </summary>
        public static StoreSchema GetStoreSchema(Type type)
        {
            PropertyMappingCache.EnsureTypeIsCached(type);
            EnsureSchemaIsCached(type);

            // Retrieve the cached entry
            if (!_schemaCache.TryGetValue(type, out var instance))
            {
                throw new InvalidOperationException($"Type {type.Name} is not cached.");
            }

            // Ensure the type actually implements IMagicTableBase
            if (instance == null)
            {
                throw new InvalidOperationException($"Type {type.Name} does not implement IMagicTableBase and cannot be used as a Magic Table.");
            }

            // Retrieve table name
            var tableName = instance.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new InvalidOperationException($"Type {type.Name} returned an invalid table name.");
            }

            var schema = new StoreSchema
            {
                TableName = tableName,
                PrimaryKeyAuto = true
            };

            // Get the primary key property
            var primaryKeyProperty = type.GetProperties()
                .FirstOrDefault(prop => PropertyMappingCache.GetPropertyEntry(prop, type).PrimaryKey);

            if (primaryKeyProperty == null)
            {
                throw new InvalidOperationException($"The entity {type.Name} does not have a primary key attribute.");
            }

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

            // Extract Compound Key
            var compoundKey = instance.GetCompoundKey();
            if (compoundKey != null)
            {
                schema.ColumnNamesInCompoundKey = compoundKey.ColumnNamesInCompoundKey.ToList();
            }

            // Extract Compound Indexes
            var compoundIndexes = instance.GetCompoundIndexes();
            if (compoundIndexes != null)
            {
                schema.ColumnNamesInCompoundIndex = compoundIndexes
                    .Select(index => index.ColumnNamesInCompoundIndex.ToList())
                    .ToList();
            }

            return schema;
        }


    }
}
