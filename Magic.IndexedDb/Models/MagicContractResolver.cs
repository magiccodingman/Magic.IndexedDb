using System.Collections;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Helpers;

namespace Magic.IndexedDb.Models;

internal class MagicContractResolver<T> : JsonConverter<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // ✅ Return default(T) if null is encountered
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        // ✅ Handle primitive types before assuming it's complex
        if (PropertyMappingCache.IsSimpleType(typeToConvert))
        {
            return (T?)ReadSimpleType(ref reader, typeToConvert, options);
        }

        // Dictionaries are JSON objects, even though they also implement IEnumerable.
        // Let System.Text.Json handle its supported key contracts and object values.
        if (IsDictionaryType(typeToConvert))
            return (T?)DeserializePassthrough(ref reader, typeToConvert, options);

        // ✅ Explicitly check if the type is `JsonElement`
        if (typeToConvert == typeof(JsonElement))
        {
            JsonElement element = JsonSerializer.Deserialize<JsonElement>(ref reader); // Extract JsonElement

            // ✅ Re-run null check for JsonElement
            if (IsSimpleJsonNull(element))
                return default;

            // ✅ Re-run primitive check for JsonElement
            if (IsSimpleJsonElement(element))
                return (T?)(object)element; // 🚀 Directly cast JsonElement to T
        }

        // ✅ Handle root-level arrays correctly
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            if (!typeof(IEnumerable).IsAssignableFrom(typeToConvert))
                throw new JsonException($"Expected an object but got an array for type {typeToConvert.Name}.");

            return (T?)ReadIEnumerable(ref reader, typeToConvert, options);
        }

        // ✅ If it's neither a primitive nor an array, assume it's a complex object
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            return (T?)ReadComplexObject(ref reader, typeToConvert, options);
        }

        // ✅ Return default(T) if EndArray is encountered
        if (reader.TokenType == JsonTokenType.EndArray)
        {
            return default;
        }

        throw new JsonException($"Unexpected JSON token: {reader.TokenType} when deserializing {typeToConvert.Name}.");
    }

    private object CreateObjectFromDictionary(Type type, Dictionary<string, object?> propertyValues, SearchPropEntry search)
    {
        object obj;
        if (search.Constructor is { } constructor && search.HasConstructorParameters)
        {
            var parameters = constructor.GetParameters();
            var constructorArgs = new object?[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                if (parameter.Name != null && propertyValues.TryGetValue(parameter.Name, out var value))
                {
                    constructorArgs[index] = value;
                    propertyValues.Remove(parameter.Name);
                }
                else if (parameter.HasDefaultValue)
                {
                    constructorArgs[index] = parameter.DefaultValue;
                }
                else
                {
                    constructorArgs[index] = GetDefaultValue(parameter.ParameterType);
                }
            }

            obj = search.InstanceCreator(constructorArgs)
                ?? throw new InvalidOperationException($"Failed to create instance of type {type.Name}.");
        }
        else
        {
            obj = search.InstanceCreator(Array.Empty<object?>())
                ?? throw new InvalidOperationException($"Failed to create instance of type {type.Name}.");
        }

        // Constructor-bound values and mutable properties are intentionally handled
        // together so immutable and hybrid models both materialize completely.
        foreach (var (propName, value) in propertyValues)
        {
            if (search.propertyEntries.TryGetValue(propName, out var propEntry))
            {
                propEntry.Setter(obj, value);
            }
        }
        return obj;
    }

    private bool IsSimpleJsonElement(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String ||
               element.ValueKind == JsonValueKind.Number ||
               element.ValueKind == JsonValueKind.True ||
               element.ValueKind == JsonValueKind.False;
    }
    private bool IsSimpleJsonNull(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Null;
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

        // 🔥 Step 1: Create a dictionary to store extracted values
        var propertyValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var properties = PropertyMappingCache.GetTypeOfTProperties(type);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                // 🔥 Step 3: Convert the dictionary into the final object
                var result = CreateObjectFromDictionary(type, propertyValues, properties);
                return result;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected PropertyName token.");

            string jsonPropertyName = reader.GetString()!;
            if (!reader.Read())
                throw new JsonException("Unexpected end of JSON.");


            string csharpPropertyName = properties.GetCsharpPropertyName(jsonPropertyName);

            //string csharpPropertyName = PropertyMappingCache.GetCsharpPropertyName(jsonPropertyName, type);

            if (properties.propertyEntries.TryGetValue(csharpPropertyName, out var mpe))
            {
                if (mpe.NotMapped)
                {
                    reader.Skip();
                    continue;
                }

                // Read-only properties are still valid when they bind to the selected
                // constructor. Only skip them when there is no constructor parameter.
                var isConstructorBound = properties.ConstructorParameterMappings
                    .ContainsKey(csharpPropertyName);
                if (mpe.Property.DeclaringType?.IsInterface == true ||
                    (!mpe.Property.CanWrite && !isConstructorBound))
                {
                    reader.Skip();
                    continue;
                }

                try
                {
                    object? value = ReadPropertyValue(ref reader, mpe, options);
                    propertyValues[csharpPropertyName] = value;
                }
                catch (Exception ex) when (ex is not JsonException)
                {
                    throw new JsonException(
                        $"Could not read JSON property '{jsonPropertyName}' as {mpe.Property.PropertyType.FullName} on {type.FullName}.",
                        ex);
                }
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException("Unexpected end of JSON while reading an object.");
    }

    private static readonly ConcurrentDictionary<Type, object?> _defaultValues = new();

    public static object? GetDefaultValue(Type type)
    {
        return _defaultValues.GetOrAdd(type, t => t.IsValueType ? Activator.CreateInstance(t) : null);
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

        if (mpe.IsComplexType)
        {
            return ReadComplexObject(ref reader, propertyType, options);
        }

        return ReadSimpleType(ref reader, propertyType, options);
    }

    /// <summary>
    /// Reads primitive and simple types efficiently.
    /// </summary>
    private object? ReadSimpleType(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return DeserializePassthrough(ref reader, type, options);
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

            if (typeof(IEnumerable).IsAssignableFrom(itemType) && itemType != typeof(string))
            {
                item = ReadIEnumerable(ref reader, itemType, options);
            }
            // 🔥 If it's a complex type, we need to deserialize it recursively
            else if (PropertyMappingCache.IsComplexType(itemType))
            {
                item = ReadComplexObject(ref reader, itemType, options);
            }
            else
            {
                item = ReadSimpleType(ref reader, itemType, options);
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

        if (collectionType.IsAssignableFrom(listType))
            return list;

        var enumerableType = typeof(IEnumerable<>).MakeGenericType(itemType);
        var enumerableConstructor = collectionType.GetConstructor([enumerableType]);
        if (enumerableConstructor != null)
            return enumerableConstructor.Invoke([list]);

        var hashSetType = typeof(HashSet<>).MakeGenericType(itemType);
        if (collectionType.IsAssignableFrom(hashSetType))
            return Activator.CreateInstance(hashSetType, list);

        throw new JsonException(
            $"Collection type {collectionType.FullName} cannot be restored from a JSON array.");
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

        if (SerializeSimple(writer, value, options))
        {
            return;
        }


        var properties = PropertyMappingCache.GetTypeOfTProperties(type);

        // 🔥 Handle complex objects recursively
        writer.WriteStartObject();
        SerializeComplexProperties(writer, value, properties.propertyEntries, options);
        writer.WriteEndObject();
    }


    /// <summary>
    /// 🔥 Serializes primitive & simple types
    /// </summary>
    private bool SerializeSimple(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
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
            WriteSimpleType(writer, value, options);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 🔥 Serializes lists (IEnumerable)
    /// </summary>
    private bool SerializeIEnumerable(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return true;
        }

        var type = value.GetType();

        if (IsDictionaryType(type))
        {
            JsonSerializer.Serialize(writer, value, type, GetPassthroughOptions(type, options));
            return true;
        }

        if (value is IEnumerable enumerable && type != typeof(string))
        {
            writer.WriteStartArray();
            foreach (var item in enumerable)
            {
                if (SerializeSimple(writer, item, options))
                {
                    continue;
                }

                if (item is IEnumerable nestedEnumerable && item is not string)
                {
                    SerializeIEnumerable(writer, nestedEnumerable, options);
                    continue;
                }

                if (item != null)
                {
                    Type itemType = item.GetType();
                    if (PropertyMappingCache.IsComplexType(itemType))
                    {
                        var nestedProperties = PropertyMappingCache.GetTypeOfTProperties(itemType);
                        writer.WriteStartObject();
                        SerializeComplexProperties(writer, item, nestedProperties.propertyEntries, options);
                        writer.WriteEndObject();
                    }
                    else
                    {
                        WriteSimpleType(writer, item, options);
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

    private void WriteSimpleType(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        JsonSerializer.Serialize(writer, value, type, GetPassthroughOptions(type, options));
    }

    /// <summary>
    /// 🔥 Serializes complex objects recursively
    /// </summary>
    private void SerializeComplexProperties(Utf8JsonWriter writer, object value, Dictionary<string, MagicPropertyEntry> properties, JsonSerializerOptions options)
    {
        var type = value.GetType();
        var cache = PropertyMappingCache.GetTypeOfTProperties(type);

        foreach (var (propertyName, mpe) in properties)
        {
            if (mpe.NotMapped)
                continue;

            // 💡 Handle constructor-only properties by using reflection
            object? propValue = null;

            try
            {
                propValue = mpe.Getter(value);
            }
            catch
            {
                // If it's a constructor-only param with no backing field or getter, ignore
                continue;
            }

            // Skip default primary key if needed
            if (mpe.PrimaryKey && IsDefaultValue(propValue, mpe))
                continue;

            // Figure out the actual output property name
            string finalPropertyName = mpe.NeverCamelCase
                ? mpe.JsPropertyName
                : (options.PropertyNamingPolicy == JsonNamingPolicy.CamelCase
                    ? char.ToLowerInvariant(mpe.JsPropertyName[0]) + mpe.JsPropertyName.Substring(1)
                    : mpe.JsPropertyName);

            writer.WritePropertyName(finalPropertyName);

            // Handle primitives/collections
            if (SerializeIEnumerable(writer, propValue, options) || SerializeSimple(writer, propValue, options))
                continue;

            // Handle complex types
            if (propValue != null && mpe.IsComplexType)
            {
                var nestedProps = PropertyMappingCache.GetTypeOfTProperties(propValue.GetType());
                writer.WriteStartObject();
                SerializeComplexProperties(writer, propValue, nestedProps.propertyEntries, options);
                writer.WriteEndObject();
            }
        }
    }

    private bool IsDefaultValue(object? value, MagicPropertyEntry mpe)
    {
        if (value == null)
            return true;

        return value.Equals(mpe.DefaultValue); // ✅ Use precomputed default value
    }

    private object? DeserializePassthrough(
        ref Utf8JsonReader reader,
        Type type,
        JsonSerializerOptions options)
    {
        return JsonSerializer.Deserialize(ref reader, type, GetPassthroughOptions(type, options));
    }

    private JsonSerializerOptions GetPassthroughOptions(Type type, JsonSerializerOptions options)
    {
        if (type != typeof(T))
            return options;

        var passthroughOptions = new JsonSerializerOptions(options);
        for (var index = passthroughOptions.Converters.Count - 1; index >= 0; index--)
        {
            if (passthroughOptions.Converters[index] is MagicContractResolver<T>)
                passthroughOptions.Converters.RemoveAt(index);
        }

        return passthroughOptions;
    }

    private static bool IsDictionaryType(Type type)
    {
        return type.GetInterfaces()
            .Prepend(type)
            .Any(candidate => candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>));
    }
}
