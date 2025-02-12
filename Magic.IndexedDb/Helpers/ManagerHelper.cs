using Magic.IndexedDb.SchemaAnnotations;
using System.Reflection;
using System.Text.Json;

namespace Magic.IndexedDb.Helpers
{
    public static class ManagerHelper
    {
        public static Dictionary<string, object?> ConvertPropertyNamesUsingMappings(
            Dictionary<string, object?> inputRecord, 
            Dictionary<string, string> propertyMappings)
        {
            var updatedRecord = new Dictionary<string, object?>();
            foreach (var kvp in inputRecord)
            {
                var targetKey = propertyMappings.FirstOrDefault(x => x.Value == kvp.Key).Key;
                if (targetKey != null)
                {
                    updatedRecord[targetKey] = kvp.Value;
                }
                else
                {
                    updatedRecord[kvp.Key] = kvp.Value;
                }
            }
            return updatedRecord;
        }

        public static Dictionary<string, object?> ConvertRecordToDictionary<TRecord>(TRecord record)
        {
            var propertyMappings = GeneratePropertyMapping<TRecord>();
            var dictionary = new Dictionary<string, object?>();
            var properties = typeof(TRecord).GetProperties();

            foreach (var property in properties)
            {
                var value = property.GetValue(record);
                var columnName = property.Name;

                if (propertyMappings.ContainsValue(property.Name))
                {
                    columnName = propertyMappings.First(kvp => kvp.Value == property.Name).Key;
                }

                dictionary[columnName] = value!;
            }

            return dictionary;
        }

        public static Dictionary<string, string> GeneratePropertyMapping<TRecord>()
        {
            var propertyMappings = new Dictionary<string, string>();
            var properties = typeof(TRecord).GetProperties();
            foreach (var property in properties)
            {
                var indexDbAttr = property.GetCustomAttribute<MagicIndexAttribute>();
                var uniqueIndexDbAttr = property.GetCustomAttribute<MagicUniqueIndexAttribute>();
                var primaryKeyDbAttr = property.GetCustomAttribute<MagicPrimaryKeyAttribute>();

                var columnName = property.Name;
                if (indexDbAttr != null)
                    columnName = property.GetPropertyColumnName<MagicIndexAttribute>();
                else if (uniqueIndexDbAttr != null)
                    columnName = property.GetPropertyColumnName<MagicUniqueIndexAttribute>();
                else if (primaryKeyDbAttr != null)
                    columnName = property.GetPropertyColumnName<MagicPrimaryKeyAttribute>();

                propertyMappings[columnName] = property.Name;
            }
            return propertyMappings;
        }

        public static object? GetValueFromValueKind(object value, Type type)
        {
            if (value is JsonElement jsonElement)
            {
                return jsonElement.ValueKind switch
                {
                    JsonValueKind.Number => jsonElement.GetDecimal(),
                    JsonValueKind.String => jsonElement.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => jsonElement.Deserialize(type)
                };
            }

            return value;
        }
    }
}
