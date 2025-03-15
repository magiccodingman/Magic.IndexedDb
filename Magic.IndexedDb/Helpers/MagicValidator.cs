using Magic.IndexedDb.SchemaAnnotations;
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

                // Validate that there's a primary key set.
                if (primaryKeyProps.Count == 0)
                {
                    errors.AppendLine($"Error: Class '{type.Name}' is marked as a Magic Table but has no primary key property. A primary key is required.");
                }
                else if (primaryKeyProps.Count > 1)
                {
                    errors.AppendLine($"Error: Class '{type.Name}' has multiple properties marked with MagicPrimaryKeyAttribute. Only one primary key is allowed.");
                }

                foreach (var prop in primaryKeyProps)
                {
                    var primaryKeyAttribute = prop.GetCustomAttribute<MagicPrimaryKeyAttribute>();

                    // Check if auto-increment is true, but type is not numeric
                    if (primaryKeyAttribute != null && primaryKeyAttribute.AutoIncrement)
                    {
                        if (!IsNumericType(prop.PropertyType)
                            //Future: Allow GUIDs as Primary Keys
                            // Update the comment to say numeric or guid when uncommented
                            //prop.PropertyType == typeof(Guid)
                            )
                        {
                            errors.AppendLine($"Error: Primary key '{prop.Name}' in class '{type.Name}' is marked as auto-increment, but its type '{prop.PropertyType.Name}' is not numeric.");
                        }
                    }

                    // Ensure primary key is NOT nullable
                    if (Nullable.GetUnderlyingType(prop.PropertyType) != null)
                    {
                        errors.AppendLine($"Error: Primary key '{prop.Name}' in class '{type.Name}' cannot be nullable.");
                    }
                }


                /*
                 * Prevent any Magic attribute to be 
                 * appended to more than one property. 
                 * This isn't allowed, only 1 magic 
                 * attribute per property!
                 */
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

                /*
                 * Run the GetCompoundKey and GetCompoundIndexes on each class 
                 * to enforce constructor to fire all validations.
                 */
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
                        var compoundIndexesObj = getCompoundIndexesMethod.Invoke(instance, null);

                        // Validate that compound indexes don't have duplicate indexes that overlap
                        var compoundIndexSets = new HashSet<string>();

                        // Ensure the returned object is a List<IMagicCompoundIndex>
                        if (compoundIndexesObj is List<IMagicCompoundIndex> compoundIndexes)
                        {
                            foreach (var index in compoundIndexes)
                            {
                                string indexKey = string.Join(",", index.ColumnNamesInCompoundIndex.OrderBy(x => x));

                                if (compoundIndexSets.Contains(indexKey))
                                {
                                    errors.AppendLine($"Error: Duplicate compound index detected in class '{type.Name}' with the same properties ({indexKey}).");
                                }
                                else
                                {
                                    compoundIndexSets.Add(indexKey);
                                }
                            }
                        }
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
