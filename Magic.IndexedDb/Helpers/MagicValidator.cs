﻿using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Helpers
{
    internal static class MagicValidator
    {
        public static void ValidateTables()
        {
            var errors = new StringBuilder();
            var magicTableClasses = SchemaHelper.GetAllMagicTables();


            foreach (var type in magicTableClasses)
            {
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var primaryKeyProps = properties.Where(p => p.GetCustomAttribute<MagicPrimaryKeyAttribute>() != null).ToList();

                if (!properties.Any())
                {
                    errors.AppendLine($"Error: Class '{type.Name}' is marked as a Magic Table but has no properties.");
                    continue;
                }

                if (primaryKeyProps.Count == 0)
                {
                    errors.AppendLine($"Error: Class '{type.Name}' is marked as a Magic Table but has no primary key property. A primary key is required.");
                }
                else if (primaryKeyProps.Count > 1)
                {
                    errors.AppendLine($"Error: Class '{type.Name}' has multiple properties marked with MagicPrimaryKeyAttribute. Only one primary key is allowed.");
                }

                foreach (var prop in properties)
                {
                    var magicAttributes = new List<Type>
                {
                    typeof(MagicPrimaryKeyAttribute),
                    typeof(MagicIndexAttribute),
                    typeof(MagicNotMappedAttribute),
                    typeof(MagicNameAttribute),
                    typeof(MagicUniqueIndexAttribute)
                };

                    var appliedMagicAttributes = prop.GetCustomAttributes()
                                                     .Select(attr => attr.GetType())
                                                     .Where(attrType => magicAttributes.Contains(attrType))
                                                     .ToList();

                    if (appliedMagicAttributes.Count > 1)
                    {
                        errors.AppendLine($"Error: Property '{prop.Name}' in class '{type.Name}' has multiple Magic attributes ({string.Join(", ", appliedMagicAttributes.Select(a => a.Name))}). A property can have at most one Magic attribute.");
                    }
                }

                var instance = Activator.CreateInstance(type);
                var getCompoundKeyMethod = type.GetMethod("GetCompoundKey", BindingFlags.Public | BindingFlags.Instance);
                var getCompoundIndexesMethod = type.GetMethod("GetCompoundIndexes", BindingFlags.Public | BindingFlags.Instance);

                if (getCompoundKeyMethod == null || getCompoundIndexesMethod == null)
                {
                    errors.AppendLine($"Error: Class '{type.Name}' is missing required methods 'GetCompoundKey()' or 'GetCompoundIndexes()'.");
                }
                else
                {
                    try
                    {
                        // Call both methods and force any errors to surface
                        var compoundKey = getCompoundKeyMethod.Invoke(instance, null);
                        var compoundIndexes = getCompoundIndexesMethod.Invoke(instance, null);
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException != null)
                    {
                        // Extract and log the **actual** exception instead of the generic wrapper
                        errors.AppendLine($"Error: Class '{type.Name}' encountered an issue when calling 'GetCompoundKey()' or 'GetCompoundIndexes()'. Exception: {tie.InnerException.Message}");
                    }
                }
            }

            if (errors.Length > 0)
            {
                throw new Exception($"Magic Table Validation Errors:\n{errors}");
            }
        }
    }
}
