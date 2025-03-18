using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Helpers
{
    public static class MagicValidator
    {
        public static void ValidateTables(List<Type>? magicTableClasses = null)
        {
            var errors = new StringBuilder();
            magicTableClasses ??= SchemaHelper.GetAllMagicTables();

            foreach (var type in magicTableClasses)
            {
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var primaryKeyProps = properties.Where(p => p.GetCustomAttribute<MagicPrimaryKeyAttribute>() != null).ToList();

                if (!properties.Any())
                {
                    errors.AppendLine($"Error: Class '{type.Name}' is marked as a Magic Table but has no properties.");
                    continue;
                }

                var instance = Activator.CreateInstance(type);
                var getCompoundKeyMethod = type.GetMethod("GetCompoundKey", BindingFlags.Public | BindingFlags.Instance);
                var getCompoundIndexesMethod = type.GetMethod("GetCompoundIndexes", BindingFlags.Public | BindingFlags.Instance);

                IMagicCompoundKey? compoundKey = null;
                List<IMagicCompoundIndex>? compoundIndexes = null;

                if (getCompoundKeyMethod != null)
                {
                    try
                    {
                        compoundKey = getCompoundKeyMethod.Invoke(instance, null) as IMagicCompoundKey;
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException != null)
                    {
                        errors.AppendLine($"Error: Class '{type.Name}' encountered an issue when calling 'GetCompoundKey()'. Exception: {tie.InnerException.Message}");
                    }
                }

                if (getCompoundIndexesMethod != null)
                {
                    try
                    {
                        compoundIndexes = getCompoundIndexesMethod.Invoke(instance, null) as List<IMagicCompoundIndex>;
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException != null)
                    {
                        errors.AppendLine($"Error: Class '{type.Name}' encountered an issue when calling 'GetCompoundIndexes()'. Exception: {tie.InnerException.Message}");
                    }
                }

                // Validate primary key and compound key rules
                if (compoundKey?.ColumnNamesInCompoundKey?.Length > 0 && primaryKeyProps.Count > 0)
                {
                    errors.AppendLine($"Error: Class '{type.Name}' has both a primary key attribute and a compound key. Only one can be defined.");
                }
                else if (primaryKeyProps.Count == 0 && (compoundKey == null || compoundKey.ColumnNamesInCompoundKey.Length == 0))
                {
                    errors.AppendLine($"Error: Class '{type.Name}' must have either a primary key or a valid compound key.");
                }

                foreach (var prop in primaryKeyProps)
                {
                    var primaryKeyAttribute = prop.GetCustomAttribute<MagicPrimaryKeyAttribute>();
                    if (primaryKeyAttribute?.AutoIncrement == true && !IsNumericType(prop.PropertyType))
                    {
                        errors.AppendLine($"Error: Primary key '{prop.Name}' in class '{type.Name}' is marked as auto-increment, but its type '{prop.PropertyType.Name}' is not numeric.");
                    }

                    if (Nullable.GetUnderlyingType(prop.PropertyType) != null)
                    {
                        errors.AppendLine($"Error: Primary key '{prop.Name}' in class '{type.Name}' cannot be nullable.");
                    }
                }

                var magicAttributes = new List<Type>
            {
                typeof(MagicPrimaryKeyAttribute),
                typeof(MagicIndexAttribute),
                typeof(MagicNotMappedAttribute),
                typeof(MagicNameAttribute),
                typeof(MagicUniqueIndexAttribute)
            };

                foreach (var prop in properties)
                {
                    var appliedMagicAttributes = prop.GetCustomAttributes()
                                                     .Select(attr => attr.GetType())
                                                     .Where(attrType => magicAttributes.Contains(attrType))
                                                     .ToList();

                    if (appliedMagicAttributes.Count > 1)
                    {
                        errors.AppendLine($"Error: Property '{prop.Name}' in class '{type.Name}' has multiple Magic attributes ({string.Join(", ", appliedMagicAttributes.Select(a => a.Name))}). A property can have at most one Magic attribute.");
                    }
                }

                if (compoundIndexes != null)
                {
                    var compoundIndexSets = new HashSet<string>();
                    foreach (var index in compoundIndexes)
                    {
                        string indexKey = string.Join(",", index.ColumnNamesInCompoundIndex.OrderBy(x => x));
                        if (!compoundIndexSets.Add(indexKey))
                        {
                            errors.AppendLine($"Error: Duplicate compound index detected in class '{type.Name}' with the same properties ({indexKey}).");
                        }
                    }
                }
            }

            if (errors.Length > 0)
            {
                throw new Exception($"Magic Table Validation Errors:\n{errors}");
            }
        }

        private static bool IsNumericType(Type type)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            return underlyingType == typeof(byte) || underlyingType == typeof(sbyte) ||
                   underlyingType == typeof(short) || underlyingType == typeof(ushort) ||
                   underlyingType == typeof(int) || underlyingType == typeof(uint) ||
                   underlyingType == typeof(long) || underlyingType == typeof(ulong) ||
                   underlyingType == typeof(float) || underlyingType == typeof(double) ||
                   underlyingType == typeof(decimal);
        }
    }
}
