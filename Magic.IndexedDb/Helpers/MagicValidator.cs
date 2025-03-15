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
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var magicTableClasses = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(SchemaHelper.HasMagicTableInterface) // Uses the helper method
                .ToList();


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
            }

            if (errors.Length > 0)
            {
                throw new Exception($"Magic Table Validation Errors:\n{errors}");
            }
        }
    }
}
