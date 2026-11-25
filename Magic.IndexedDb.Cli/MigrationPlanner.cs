using System.Text.Json.Nodes;

namespace Magic.IndexedDb.Cli;

internal static class MigrationPlanner
{
    internal static JsonArray Diff(JsonObject before, JsonObject after)
    {
        var operations = new JsonArray();
        if (Protocol.Text(before, "numericFormat") != Protocol.Text(after, "numericFormat"))
            operations.Add(Manual("Protocol 1 cannot change numeric storage codecs. Preserve numericFormat until a dedicated storage adapter is available; previously rounded values cannot be recovered.", "numericFormat"));
        var oldStores = Named(Protocol.Array(before, "stores"));
        var newStores = Named(Protocol.Array(after, "stores"));
        foreach (var pair in oldStores.Where(pair => !newStores.ContainsKey(pair.Key)))
            operations.Add(Manual($"Store '{pair.Key}' was removed. Decide whether to retain/move its records or explicitly use deleteStore with allowDataLoss:true.", "deleteStore", pair.Key));
        foreach (var (name, store) in newStores)
        {
            if (!oldStores.TryGetValue(name, out var old))
            {
                operations.Add(new JsonObject { ["op"] = "createStore", ["definition"] = store.DeepClone() });
                continue;
            }
            if (!Protocol.Equal(old["keyPath"], store["keyPath"]) || !Protocol.Equal(old["autoIncrement"], store["autoIncrement"]))
                operations.Add(Manual("Primary key or auto-increment changed. Plan an explicit staged rebuild; automatic key conversion is unsupported.", "primaryKey", name));
            if (!ProtectionRules.SamePolicy(old, store))
            {
                var reprotect = new JsonObject { ["op"] = "reprotect", ["store"] = name };
                if (ProtectionRules.LosesProtection(old, store)) reprotect["allowProtectionLoss"] = false;
                operations.Add(reprotect);
            }

            var oldIndexes = Named(Protocol.Array(old, "indexes"));
            var newIndexes = Named(Protocol.Array(store, "indexes"));
            foreach (var index in oldIndexes)
                if (!newIndexes.TryGetValue(index.Key, out var next) || !Protocol.Equal(index.Value, next))
                    operations.Add(new JsonObject { ["op"] = "deleteIndex", ["store"] = name, ["name"] = index.Key });

            var oldFields = Named(Protocol.Array(old, "fields"));
            var newFields = Named(Protocol.Array(store, "fields"));
            foreach (var field in oldFields)
            {
                if (!newFields.TryGetValue(field.Key, out var next))
                    operations.Add(Manual($"Persisted field '{field.Key}' was removed. Declare a rename, or explicitly deleteField with allowDataLoss:true.", "fieldRemoved", name, field.Key));
                else if (!Protocol.Equal(field.Value, next) && !OnlyRelaxedNullability(field.Value, next))
                    operations.Add(Manual($"Persisted contract for '{field.Key}' changed. Supply a transformation or an explicitly reviewed data-only operation.", "fieldChanged", name, field.Key));
            }
            foreach (var field in newFields.Where(pair => !oldFields.ContainsKey(pair.Key)))
                if (!Protocol.Boolean(field.Value, "nullable"))
                    operations.Add(Manual($"Required field '{field.Key}' is new. Supply setMissing with an explicit value or a custom transform.", "backfill", name, field.Key));
            foreach (var index in newIndexes)
                if (!oldIndexes.TryGetValue(index.Key, out var previous) || !Protocol.Equal(index.Value, previous))
                    operations.Add(new JsonObject { ["op"] = "createIndex", ["store"] = name, ["index"] = index.Value.DeepClone() });
        }
        return operations;
    }

    private static bool OnlyRelaxedNullability(JsonObject before, JsonObject after)
    {
        if (Protocol.Boolean(before, "nullable") || !Protocol.Boolean(after, "nullable")) return false;
        var copy = Protocol.Clone(before); copy["nullable"] = true;
        return Protocol.Equal(copy, after);
    }

    internal static SortedDictionary<string, JsonObject> Named(JsonArray array)
    {
        var result = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in array) { var obj = Protocol.Object(node, "entry"); result.Add(Protocol.Text(obj, "name"), obj); }
        return result;
    }
    internal static JsonObject Manual(string reason, string change, string? store = null, string? field = null) => new()
    {
        ["op"] = "manual", ["reason"] = reason, ["change"] = change, ["store"] = store, ["field"] = field
    };
}
