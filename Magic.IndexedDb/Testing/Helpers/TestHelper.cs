using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using Magic.IndexedDb.Testing.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Testing.Helpers
{
    public static class TestValidator
    {
        public static TestResponse ValidateLists<T>(IEnumerable<T> correctResults, IEnumerable<T> testResults)
        {
            if (correctResults == null || testResults == null)
                return new TestResponse { Success = false, Message = "Error: One or both input lists are null." };

            var correctList = correctResults.ToList();
            var testList = testResults.ToList();

            if (correctList.Count != testList.Count)
                return new TestResponse
                {
                    Success = false,
                    Message = $"List size mismatch:\nExpected count: {correctList.Count}\nActual count: {testList.Count}"
                };

            // 🔑 Get the primary key property dynamically
            MagicPropertyEntry mpe = PropertyMappingCache.GetPrimaryKeyOfType(typeof(T));
            PropertyInfo primaryKeyProp = mpe.Property;

            if (primaryKeyProp == null)
                return new TestResponse { Success = false, Message = "Error: No primary key found for type." };

            var failureDetails = new List<string>();

            // Convert correct results into a dictionary for fast lookup by primary key
            var correctDictionary = correctList.ToDictionary(item => primaryKeyProp.GetValue(item));

            foreach (var actualItem in testList)
            {
                var actualKey = primaryKeyProp.GetValue(actualItem);

                if (actualKey == null || !correctDictionary.TryGetValue(actualKey, out var expectedItem))
                {
                    failureDetails.Add($"❌ No matching item found for Primary Key [{actualKey}].");
                    continue;
                }

                // Deeply compare the expected and actual item
                var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(prop => !Attribute.IsDefined(prop, typeof(MagicNotMappedAttribute)))
                    .ToArray();

                var propertyDifferences = CompareObjects(expectedItem, actualItem, properties, $"Item[{actualKey}]");

                if (propertyDifferences.Any())
                {
                    failureDetails.Add($"Mismatch in object with Primary Key [{actualKey}]:\n" + string.Join("\n", propertyDifferences));
                }
            }

            return failureDetails.Any()
                ? new TestResponse { Success = false, Message = string.Join("\n\n", failureDetails) }
                : new TestResponse { Success = true };
        }
        private static List<string> CompareObjects(object expected, object actual, PropertyInfo[] properties, string path)
        {
            var differences = new List<string>();

            foreach (var property in properties)
            {
                object expectedValue = property.GetValue(expected);
                object actualValue = property.GetValue(actual);

                if (expectedValue == null && actualValue == null)
                    continue; // Both are null, no difference.

                if ((expectedValue == null && actualValue != null) || (expectedValue != null && actualValue == null))
                {
                    differences.Add($"   - ❌ {path}.{property.Name}: Expected **[{expectedValue}]**, but got **[{actualValue}]** (null mismatch).");
                    continue;
                }

                if (!expectedValue.Equals(actualValue))
                {
                    if (PropertyMappingCache.IsComplexType(property.PropertyType))
                    {
                        var nestedProperties = property.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(prop => !Attribute.IsDefined(prop, typeof(MagicNotMappedAttribute)))
                            .ToArray();

                        var nestedDifferences = CompareObjects(expectedValue, actualValue, nestedProperties, $"{path}.{property.Name}");
                        differences.AddRange(nestedDifferences);
                    }
                    else
                    {
                        differences.Add($"   - ❌ {path}.{property.Name}: Expected **[{expectedValue}]**, but got **[{actualValue}]**.");
                    }
                }
            }

            return differences;
        }
    }
}
