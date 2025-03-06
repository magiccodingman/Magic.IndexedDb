using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Helpers
{
    public static class SchemaHelper
    {
        public const string defaultNone = "DefaultedNone";

        private static readonly ConcurrentDictionary<Type, string> _schemaCache = new();

        public static string GetSchemaName<T>() where T : class
        {
            Type type = typeof(T);

            if (_schemaCache.TryGetValue(type, out string schemaName))
            {
                return schemaName;
            }

            var schemaAttribute = type.GetCustomAttribute<MagicTableAttribute>();
            if (schemaAttribute != null)
            {
                schemaName = schemaAttribute.SchemaName;
            }
            else
            {
                schemaName = type.Name;
            }

            _schemaCache[type] = schemaName;
            return schemaName;
        }

    }
}
