using Magic.IndexedDb.Models;
using System.Text.Json;

namespace Magic.IndexedDb.Helpers
{
    /// <summary>
    /// Helper to serialize between the Magic Library content to the JS. To communicate with Dexie.JS - 
    /// Note I left this public only to allow it to be targeted by external projects for testing.
    /// </summary>
    public static class MagicSerializationHelper
    {
        public static string SerializeObject<T>(T value, MagicJsonSerializationSettings? settings = null)
        {
            if (settings == null)
                settings = new MagicJsonSerializationSettings();

            if (value == null)
                throw new ArgumentNullException(nameof(value), "Object cannot be null");

            return JsonSerializer.Serialize(value, settings.Options);
        }

        public static T? DeserializeObject<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON cannot be null or empty.", nameof(json));

            return JsonSerializer.Deserialize<T>(json);
        }

        public static void PopulateObject<T>(T source, T target)
        {
            if (source == null || target == null)
                throw new ArgumentNullException("Source and target cannot be null");

            var json = JsonSerializer.Serialize(source);
            var deserialized = JsonSerializer.Deserialize<T>(json);

            foreach (var prop in typeof(T).GetProperties())
            {
                var value = prop.GetValue(deserialized);
                prop.SetValue(target, value);
            }
        }
    }


}
