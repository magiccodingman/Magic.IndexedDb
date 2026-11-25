using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Magic.IndexedDb.Protocol;

namespace Magic.IndexedDb.Cli;

internal static partial class Protocol
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal static readonly HashSet<string> Capabilities = ["atomic", "resumable", "legacyAdoption", "customTransforms"];
    internal static readonly HashSet<string> DataOperations = ["renameField", "setMissing", "deleteField", "transform"];
    internal static readonly HashSet<string> Operations = ["createStore", "deleteStore", "createIndex", "deleteIndex", "renameStore", "renameField", "setMissing", "deleteField", "transform"];
    private static readonly HashSet<string> DangerousMembers = ["__proto__", "prototype", "constructor"];

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    internal static string Identifier(string? value, string label)
    {
        if (value is null || !IdentifierPattern().IsMatch(value) || value is "." or "..")
            throw new CliException("MAGDB4101", $"{label} must contain 1–128 ASCII letters, digits, '.', '_' or '-', starting with a letter or digit.");
        return value;
    }

    internal static JsonObject ReadObject(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            // JsonNode accepts duplicate properties until enumeration. Explicitly reject
            // duplicates so the CLI and browser cannot disagree about a signed artifact.
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            RejectDuplicateProperties(document.RootElement);
            return JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject
                ?? throw new CliException("MAGDB4101", $"Expected a JSON object in '{path}'.");
        }
        catch (JsonException error) { throw new CliException("MAGDB4101", $"Invalid JSON in '{path}': {error.Message}"); }
    }

    internal static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new CliException("MAGDB4101", $"Duplicate JSON property '{property.Name}'.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    internal static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(Canonical(node));
    internal static string Canonical(JsonNode? node) => CanonicalJson.Canonicalize(JsonSerializer.SerializeToElement(node, JsonOptions));
    internal static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string Hash(JsonNode node) => Hash(Bytes(node));
    internal static JsonObject Clone(JsonObject value) => (JsonObject)value.DeepClone();
    internal static bool Equal(JsonNode? a, JsonNode? b) => Canonical(a) == Canonical(b);
    internal static string Text(JsonObject obj, string key) => obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text)
        ? text : throw new CliException("MAGDB4101", $"'{key}' must be a nonempty string.");
    internal static int Integer(JsonObject obj, string key, int minimum = 1) => obj[key] is JsonValue value && value.TryGetValue<int>(out var number) && number >= minimum
        ? number : throw new CliException("MAGDB4101", $"'{key}' must be an integer >= {minimum}.");
    internal static JsonArray Array(JsonObject obj, string key) => obj[key] as JsonArray ?? throw new CliException("MAGDB4101", $"'{key}' must be an array.");
    internal static JsonObject Object(JsonNode? node, string label) => node as JsonObject ?? throw new CliException("MAGDB4101", $"{label} must be an object.");
    internal static bool Boolean(JsonObject obj, string key, bool optional = false)
    {
        if (!obj.ContainsKey(key) && optional) return false;
        return obj[key] is JsonValue value && value.TryGetValue<bool>(out var boolean) ? boolean
            : throw new CliException("MAGDB4101", $"'{key}' must be a boolean.");
    }

    internal static void Version(JsonObject obj)
    {
        if (Integer(obj, "protocolVersion") != 1) throw new CliException("MAGDB4102", "Unsupported protocolVersion. This tool supports protocol 1.");
        if (obj.TryGetPropertyValue("requiredCapabilities", out var node))
        {
            if (node is not JsonArray capabilities) throw new CliException("MAGDB4102", "requiredCapabilities must be an array.");
            foreach (var item in capabilities)
                if (item is not JsonValue value || !value.TryGetValue<string>(out var name) || !Capabilities.Contains(name))
                    throw new CliException("MAGDB4102", $"Unsupported required capability: {item?.ToJsonString()}.");
        }
    }

    internal static JsonObject ValidateSchema(JsonObject input)
    {
        var schema = Clone(input);
        Version(schema);
        Identifier(Text(schema, "schemaId"), "schemaId");
        var format = Text(schema, "numericFormat");
        if (format is not ("legacy" or "lossless-v1")) throw new CliException("MAGDB4102", $"Unsupported numericFormat '{format}'.");
        schema["requiredCapabilities"] ??= new JsonArray();
        var stores = Array(schema, "stores");
        var storeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in stores)
        {
            var store = Object(node, "store");
            var name = Text(store, "name");
            ValidateName(name);
            if (!storeNames.Add(name))
                throw new CliException("MAGDB4103", $"Reserved or duplicate store '{name}'.");
            foreach (var property in new[] { "encryptedFields", "exposedFields" })
                if (store[property] is { } protection && (protection is not JsonArray members || members.Any(member => member is not JsonValue value || !value.TryGetValue<string>(out _))))
                    throw new CliException("MAGDB4103", $"'{property}' must be null or an array of persisted field names.");
            ValidateKeyPath(store["keyPath"], allowNull: true);
            if (Boolean(store, "autoIncrement") && store["keyPath"] is JsonArray)
                throw new CliException("MAGDB4103", $"Compound primary key on '{name}' cannot auto-increment.");
            var indexes = Array(store, "indexes");
            var indexNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in indexes)
            {
                var index = Object(entry, "index");
                var indexName = Text(index, "name"); ValidateName(indexName);
                if (!indexNames.Add(indexName)) throw new CliException("MAGDB4103", $"Duplicate index in '{name}'.");
                ValidateKeyPath(index["keyPath"], allowNull: false);
                Boolean(index, "unique");
                if (Boolean(index, "multiEntry") && index["keyPath"] is JsonArray)
                    throw new CliException("MAGDB4103", "A compound index cannot use multiEntry.");
            }
            var fields = Array(store, "fields");
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in fields)
            {
                var field = Object(entry, "field");
                var fieldName = Text(field, "name"); ValidateName(fieldName);
                if (!fieldNames.Add(fieldName)) throw new CliException("MAGDB4103", $"Duplicate field in '{name}'.");
                Text(field, "type"); Text(field, "codec"); Boolean(field, "nullable");
            }
            store["indexes"] = SortNamed(indexes);
            store["fields"] = SortNamed(fields);
        }
        schema["stores"] = SortNamed(stores);
        return schema;
    }

    internal static JsonArray SortNamed(JsonArray values) => new(values.Select(item => Object(item, "entry"))
        .OrderBy(item => Text(item, "name"), StringComparer.Ordinal).Select(item => (JsonNode?)item.DeepClone()).ToArray());

    internal static void ValidateKeyPath(JsonNode? value, bool allowNull)
    {
        if (value is null && allowNull) return;
        static bool Valid(string text) => text.Length > 0 && text.Split('.').All(part => part.Length > 0 && !DangerousMembers.Contains(part));
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && Valid(text)) return;
        if (value is JsonArray array && array.Count > 0 && array.All(item => item is JsonValue part && part.TryGetValue<string>(out var path) && Valid(path))) return;
        throw new CliException("MAGDB4103", "keyPath must be a nonempty string, a nonempty ordered array of strings, or null for an out-of-line primary key.");
    }

    internal static void ValidateName(string name)
    {
        if (name.Length == 0 || name.StartsWith("__magic_", StringComparison.Ordinal) || DangerousMembers.Contains(name))
            throw new CliException("MAGDB4103", $"Invalid or reserved persisted name '{name}'.");
    }

    internal static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':') || relative.Contains('?') || relative.Contains('#') || relative.Contains('%'))
            throw new CliException("MAGDB4104", $"Invalid portable relative asset path '{relative}'.");
        var segments = relative.Split('/');
        if (segments.Any(segment => segment is "" or "." or "..")) throw new CliException("MAGDB4104", $"Asset path traversal is not allowed: '{relative}'.");
        var basePath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(basePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new CliException("MAGDB4104", "Asset is outside its schema directory.");
        // A symlink inside the source must not read files outside the source tree.
        var current = Path.GetFullPath(root);
        if (Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            throw new CliException("MAGDB4104", "Schema source/output directories cannot be symbolic links.");
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new CliException("MAGDB4104", $"Symbolic links are not supported in migration assets: '{relative}'.");
        }
        return result;
    }

    internal static string RelativeModule(string migrationPath, string module)
    {
        if (string.IsNullOrWhiteSpace(module) || Path.IsPathRooted(module) || module.Contains('\\') || module.Contains(':') || module.Contains('?') || module.Contains('#') || module.Contains('%'))
            throw new CliException("MAGDB4104", "Custom modules must use relative paths within the schema source directory.");
        var parts = migrationPath.Split('/').SkipLast(1).ToList();
        foreach (var part in module.Split('/'))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new CliException("MAGDB4104", "Custom module escapes its schema directory.");
                parts.RemoveAt(parts.Count - 1);
            }
            else if (part.Length == 0) throw new CliException("MAGDB4104", "Custom modules cannot have empty path segments.");
            else parts.Add(part);
        }
        if (parts.Count == 0) throw new CliException("MAGDB4104", "Custom module must name a file.");
        return string.Join('/', parts);
    }

    internal static void CheckHash(string expected, byte[] bytes, string asset)
    {
        if (!HashPattern().IsMatch(expected) || expected != Hash(bytes))
            throw new CliException("MAGDB4301", $"Hash mismatch for '{asset}'. Expected {expected}; actual {Hash(bytes)}.");
    }

    internal static JsonObject PhysicalSchema(JsonObject schema) => new()
    {
        ["stores"] = new JsonArray(Array(schema, "stores").Select(item =>
        {
            var store = Object(item, "store");
            return (JsonNode?)new JsonObject
            {
                ["name"] = Text(store, "name"), ["keyPath"] = store["keyPath"]?.DeepClone(),
                ["autoIncrement"] = Boolean(store, "autoIncrement"),
                ["indexes"] = new JsonArray(Array(store, "indexes").Select(index => (JsonNode?)PhysicalIndex(Object(index, "index"))).ToArray())
            };
        }).ToArray())
    };

    internal static JsonObject PhysicalIndex(JsonObject index) => new()
    {
        ["name"] = Text(index, "name"), ["keyPath"] = index["keyPath"]!.DeepClone(),
        ["unique"] = Boolean(index, "unique"), ["multiEntry"] = Boolean(index, "multiEntry")
    };
}
