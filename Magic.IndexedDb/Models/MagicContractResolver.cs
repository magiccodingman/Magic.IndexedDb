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
using Microsoft.Extensions.Options;

namespace Magic.IndexedDb.Models
{
    internal class MagicContractResolver<T> : JsonConverter<T>
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return default; // ✅ Return default(T) if null is encountered

            // ✅ Handle root-level arrays correctly
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                if (!typeof(IEnumerable).IsAssignableFrom(typeToConvert))
                    throw new JsonException($"Expected an object but got an array for type {typeToConvert.Name}.");

                return (T?)ReadIEnumerable(ref reader, typeToConvert, options);
            }

            return (T?)ReadComplexObject(ref reader, typeToConvert, options);
        }

        /// <summary>
        /// Recursively reads a complex object while correctly mapping JSON properties to C# properties.
        /// </summary>
        private object? ReadComplexObject(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected StartObject token for type {type.Name}.");

            var instance = Activator.CreateInstance(type);
            var properties = PropertyMappingCache.GetTypeOfTProperties(type);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return instance;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected PropertyName token.");

                string jsonPropertyName = reader.GetString()!;
                if (!reader.Read())
                    throw new JsonException("Unexpected end of JSON.");

                // 🔥 Resolve correct C# property name dynamically
                string csharpPropertyName = PropertyMappingCache.GetCsharpPropertyName(jsonPropertyName, type);

                if (properties.TryGetValue(csharpPropertyName, out var mpe))
                {
                    if (mpe.NotMapped)
                    {
                        reader.Skip();
                        continue;
                    }

                    object? value = ReadPropertyValue(ref reader, mpe, options);
                    if (value != null)
                    {
                        try
                        {
                            mpe.Setter(instance, value);
                        }
                        catch
                        {
                            // Prevent hard crash but log if needed
                        }
                    }
                }
                else
                {
                    reader.Skip();
                }
            }

            return instance;
        }

        /// <summary>
        /// Reads and assigns a property value, detecting collections, simple types, and complex objects.
        /// </summary>
        private object? ReadPropertyValue(ref Utf8JsonReader reader, MagicPropertyEntry mpe, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            Type propertyType = mpe.Property.PropertyType;

            if (typeof(IEnumerable).IsAssignableFrom(propertyType) && propertyType != typeof(string))
            {
                return ReadIEnumerable(ref reader, propertyType, options);
            }

            if (PropertyMappingCache.IsComplexType(propertyType))
            {
                return ReadComplexObject(ref reader, propertyType, options);
            }

            return ReadSimpleType(ref reader, propertyType);
        }

        /// <summary>
        /// Reads primitive and simple types efficiently.
        /// </summary>
        private object? ReadSimpleType(ref Utf8JsonReader reader, Type type)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            return JsonSerializer.Deserialize(ref reader, type);
        }

        /// <summary>
        /// Reads a collection (List, Array, HashSet, etc.), keeping its structure intact.
        /// </summary>
        private object? ReadIEnumerable(ref Utf8JsonReader reader, Type collectionType, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"Expected StartArray token but got {reader.TokenType}.");

            // Determine the item type of the collection
            Type itemType = collectionType.IsArray
                ? collectionType.GetElementType()!
                : collectionType.GenericTypeArguments.FirstOrDefault() ?? typeof(object);

            var listType = typeof(List<>).MakeGenericType(itemType);
            var list = (IList)Activator.CreateInstance(listType)!;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;

                object? item;

                // 🔥 If it's a complex type, we need to deserialize it recursively
                if (PropertyMappingCache.IsComplexType(itemType))
                {
                    item = ReadComplexObject(ref reader, itemType, options);
                }
                else
                {
                    item = ReadSimpleType(ref reader, itemType);
                }

                list.Add(item);
            }

            // Convert to array if original type was an array
            if (collectionType.IsArray)
            {
                var array = Array.CreateInstance(itemType, list.Count);
                list.CopyTo(array, 0);
                return array;
            }

            return list;
        }



        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            var type = typeof(T);

            // Handle collections
            if (SerializeIEnumerable(writer, value, options))
            {
                return;
            }

            if (SerializeSimple(writer, value))
            {
                return;
            }


            var properties = PropertyMappingCache.GetTypeOfTProperties(type);

            // 🔥 Handle complex objects recursively
            writer.WriteStartObject();            
            SerializeComplexProperties(writer, value, properties, options);
            writer.WriteEndObject();
        }


        /// <summary>
        /// 🔥 Serializes primitive & simple types
        /// </summary>
        private bool SerializeSimple(Utf8JsonWriter writer, object value)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return true;
            }

            var type = value.GetType();

            // Handle simple or primitive types directly
            if (type == typeof(string) || PropertyMappingCache.IsSimpleType(type))
            {
                WriteSimpleType(writer, value);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 🔥 Serializes lists (IEnumerable)
        /// </summary>
        private bool SerializeIEnumerable(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return true;
            }

            var type = value.GetType();

            if (value is IEnumerable enumerable && type != typeof(string))
            {
                writer.WriteStartArray();
                foreach (var item in enumerable)
                {
                    if (SerializeSimple(writer, item))
                    {
                        continue;
                    }

                    if (item != null)
                    {
                        Type itemType = item.GetType();
                        if (PropertyMappingCache.IsComplexType(itemType))
                        {
                            var nestedProperties = PropertyMappingCache.GetTypeOfTProperties(itemType);
                            writer.WriteStartObject();
                            SerializeComplexProperties(writer, item, nestedProperties, options);
                            writer.WriteEndObject();
                        }
                        else
                        {
                            WriteSimpleType(writer, item);
                        }
                    }
                    else
                    {
                        writer.WriteNullValue();
                    }
                }
                writer.WriteEndArray();
                return true;
            }

            return false;
        }

        private void WriteSimpleType(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case string str:
                    writer.WriteStringValue(str);
                    break;
                case bool b:
                    writer.WriteBooleanValue(b);
                    break;
                case int i:
                    writer.WriteNumberValue(i);
                    break;
                case long l:
                    writer.WriteNumberValue(l);
                    break;
                case double d:
                    writer.WriteNumberValue(d);
                    break;
                case decimal dec:
                    writer.WriteNumberValue(dec);
                    break;
                case DateTime dt:
                    writer.WriteStringValue(dt.ToString("o")); // ISO format
                    break;
                case Guid guid:
                    writer.WriteStringValue(guid.ToString());
                    break;
                default:
                    JsonSerializer.Serialize(writer, value);
                    break;
            }
        }

        /// <summary>
        /// 🔥 Serializes complex objects recursively
        /// </summary>
        private void SerializeComplexProperties(Utf8JsonWriter writer, object value, Dictionary<string, MagicPropertyEntry> properties, JsonSerializerOptions options)
        {
            foreach (var (propertyName, mpe) in properties)
            {
                if (mpe.NotMapped) 
                    continue;

                object? propValue = null;
                try
                {
                    propValue = mpe.Getter(value);
                }
                catch
                {
                    propValue = null;
                }

                if (mpe.PrimaryKey && IsDefaultValue(propValue))
                {
                    continue;
                }

                string finalPropertyName = mpe.NeverCamelCase
                    ? mpe.JsPropertyName
                    : (options.PropertyNamingPolicy == JsonNamingPolicy.CamelCase
                        ? char.ToLowerInvariant(mpe.JsPropertyName[0]) + mpe.JsPropertyName.Substring(1)
                        : mpe.JsPropertyName);

                writer.WritePropertyName(finalPropertyName);

                if (SerializeIEnumerable(writer, propValue, options) || SerializeSimple(writer, propValue))
                {
                    continue;
                }

                if (propValue != null && mpe.IsComplexType)
                {
                    var nestedProps = PropertyMappingCache.GetTypeOfTProperties(propValue.GetType());
                    writer.WriteStartObject();
                    SerializeComplexProperties(writer, propValue, nestedProps, options);
                    writer.WriteEndObject();
                }
            }
        }

        private bool IsDefaultValue(object? value)
        {
            if (value == null) 
                return true;

            Type type = value.GetType();
            var isDefault = value.Equals(Activator.CreateInstance(type));
            return isDefault;
        }
    }
}
