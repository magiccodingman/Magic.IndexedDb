using Magic.IndexedDb.SchemaAnnotations;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace Magic.IndexedDb.Models
{
    internal class MagicContractResolver<T> : JsonConverter<T>
    {
        private static readonly ConcurrentDictionary<MemberInfo, bool> _cachedIgnoredProperties = new();

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null) return;

            writer.WriteStartObject();

            foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // Check cache first
                if (!_cachedIgnoredProperties.TryGetValue(property, out bool shouldIgnore))
                {
                    shouldIgnore = property.GetCustomAttributes(inherit: true)
                                           .Any(a => a.GetType().FullName == typeof(MagicNotMappedAttribute).FullName);
                    _cachedIgnoredProperties[property] = shouldIgnore;
                }

                if (!shouldIgnore)
                {
                    var propValue = property.GetValue(value);
                    var propName = options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
                    writer.WritePropertyName(propName);
                    JsonSerializer.Serialize(writer, propValue, property.PropertyType, options);
                }
            }

            writer.WriteEndObject();
        }
    }
}
