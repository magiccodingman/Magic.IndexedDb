using Magic.IndexedDb.Helpers;
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
                return new TestResponse { Message = "Error: One or both input lists are null." };

            var correctList = correctResults.ToList();
            var testList = testResults.ToList();

            if (correctList.Count != testList.Count)
                return new TestResponse
                {
                    Success = false,  // ❗️ Ensure failure is registered
                    Message = $"List size mismatch:\nExpected count: {correctList.Count}\nActual count: {testList.Count}"
                };


            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(prop => !Attribute.IsDefined(prop, typeof(MagicNotMappedAttribute)))
                .ToArray();

            var failureDetails = new List<string>();

            for (int i = 0; i < correctList.Count; i++)
            {
                var expectedItem = correctList[i];
                var actualItem = testList[i];

                var propertyDifferences = CompareObjects(expectedItem, actualItem, properties, $"Item[{i}]");

                if (propertyDifferences.Any())
                {
                    failureDetails.Add($"Mismatch at index {i}:\n" + string.Join("\n", propertyDifferences));
                }
            }

            return failureDetails.Any()
                ? new TestResponse { Message = string.Join("\n\n", failureDetails) }
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
