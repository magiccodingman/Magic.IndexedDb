using System.Reflection;
using System.Text.Json;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using TestBase.Models;

namespace TestBase.Helpers;

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

        // 🔑 Retrieve primary key properties
        List<MagicPropertyEntry> primaryKeys = PropertyMappingCache.GetPrimaryKeysOfType(typeof(T))
            .Where(pk => pk.PrimaryKey)
            .ToList();

        if (!primaryKeys.Any())
            return new TestResponse { Success = false, Message = "Error: No primary keys found for type." };

        var failureDetails = new List<string>();

        // 🔍 Convert correct results into a dictionary for fast lookup by **property name**
        var correctDictionary = correctList.ToDictionary(
            item => primaryKeys.ToDictionary(
                pk => pk.Property.DeclaringType.FullName + "." + pk.Property.Name,  // **Use fully qualified property name**
                pk => pk.Property.GetValue(item)
            ),
            item => item
        );

        foreach (var actualItem in testList)
        {
            // Extract key properties from the actual item
            var actualKeyValues = primaryKeys
                .Select(pk => (Property: pk.Property.Name, Value: pk.Property.GetValue(actualItem)))
                .ToList();

            // Find matching item in correctList using key properties
            var expectedItem = correctList.FirstOrDefault(correctItem =>
                primaryKeys.All(pk =>
                    Equals(pk.Property.GetValue(correctItem), pk.Property.GetValue(actualItem))
                )
            );

            if (expectedItem == null)
            {
                failureDetails.Add($"❌ No matching item found for Primary Key [{FormatKey(actualKeyValues)}].");
                continue;
            }

            // **Deeply compare object properties**
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(prop => !Attribute.IsDefined(prop, typeof(MagicNotMappedAttribute)))
                .ToArray();

            var propertyDifferences = CompareObjects(expectedItem, actualItem, properties, $"Item[{FormatKey(actualKeyValues)}]");

            if (propertyDifferences.Any())
            {
                failureDetails.Add($"Mismatch in object with Primary Key [{FormatKey(actualKeyValues)}]:\n" + string.Join("\n", propertyDifferences));
            }
        }

        return failureDetails.Any()
            ? new TestResponse { Success = false, Message = string.Join("\n\n", failureDetails) }
            : new TestResponse { Success = true };
    }

    private static string FormatKey(List<(string Property, object Value)> keyValues)
    {
        if (keyValues == null || keyValues.Count == 0)
            return "NULL";

        return string.Join(" | ", keyValues
            .OrderBy(kv => kv.Property) // Ensure stable order for comparison
            .Select(kv => $"{kv.Property}={kv.Value?.ToString() ?? "NULL"}"));
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

            Type propType = property.PropertyType;

            // 🔥 Special case: if it's object or anonymous, compare via JSON string
            if (propType == typeof(object) || IsAnonymousType(expectedValue.GetType()) || IsAnonymousType(actualValue.GetType()))
            {
                var expectedJson = JsonSerializer.Serialize(expectedValue);
                var actualJson = JsonSerializer.Serialize(actualValue);

                if (expectedJson != actualJson)
                {
                    differences.Add($"   - ❌ {path}.{property.Name}: Expected JSON **[{expectedJson}]**, but got **[{actualJson}]**.");
                }

                continue;
            }

            // 💡 Deep compare if complex
            if (!expectedValue.Equals(actualValue))
            {
                if (PropertyMappingCache.IsComplexType(propType))
                {
                    var nestedProperties = propType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
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
    private static bool IsAnonymousType(Type type)
    {
        return Attribute.IsDefined(type, typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))
               && type.IsGenericType
               && type.Name.Contains("AnonymousType")
               && (type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))
               && type.Namespace == null;
    }
}