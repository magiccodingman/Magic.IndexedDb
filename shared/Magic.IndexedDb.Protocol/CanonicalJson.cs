using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Magic.IndexedDb.Protocol;

// Linked into the runtime and standalone tool so fingerprints have one definition.
internal static class CanonicalJson
{
    /// <summary>Sorts object keys ordinally and preserves array order, matching the JavaScript engine.</summary>
    public static string Canonicalize(JsonElement element)
    {
        // Decode string tokens ourselves: JsonElement.GetString rejects lone UTF16
        // surrogates, but JSON.stringify intentionally preserves them as escapes.
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(element.GetRawText()));
        if (!reader.Read()) throw new JsonException("Empty JSON cannot be canonicalized.");
        var result = Write(ref reader);
        if (reader.Read()) throw new JsonException("Unexpected data after JSON value.");
        return result;
    }

    private static string Write(ref Utf8JsonReader reader)
    {
        var builder = new StringBuilder();
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected an object member.");
                    var name = DecodeString(reader.ValueSpan);
                    if (!reader.Read()) throw new JsonException("Expected an object value.");
                    if (!properties.TryAdd(name, Write(ref reader)))
                        throw new JsonException($"Duplicate JSON member '{name}' cannot be canonicalized.");
                }
                builder.Append('{');
                var first = true;
                foreach (var property in properties)
                {
                    if (!first) builder.Append(',');
                    first = false;
                    WriteString(builder, property.Key);
                    builder.Append(':');
                    builder.Append(property.Value);
                }
                builder.Append('}');
                return builder.ToString();
            case JsonTokenType.StartArray:
                builder.Append('[');
                var arrayFirst = true;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (!arrayFirst) builder.Append(',');
                    arrayFirst = false;
                    builder.Append(Write(ref reader));
                }
                builder.Append(']');
                return builder.ToString();
            case JsonTokenType.String:
                WriteString(builder, DecodeString(reader.ValueSpan));
                return builder.ToString();
            case JsonTokenType.Number:
                var number = reader.GetDouble();
                if (!double.IsFinite(number))
                    throw new JsonException("Nonfinite numbers cannot appear in schema JSON.");
                return FormatNumber(number);
            case JsonTokenType.True: return "true";
            case JsonTokenType.False: return "false";
            case JsonTokenType.Null: return "null";
            default: throw new JsonException("Undefined JSON cannot appear in a schema.");
        }
    }

    private static string DecodeString(ReadOnlySpan<byte> token)
    {
        var raw = Encoding.UTF8.GetString(token);
        if (!raw.Contains('\\')) return raw;
        var value = new StringBuilder();
        for (var index = 0; index < raw.Length; index++)
        {
            if (raw[index] != '\\') { value.Append(raw[index]); continue; }
            if (++index >= raw.Length) throw new JsonException("Incomplete JSON escape.");
            if (raw[index] == 'u')
            {
                if (index + 4 >= raw.Length || !ushort.TryParse(raw.AsSpan(index + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                    throw new JsonException("Invalid Unicode escape.");
                value.Append((char)code); index += 4;
            }
            else value.Append(raw[index] switch
            {
                '"' => '"', '\\' => '\\', '/' => '/', 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                _ => throw new JsonException("Invalid JSON escape.")
            });
        }
        return value.ToString();
    }

    // JSON.stringify leaves HTML, non-ASCII and line separators unescaped. Using
    // an explicit writer also avoids global block-list differences among .NET SDKs.
    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                    {
                        builder.Append(character).Append(value[++index]);
                    }
                    else if (character < 0x20 || char.IsSurrogate(character))
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else builder.Append(character);
                    break;
            }
        }
        builder.Append('"');
    }

    private static string FormatNumber(double number)
    {
        if (number == 0) return "0";
        var negative = number < 0;
        var text = Math.Abs(number).ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
        var split = text.Split('e');
        var mantissa = split[0];
        var exponent = split.Length == 2 ? int.Parse(split[1], CultureInfo.InvariantCulture) : 0;
        var decimalPosition = mantissa.IndexOf('.');
        if (decimalPosition < 0) decimalPosition = mantissa.Length;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal);
        var point = decimalPosition + exponent;
        while (digits.Length > 1 && digits[0] == '0') { digits = digits[1..]; point--; }
        var scientificExponent = point - 1;
        string result;
        if (scientificExponent >= -6 && scientificExponent < 21)
        {
            result = point <= 0 ? "0." + new string('0', -point) + digits :
                point >= digits.Length ? digits + new string('0', point - digits.Length) :
                digits[..point] + "." + digits[point..];
        }
        else
        {
            result = digits.Length == 1 ? digits : digits[0] + "." + digits[1..];
            result += "e" + (scientificExponent >= 0 ? "+" : "") + scientificExponent.ToString(CultureInfo.InvariantCulture);
        }
        return negative ? "-" + result : result;
    }
}
