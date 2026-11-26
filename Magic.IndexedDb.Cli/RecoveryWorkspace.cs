using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Magic.IndexedDb.Cli;

internal sealed partial class MigrationWorkspace
{
    internal JsonObject AddRecovery(int revision, string id, string inputModule)
    {
        Protocol.Identifier(id, "recovery ID");
        if (Path.GetExtension(inputModule) is not (".js" or ".mjs"))
            throw new CliException("MAGDB4210", "The recovery module must be a .js or .mjs file.");
        using var writeLock = Lock();
        var package = Validate(null);
        var history = ReadHistory();
        if (!history.ContainsKey("recoveries")) history["recoveries"] = new JsonArray();
        var entries = Protocol.Array(history, "recoveries");
        if (entries.Any(node =>
        {
            var entry = Protocol.Object(node, "recovery binding");
            return Protocol.Integer(entry, "fromRevision") == revision || Protocol.Text(entry, "id") == id;
        }))
            throw new CliException("MAGDB4210", "Each edge and recovery ID can be approved only once.");

        var edge = RecoveryEdge(Protocol.Array(package.Manifest, "migrations"), revision);
        var operation = RecoveryOperation(edge, package.Schemas, package.Assets);
        var module = File.ReadAllBytes(inputModule);
        var path = $"recoveries/{id}/recovery.json";
        var modulePath = $"recoveries/{id}/repair.mjs";
        var document = DescribeRecovery(id, edge, package.Schemas, operation, "repair.mjs", Protocol.Hash(module));
        if (File.Exists(Protocol.SafePath(Root, path)) || File.Exists(Protocol.SafePath(Root, modulePath)))
            throw new CliException("MAGDB4302", "Recovery source already exists. Refusing to overwrite it.");

        WriteNew(modulePath, module);
        WriteNew(path, Protocol.Bytes(document));
        entries.Add(new JsonObject { ["id"] = id, ["fromRevision"] = revision, ["path"] = path });
        AtomicWrite(HistoryPath, Protocol.Bytes(history));
        return new JsonObject { ["status"] = "recovery-added", ["id"] = id, ["fromRevision"] = revision, ["path"] = path };
    }

    private JsonArray ValidateRecoveries(JsonObject history, SortedDictionary<int, JsonObject> schemas,
        JsonArray edges, SortedDictionary<string, byte[]> assets)
    {
        var references = new JsonArray();
        if (!history.ContainsKey("recoveries")) return references;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var revisions = new HashSet<int>();
        foreach (var item in Protocol.Array(history, "recoveries"))
        {
            var entry = Protocol.Object(item, "recovery binding");
            var id = Protocol.Identifier(Protocol.Text(entry, "id"), "recovery ID");
            var revision = Protocol.Integer(entry, "fromRevision");
            if (entry.Count != 3 || !ids.Add(id) || !revisions.Add(revision))
                throw new CliException("MAGDB4210", "Invalid or duplicate recovery binding.");
            var edge = RecoveryEdge(edges, revision);
            var original = RecoveryOperation(edge, schemas, assets);
            var path = Protocol.Text(entry, "path");
            var document = Protocol.ReadObject(Protocol.SafePath(Root, path));
            var module = Protocol.Object(document["module"], "recovery module");
            var moduleName = Protocol.Text(module, "path");
            if (!Regex.IsMatch(moduleName, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\.m?js\z", RegexOptions.CultureInvariant))
                throw new CliException("MAGDB4210", "Recovery requires a sibling JavaScript module.");
            var hash = Protocol.Text(module, "sha256");
            if (!Protocol.Equal(document, DescribeRecovery(id, edge, schemas, original, moduleName, hash)))
                throw new CliException("MAGDB4210", "Recovery document does not match its bound edge, models or operation.");

            var modulePath = Protocol.RelativeModule(path, moduleName);
            var bytes = File.ReadAllBytes(Protocol.SafePath(Root, modulePath));
            Protocol.CheckHash(hash, bytes, modulePath);
            AddAsset(assets, modulePath, bytes);
            AddAsset(assets, path, Protocol.Bytes(document));
            references.Add(new JsonObject { ["id"] = id, ["fromRevision"] = revision, ["path"] = path,
                ["sha256"] = Protocol.Hash(assets[path]) });
        }
        return references;
    }

    private static JsonObject RecoveryEdge(JsonArray edges, int revision)
        => edges.Select(node => Protocol.Object(node, "migration reference"))
            .SingleOrDefault(edge => Protocol.Integer(edge, "fromRevision") == revision)
            ?? throw new CliException("MAGDB4210", "Recovery must identify an existing adjacent migration edge.");

    private static JsonObject RecoveryOperation(JsonObject edge, SortedDictionary<int, JsonObject> schemas,
        SortedDictionary<string, byte[]> assets)
    {
        var migration = Protocol.Object(JsonNode.Parse(assets[Protocol.Text(edge, "path")]), "original migration");
        var operations = Protocol.Array(migration, "operations");
        if (Protocol.Text(migration, "mode") != "resumable" || operations.Count != 1 ||
            operations[0] is not JsonObject operation || Protocol.Text(operation, "op") != "transform")
            throw new CliException("MAGDB4210", "Recovery requires one resumable custom transform.");
        var from = schemas[Protocol.Integer(edge, "fromRevision")];
        var to = schemas[Protocol.Integer(edge, "toRevision")];
        RequireSupported(from, "original source model");
        RequireSupported(to, "original target model");
        if (!Protocol.Equal(Protocol.PhysicalSchema(from), Protocol.PhysicalSchema(to)) ||
            Protocol.Text(from, "numericFormat") != Protocol.Text(to, "numericFormat"))
            throw new CliException("MAGDB4210", "Recovery cannot change native layout or numeric format.");
        var storeName = Protocol.Text(operation, "store");
        foreach (var node in Protocol.Array(from, "stores"))
        {
            var previous = Protocol.Object(node, "source store");
            var next = Protocol.Array(to, "stores").Select(candidate => Protocol.Object(candidate, "target store"))
                .Single(candidate => Protocol.Text(candidate, "name") == Protocol.Text(previous, "name"));
            if (Protocol.Text(previous, "name") != storeName)
            {
                if (!Protocol.Equal(previous, next))
                    throw new CliException("MAGDB4210", "Recovery cannot change unrelated store contracts.");
            }
            else
            {
                if (!Protocol.Equal(previous["recordCodec"], next["recordCodec"]))
                    throw new CliException("MAGDB4210", "Recovery cannot change the store codec.");
                CheckRecoveryFields(Protocol.Array(previous, "fields"), Protocol.Array(next, "fields"));
            }
        }
        return operation;
    }

    private static void CheckRecoveryFields(JsonArray previous, JsonArray next)
    {
        foreach (var node in previous)
        {
            var field = Protocol.Object(node, "source field");
            var target = next.Select(item => Protocol.Object(item, "target field"))
                .FirstOrDefault(item => Protocol.Text(item, "name") == Protocol.Text(field, "name"));
            if (target is null) continue;
            var left = (JsonObject)field.DeepClone();
            var right = (JsonObject)target.DeepClone();
            left.Remove("properties");
            right.Remove("properties");
            if (!Protocol.Equal(left, right))
                throw new CliException("MAGDB4210", "Recovery cannot change an existing field representation.");
            CheckRecoveryFields(field["properties"] as JsonArray ?? new JsonArray(),
                target["properties"] as JsonArray ?? new JsonArray());
        }
    }

    private JsonObject DescribeRecovery(string id, JsonObject edge, SortedDictionary<int, JsonObject> schemas,
        JsonObject original, string module, string hash)
    {
        var from = Protocol.Integer(edge, "fromRevision");
        var to = Protocol.Integer(edge, "toRevision");
        return new JsonObject
        {
            ["protocolVersion"] = 2, ["id"] = id, ["schemaId"] = SchemaId,
            ["source"] = new JsonObject { ["revision"] = from, ["schemaHash"] = Protocol.Hash(schemas[from]),
                ["migrationId"] = Protocol.Text(edge, "id"), ["migrationHash"] = Protocol.Text(edge, "sha256") },
            ["target"] = new JsonObject { ["revision"] = to, ["schemaHash"] = Protocol.Hash(schemas[to]) },
            ["operation"] = new JsonObject { ["index"] = 0, ["store"] = Protocol.Text(original, "store") },
            ["module"] = new JsonObject { ["path"] = module, ["sha256"] = hash }
        };
    }
}
