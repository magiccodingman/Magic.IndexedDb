using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb.Helpers;

/// <summary>Shares converter selection between stored properties and query operands.</summary>
internal static class MagicConverterHelper
{
    private static readonly ConcurrentDictionary<PropertyInfo, JsonConverter?> PropertyConverters = new();
    private static readonly ConcurrentDictionary<Type, bool> AttributedTypes = new();

    internal static bool HasCustomConverter(Type type, JsonSerializerOptions options)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        // System.Text.Json lifts a value-type converter to Nullable<T>. Select
        // that same path before Magic attempts to materialize a complex object.
        return AttributedTypes.GetOrAdd(underlying,
                   candidate => candidate.IsDefined(typeof(JsonConverterAttribute), inherit: true)) ||
               options.Converters.Any(converter => !IsMagicResolver(converter) &&
                   (converter.CanConvert(type) || (underlying != type && converter.CanConvert(underlying))));
    }

    internal static bool HasCustomConverter(PropertyInfo property, JsonSerializerOptions? options = null) =>
        GetPropertyConverter(property) is not null || HasCustomConverter(property.PropertyType, options ?? new());

    internal static JsonSerializerOptions ForProperty(PropertyInfo property, JsonSerializerOptions options)
    {
        var converter = GetPropertyConverter(property);
        if (converter is null) return options;

        var selected = new JsonSerializerOptions(options);
        selected.Converters.Insert(0, converter);
        return selected;
    }

    internal static JsonElement SerializePropertyValue(PropertyInfo property, object? value,
        JsonSerializerOptions? options = null) =>
        JsonSerializer.SerializeToElement(value, property.PropertyType, ForProperty(property, options ?? new()));

    private static JsonConverter? GetPropertyConverter(PropertyInfo property) =>
        PropertyConverters.GetOrAdd(property, candidate =>
        {
            var attribute = candidate.GetCustomAttribute<JsonConverterAttribute>(inherit: true);
            if (attribute is null) return null;

            return attribute.ConverterType is { } converterType
                ? Activator.CreateInstance(converterType) as JsonConverter
                    ?? throw new InvalidOperationException($"The converter for '{candidate.Name}' must derive from JsonConverter.")
                : attribute.CreateConverter(candidate.PropertyType)
                    ?? throw new InvalidOperationException($"The converter attribute for '{candidate.Name}' did not create a converter.");
        });

    private static bool IsMagicResolver(JsonConverter converter) =>
        converter.GetType().IsGenericType &&
        converter.GetType().GetGenericTypeDefinition() == typeof(MagicContractResolver<>);
}
