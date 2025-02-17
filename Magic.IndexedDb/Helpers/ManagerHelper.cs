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
