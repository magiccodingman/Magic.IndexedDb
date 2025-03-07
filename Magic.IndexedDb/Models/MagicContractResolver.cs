using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb.Models
{
    internal class MagicContractResolver<T> : JsonConverter<T>
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected StartObject token.");

            var record = Activator.CreateInstance<T>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return record;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected PropertyName token.");

                string jsonPropertyName = reader.GetString()!;
                if (!reader.Read())
                    throw new JsonException("Unexpected end of JSON.");

                string csharpPropertyName = PropertyMappingCache.GetCsharpPropertyName<T>(jsonPropertyName);

                var property = typeof(T).GetProperty(csharpPropertyName);
                if (property != null)
                {
                    var value = JsonSerializer.Deserialize(ref reader, property.PropertyType, options);
                    property.SetValue(record, value);
                }
                else
                {
                    reader.Skip();
                }
            }

            return record;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
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

            if (typeof(T) == typeof(string))
            {
                writer.WriteStringValue(value as string); // ✅ Directly write the string, no recursion
                return;
            }

            // Handle simple or primitive types directly
            if (IsSimpleType(typeof(T)))
            {
                JsonSerializer.Serialize(writer, value, options);
                return;
            }

            // Handle complex objects
            writer.WriteStartObject();

            foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                MagicPropertyEntry mpe = PropertyMappingCache.GetPropertyEntry<T>(property);

                if (mpe.NotMapped)
                    continue;

                if (property.GetIndexParameters().Length > 0)
                    continue; // Skip indexers entirely

                string jsPropertyName = mpe.JsPropertyName;

                object? propValue;
                try
                {
                    propValue = property.GetValue(value);
                }
                catch
                {
                    propValue = null;
                }

                string propertyName = options.PropertyNamingPolicy == JsonNamingPolicy.CamelCase
                    ? char.ToLowerInvariant(mpe.JsPropertyName[0]) + mpe.JsPropertyName.Substring(1) // Convert to camelCase
                    : mpe.JsPropertyName; // Keep original casing if camelCase is not set

                writer.WritePropertyName(propertyName);
                if (propValue == null)
                    writer.WriteNullValue();
                else
                    JsonSerializer.Serialize(writer, propValue, propValue.GetType(), options);
            }

            writer.WriteEndObject();
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
