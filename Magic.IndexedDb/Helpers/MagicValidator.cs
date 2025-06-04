using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Helpers;

public static class MagicValidator
{
    public static void ValidateTables(List<Type>? magicTableClasses = null)
    {
        var errors = new StringBuilder();
        magicTableClasses ??= SchemaHelper.GetAllMagicTables();

        foreach (var type in magicTableClasses)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (!properties.Any())
            {
                errors.AppendLine($"Error: Class '{type.Name}' is marked as a Magic Table but has no properties.");
                continue;
            }

            var instance = Activator.CreateInstance(type) as IMagicTableBase;
            if (instance == null)
            {
                errors.AppendLine($"Error: Unable to create an instance of '{type.Name}'.");
                continue;
            }
                
            IMagicCompoundKey compoundKey = instance.GetKeys();
            List<IMagicCompoundIndex>? compoundIndexes = instance.GetCompoundIndexes();

            if (compoundKey == null || compoundKey.ColumnNamesInCompoundKey.Length == 0)
            {
                errors.AppendLine($"Error: Class '{type.Name}' must have at least one primary key.");
                continue;
            }

            // If there's only one key, treat it as a primary key; otherwise, enforce compound key rules.
            if (compoundKey.ColumnNamesInCompoundKey.Length == 1)
            {
                var primaryKeyProperty = compoundKey.PropertyInfos.First();
                if (compoundKey.AutoIncrement && !IsNumericType(primaryKeyProperty.PropertyType))
                {
                    errors.AppendLine($"Error: Primary key '{primaryKeyProperty.Name}' in class '{type.Name}' is marked as auto-increment, but its type '{primaryKeyProperty.PropertyType.Name}' is not numeric.");
                }

                if (Nullable.GetUnderlyingType(primaryKeyProperty.PropertyType) != null)
                {
                    errors.AppendLine($"Error: Primary key '{primaryKeyProperty.Name}' in class '{type.Name}' cannot be nullable.");
                }
            }
            else
            {
                // Validate compound key rules
                var keyNames = new HashSet<string>(compoundKey.ColumnNamesInCompoundKey);
                if (keyNames.Count != compoundKey.ColumnNamesInCompoundKey.Length)
                {
                    errors.AppendLine($"Error: Class '{type.Name}' has duplicate column names in its compound key definition.");
                }
            }

            var magicAttributes = new List<Type>
            {
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