using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Magic.IndexedDb.Protocol;

namespace Magic.IndexedDb.Migrations;

/// <summary>Deterministic portable schema serialization and fingerprints.</summary>
public static class MagicSchemaJson
{
    public static JsonSerializerOptions CreateSerializerOptions() => new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, CreateSerializerOptions());

    public static string Serialize<T>(T value) => Canonicalize(ToElement(value));

    public static string Fingerprint(MagicSchemaDocument schema) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(schema))));

    /// <summary>Sorts object keys ordinally and preserves array order, matching the JavaScript engine.</summary>
    public static string Canonicalize(JsonElement element)
    {
        try { return CanonicalJson.Canonicalize(element); }
        catch (JsonException error)
        {
            throw new MagicSchemaException("MAGDB3013", error.Message, error);
        }
    }
}
