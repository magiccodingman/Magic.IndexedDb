using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Magic.IndexedDb.Helpers
{
    /// <summary>
    /// Helper to serialize between the Magic Library content to the JS. To communicate with Dexie.JS - 
    /// Note I left this public only to allow it to be targeted by external projects for testing.
    /// </summary>
    public static class MagicSerializationHelper
    {

        public static object[] SerializeObjects(ITypedArgument[] objs, MagicJsonSerializationSettings? settings = null)
        {
            return objs.Select(arg => arg.SerializeToJsonElement(settings)).Cast<object>().ToArray();
        }

        public static string[] SerializeObjectsToString(ITypedArgument[] objs, MagicJsonSerializationSettings? settings = null)
        {
            return objs.Select(arg => arg.SerializeToJsonString(settings)).ToArray();
        }

        public static JsonElement SerializeObjectToJsonElement<T>(T value, MagicJsonSerializationSettings? settings = null)
        {
            if (settings == null)
                settings = new MagicJsonSerializationSettings();

            if (value == null)
                throw new ArgumentNullException(nameof(value), "Object cannot be null");

            var options = settings.GetOptionsWithResolver<T>(); // Ensure the correct resolver is applied
            string jsonString = JsonSerializer.Serialize(value, options); // Serialize using your settings

            // Convert the string to a JsonElement so that Blazor treats it as a structured object
            using JsonDocument doc = JsonDocument.Parse(jsonString);
            return doc.RootElement.Clone(); // Clone to prevent disposal issues
        }

        public static async Task SerializeObjectToStreamAsync<T>(StreamWriter writer, T value, MagicJsonSerializationSettings? settings = null)
        {
            if (settings == null)
                settings = new MagicJsonSerializationSettings();

            if (value == null)
                throw new ArgumentNullException(nameof(value), "Object cannot be null");

            var options = settings.GetOptionsWithResolver<T>();
            string jsonString = JsonSerializer.Serialize(value, options); // Use your serializer

            await writer.WriteAsync(jsonString);
            await writer.FlushAsync();
        }

        public static string SerializeObject<T>(T value, MagicJsonSerializationSettings? settings = null)
        {
            if (settings == null)
                settings = new MagicJsonSerializationSettings();

            if (value == null)
                throw new ArgumentNullException(nameof(value), "Object cannot be null");

            var options = settings.GetOptionsWithResolver<T>(); // Ensure the correct resolver is applied

            return JsonSerializer.Serialize(value, options);
        }

        public static T? DeserializeObject<T>(string json, MagicJsonSerializationSettings? settings = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON cannot be null or empty.", nameof(json));

            // if (settings == null)
            //    settings = new MagicJsonSerializationSettings();

            var options = settings.GetOptionsWithResolver<T>(); // Ensure correct resolver for deserialization

            return JsonSerializer.Deserialize<T>(json, options);
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
