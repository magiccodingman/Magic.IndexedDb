using Magic.IndexedDb.SchemaAnnotations;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Collections;

namespace Magic.IndexedDb.Models
{
    internal class MagicContractResolver<T> : JsonConverter<T>
    {
        private static readonly ConcurrentDictionary<MemberInfo, bool> _cachedIgnoredProperties = new();

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Defer deserialization back to built-in handling to ensure correctness
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            // Handle simple or primitive types directly
            if (IsSimpleType(typeof(T)))
            {
                JsonSerializer.Serialize(writer, value, options);
                return;
            }

            // Handle collections
            if (value is IEnumerable enumerable && typeof(T) != typeof(string))
            {
                writer.WriteStartArray();
                foreach (var item in enumerable)
                {
                    if (item == null)
                        writer.WriteNullValue();
                    else
                        JsonSerializer.Serialize(writer, item, item.GetType(), options);
                }
                writer.WriteEndArray();
                return;
            }

            // Handle complex objects
            writer.WriteStartObject();
            foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (ShouldIgnoreProperty(property))
                    continue;

                var propName = options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;

                if (property.GetIndexParameters().Length > 0)
                    continue; // Skip indexers entirely

                object? propValue;
                try
                {
                    propValue = property.GetValue(value);
                }
                catch
                {
                    // Fallback: write null if getting property fails
                    propValue = null;
                }

                writer.WritePropertyName(propName);
                if (propValue == null)
                    writer.WriteNullValue();
                else
                    JsonSerializer.Serialize(writer, propValue, propValue.GetType(), options);
            }
            writer.WriteEndObject();
        }

        private static bool ShouldIgnoreProperty(PropertyInfo property)
        {
            return _cachedIgnoredProperties.GetOrAdd(property, prop =>
                prop.GetCustomAttributes(inherit: true)
                    .Any(a => a.GetType().FullName == typeof(MagicNotMappedAttribute).FullName));
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive ||
                   type.IsEnum ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(DateTimeOffset) ||
                   type == typeof(Guid) ||
                   type == typeof(Uri) ||
                   type == typeof(TimeSpan);
        }
    }

}
