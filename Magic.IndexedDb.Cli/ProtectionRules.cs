using System.Text.Json.Nodes;

namespace Magic.IndexedDb.Cli;

/// <summary>Portable protection rules shared by authoring and publication validation.</summary>
internal static class ProtectionRules
{
    internal const string Capability = "authenticatedMigrationsV1";

    internal static bool HasProtection(JsonObject schema) => Protocol.Array(schema, "stores")
        .Select(node => Protocol.Object(node, "store")).Any(ProtectsRecords);

    internal static bool ProtectsRecords(JsonObject store) =>
        Members(store, "encryptedFields").Count != 0 || store["exposedFields"] is not null;

    internal static bool HasCapability(JsonObject document) => document["requiredCapabilities"] is JsonArray capabilities &&
        capabilities.Any(item => item is JsonValue value && value.TryGetValue<string>(out var name) && name == Capability);

    internal static bool SamePolicy(JsonObject before, JsonObject after) =>
        Members(before, "encryptedFields").SetEquals(Members(after, "encryptedFields")) &&
        (before["exposedFields"] is null) == (after["exposedFields"] is null) &&
        Members(before, "exposedFields").SetEquals(Members(after, "exposedFields"));

    internal static bool LosesProtection(JsonObject before, JsonObject after)
    {
        var oldExposed = before["exposedFields"] is not null;
        var newExposed = after["exposedFields"] is not null;
        if (oldExposed)
            // Whole-record protection includes persisted properties not declared by
            // the current model. A finite field list cannot preserve that promise.
            return !newExposed || !Members(after, "exposedFields").IsSubsetOf(Members(before, "exposedFields"));
        var oldFields = Members(before, "encryptedFields");
        return newExposed ? oldFields.Overlaps(Members(after, "exposedFields"))
            : !oldFields.IsSubsetOf(Members(after, "encryptedFields"));
    }

    internal static void ValidateSchema(JsonObject schema, string context)
    {
        foreach (var node in Protocol.Array(schema, "stores"))
        {
            var store = Protocol.Object(node, "store");
            var fields = Members(store, "encryptedFields");
            var exposed = store["exposedFields"] is null ? null : Members(store, "exposedFields");
            if (exposed is not null && fields.Overlaps(exposed))
                throw Invalid(context, "A member cannot be both encrypted and publicly exposed.");
            if (fields.Count == 0 && exposed is null) continue;
            if (Protocol.Boolean(store, "autoIncrement") || store["keyPath"] is null)
                throw Invalid(context, "Protected stores require an explicit inline primary key.");
            if (fields.Count != 0 && exposed is null && store["recordCodec"] is not null)
                throw Invalid(context, "Custom record codecs require whole-record protection instead of field-only protection.");
            foreach (var path in KeyPaths(store["keyPath"]).Concat(Protocol.Array(store, "indexes")
                         .SelectMany(index => KeyPaths(Protocol.Object(index, "index")["keyPath"]))))
            {
                if (path.Contains('.') || fields.Contains(path) || exposed is not null && !exposed.Contains(path))
                    throw Invalid(context, "Primary-key and index members must remain public top-level fields under the declared protection policy.");
            }
        }
    }

    private static HashSet<string> Members(JsonObject store, string property)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (store[property] is null) return result;
        foreach (var node in Protocol.Array(store, property))
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out var member) ||
                string.IsNullOrEmpty(member) || member.Contains('.') || member.StartsWith("__magic_", StringComparison.Ordinal) ||
                member is "__proto__" or "prototype" or "constructor" || !result.Add(member))
                throw Invalid("schema", "Protection members must be unique, safe top-level persisted field names.");
        }
        return result;
    }

    private static IEnumerable<string> KeyPaths(JsonNode? node) => node switch
    {
        JsonValue value => [value.GetValue<string>()],
        JsonArray array => array.Select(item => item!.GetValue<string>()),
        _ => []
    };

    private static CliException Invalid(string context, string message) =>
        new("MAGDB4208", $"Invalid protected storage in {context}: {message}");
}
