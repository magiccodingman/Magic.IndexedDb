using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Magic.IndexedDb.Helpers;

internal static class MagicNumericSerialization
{
    // IndexedDB can generate 2^53 itself, then fails rather than generating 2^53+1.
    internal const long MaxExactNativeInteger = 9_007_199_254_740_992;
    private static readonly ConcurrentDictionary<(PropertyInfo Property, Type Owner), bool> NativeKeys = new();

    internal static bool IsNativeAutoIncrementKey(PropertyInfo property, Type ownerType) =>
        NativeKeys.GetOrAdd((property, ownerType), pair =>
        {
            SchemaHelper.EnsureSchemaIsCached(pair.Owner);
            return SchemaHelper._schemaCache.TryGetValue(pair.Owner, out var table) &&
                   table?.GetKeys() is { AutoIncrement: true } keys &&
                   keys.PropertyInfos.Contains(pair.Property);
        });

    internal static bool IsLossless(JsonSerializerOptions options) =>
        options.Converters.Any(converter => converter is MagicNumericConverterFactory);

    internal static object? ValidateNativeKey(object? value, Type declaredType)
    {
        if (value is null) return null;
        var declared = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        var type = declared.IsEnum ? Enum.GetUnderlyingType(declared) : declared;
        if (type != typeof(long) && type != typeof(ulong) && type != typeof(decimal))
            return value;

        decimal number;
        try { number = Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
        catch (Exception error) when (error is OverflowException or InvalidCastException or FormatException)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An auto-increment key must be an exact integer within IndexedDB's key-generator range.");
        }
        if (number != decimal.Truncate(number) || number < -MaxExactNativeInteger || number > MaxExactNativeInteger ||
            (type == typeof(ulong) && number < 0))
            throw new ArgumentOutOfRangeException(nameof(value),
                "An auto-increment key must be an exact integer between -9007199254740992 and 9007199254740992. Use an explicit key for the full numeric range.");
        var converted = Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
        return declared.IsEnum ? Enum.ToObject(declared, converted) : converted;
    }

    internal static JsonElement SerializeNativeKey(object? value, Type declaredType) =>
        JsonSerializer.SerializeToElement(ValidateNativeKey(value, declaredType), declaredType);

    internal static object? ReadNativeKey(ref Utf8JsonReader reader, Type declaredType)
    {
        var value = JsonSerializer.Deserialize(ref reader, declaredType);
        return ValidateNativeKey(value, declaredType);
    }
}
