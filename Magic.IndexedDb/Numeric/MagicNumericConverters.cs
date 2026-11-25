using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Magic.IndexedDb.Helpers;

// Identifies Magic's known, order-preserving codecs separately from application converters.
internal interface IMagicNumericConverter;

internal sealed class MagicNumericConverterFactory : JsonConverterFactory, IMagicNumericConverter
{
    public override bool CanConvert(Type typeToConvert)
    {
        // An application type converter continues to own its wire representation.
        if (typeToConvert.IsDefined(typeof(JsonConverterAttribute), inherit: true)) return false;
        var type = typeToConvert.IsEnum ? Enum.GetUnderlyingType(typeToConvert) : typeToConvert;
        return type == typeof(long) || type == typeof(ulong) || type == typeof(decimal);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(long)) return new Int64Converter();
        if (typeToConvert == typeof(ulong)) return new UInt64Converter();
        if (typeToConvert == typeof(decimal)) return new DecimalConverter();
        return (JsonConverter)Activator.CreateInstance(typeof(WideEnumConverter<>).MakeGenericType(typeToConvert))!;
    }

    private static string Encoded(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a lossless numeric value. Numeric storage formats cannot be mixed; migrate or rebuild the database explicitly.");
        return reader.GetString()!;
    }

    private sealed class Int64Converter : JsonConverter<long>, IMagicNumericConverter
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            MagicNumericCodec.DecodeInt64(Encoded(ref reader));
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
            writer.WriteStringValue(MagicNumericCodec.Encode(value));
        // Dictionary keys are already JSON strings and never pass through Number.
        public override long ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            long.Parse(reader.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class UInt64Converter : JsonConverter<ulong>, IMagicNumericConverter
    {
        public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            MagicNumericCodec.DecodeUInt64(Encoded(ref reader));
        public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) =>
            writer.WriteStringValue(MagicNumericCodec.Encode(value));
        public override ulong ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ulong.Parse(reader.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class DecimalConverter : JsonConverter<decimal>, IMagicNumericConverter
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            MagicNumericCodec.DecodeDecimal(Encoded(ref reader));
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteStringValue(MagicNumericCodec.Encode(value));
        public override decimal ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            decimal.Parse(reader.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class WideEnumConverter<T> : JsonConverter<T>, IMagicNumericConverter where T : struct, Enum
    {
        private static readonly bool Signed = Enum.GetUnderlyingType(typeof(T)) == typeof(long);
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = Encoded(ref reader);
            return (T)(Signed
                ? Enum.ToObject(typeof(T), MagicNumericCodec.DecodeInt64(text))
                : Enum.ToObject(typeof(T), MagicNumericCodec.DecodeUInt64(text)));
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Signed
                ? MagicNumericCodec.Encode(Convert.ToInt64(value, CultureInfo.InvariantCulture))
                : MagicNumericCodec.Encode(Convert.ToUInt64(value, CultureInfo.InvariantCulture)));
        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Enum.Parse<T>(reader.GetString()!);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.ToString());
    }
}
