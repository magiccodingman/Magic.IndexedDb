using System.Text;
using System.Text.Json.Nodes;

namespace Magic.IndexedDb.Cli;

internal sealed partial class MigrationWorkspace(string directory, string schemaId)
{
    internal string SchemaId { get; } = Protocol.Identifier(schemaId, "schemaId");
    internal string Root { get; } = Path.Combine(Path.GetFullPath(directory), Protocol.Identifier(schemaId, "schemaId"));
    private string HistoryPath => Path.Combine(Root, "history.json");
    private string PublishedLockPath => Path.Combine(Root, "published.lock.json");

    internal JsonObject Add(JsonObject model, string name, bool empty, string mode)
    {
        Protocol.Identifier(name, "migration name");
        if (mode is not ("atomic" or "resumable")) throw new CliException("MAGDB4001", "--mode must be atomic or resumable.", 2);
        using var writeLock = Lock();
        if (!File.Exists(HistoryPath))
        {
            if (empty) throw new CliException("MAGDB4201", "Create the initial schema before adding a data-only migration.");
            var history = new JsonObject
            {
                ["protocolVersion"] = 1, ["schemaId"] = SchemaId, ["targetRevision"] = 1,
                ["requiredCapabilities"] = new JsonArray(),
                ["schemas"] = new JsonArray(new JsonObject { ["revision"] = 1, ["path"] = "schemas/0001.json" }),
                ["migrations"] = new JsonArray(), ["baselines"] = new JsonArray()
            };
            WriteNew("schemas/0001.json", Protocol.Bytes(model));
            AtomicWrite(HistoryPath, Protocol.Bytes(history));
            var protectedModel = HasProtection(model);
            return Result("created", 1, protectedModel, protectedModel
                ? "Schema snapshot authored. This preview cannot validate or publish protected storage until an authenticated migration adapter is available; see the CLI guide."
                : "Initial schema snapshot created. New databases bootstrap directly at this revision.");
        }

        // Authoring a protected model is useful even while the preview runtime
        // cannot execute it. Only this source-generation path bypasses support checks.
        var previous = Validate(currentModel: null, allowPending: false, requireRuntimeSupport: false);
        if (Protocol.Equal(previous.TargetSchema, model) && !empty)
            return Result("unchanged", previous.TargetRevision, HasProtection(model), HasProtection(model)
                ? "No persisted model changes; revision unchanged. Protected storage remains unsupported for validation or publishing in this preview."
                : "No persisted model changes; revision unchanged.");
        var revision = checked(previous.TargetRevision + 1);
        var id = $"{revision:D4}_{name}";
        var operations = empty ? new JsonArray(MigrationPlanner.Manual("Add custom data operations before publishing this migration.", "dataOnly"))
            : MigrationPlanner.Diff(previous.TargetSchema, model);
        // A schema contract can change without physical changes, e.g. relaxing nullability.
        var migration = new JsonObject
        {
            ["protocolVersion"] = 1, ["schemaId"] = SchemaId, ["id"] = id,
            ["fromRevision"] = previous.TargetRevision, ["toRevision"] = revision,
            ["fromSchemaHash"] = Protocol.Hash(previous.TargetSchema), ["toSchemaHash"] = Protocol.Hash(model),
            ["mode"] = mode, ["requiredCapabilities"] = new JsonArray(), ["operations"] = operations
        };
        var schemaPath = $"schemas/{revision:D4}.json";
        var migrationPath = $"migrations/{id}.json";
        if (File.Exists(Protocol.SafePath(Root, schemaPath)) || File.Exists(Protocol.SafePath(Root, migrationPath)))
            throw new CliException("MAGDB4202", "Unreferenced revision artifacts already exist. Inspect them before retrying; no files have been overwritten.");
        var updated = ReadHistory();
        Protocol.Array(updated, "schemas").Add(new JsonObject { ["revision"] = revision, ["path"] = schemaPath });
        Protocol.Array(updated, "migrations").Add(new JsonObject { ["id"] = id, ["fromRevision"] = previous.TargetRevision, ["toRevision"] = revision, ["path"] = migrationPath });
        updated["targetRevision"] = revision;
        WriteNew(schemaPath, Protocol.Bytes(model));
        WriteNew(migrationPath, Protocol.Bytes(migration));
        AtomicWrite(HistoryPath, Protocol.Bytes(updated));
        var requiresReview = HasProtection(model) || operations.Any(node => Protocol.Text(Protocol.Object(node, "operation"), "op") == "manual") ||
            mode == "resumable" && operations.Any(node => !Protocol.DataOperations.Contains(Protocol.Text(Protocol.Object(node, "operation"), "op")));
        return Result("created", revision, requiresReview,
            HasProtection(model) ? "Draft created. Protected storage cannot be validated or published until an authenticated migration adapter is available; see the CLI guide." :
            requiresReview ? "Draft created. Resolve manual operations before validation or publishing." : "Migration created. Review and run migration tests before publishing.");
    }

    internal JsonObject AddBaseline(JsonObject legacyModel, int revision, string id, bool acknowledged)
    {
        if (!acknowledged) throw new CliException("MAGDB4203", "Baseline adoption requires --acknowledge-data-contract. Physical metadata cannot prove historical converter or business semantics; review that mapping first.");
        Protocol.Identifier(id, "baseline ID");
        using var writeLock = Lock();
        var package = Validate(null, false);
        if (!package.Schemas.TryGetValue(revision, out var managed)) throw new CliException("MAGDB4203", $"No managed schema revision {revision} exists.");
        RequireSupported(legacyModel, "legacy baseline");
        if (!Protocol.Equal(managed, legacyModel))
            throw new CliException("MAGDB4203", "Legacy baseline must exactly match the complete canonical schema at the selected managed revision, including fields, codecs, and numeric format. Acknowledgement cannot map different data contracts. Start history with the complete legacy model, then add explicit migrations.");
        var history = ReadHistory();
        var baselines = Protocol.Array(history, "baselines");
        if (baselines.Any(node => Protocol.Text(Protocol.Object(node, "baseline"), "id") == id))
            throw new CliException("MAGDB4203", $"Baseline '{id}' already exists.");
        foreach (var node in baselines)
        {
            var existing = Protocol.Object(node, "baseline");
            var reference = Protocol.Object(existing["schema"], "baseline schema reference");
            var definition = Protocol.Object(JsonNode.Parse(package.Assets[Protocol.Text(reference, "path")]), "baseline schema");
            if (Protocol.Equal(Protocol.PhysicalSchema(definition), Protocol.PhysicalSchema(legacyModel)))
                throw new CliException("MAGDB4203", $"Baseline '{id}' has the same physical layout as '{Protocol.Text(existing, "id")}', making browser adoption ambiguous. Keep one adoption baseline for that layout and migrate forward from its revision.");
        }
        var path = $"baselines/{id}.json";
        WriteNew(path, Protocol.Bytes(legacyModel));
        baselines.Add(new JsonObject
        {
            ["id"] = id, ["revision"] = revision,
            ["schema"] = new JsonObject { ["path"] = path }, ["acknowledgedDataContract"] = true
        });
        AtomicWrite(HistoryPath, Protocol.Bytes(history));
        return Result("baseline-created", package.TargetRevision, false, "Adoption plan recorded. The browser must validate the actual database before adoption; this command has not accessed browser data.");
    }

    internal Package Validate(JsonObject? currentModel, bool allowPending = false, bool requireRuntimeSupport = true)
    {
        if (requireRuntimeSupport && currentModel is not null) RequireSupported(currentModel, "current model");
        var history = ReadHistory();
        var target = Protocol.Integer(history, "targetRevision");
        var schemaEntries = Protocol.Array(history, "schemas");
        var migrationEntries = Protocol.Array(history, "migrations");
        var assets = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var schemas = new SortedDictionary<int, JsonObject>();
        var schemaReferences = new JsonArray();
        foreach (var node in schemaEntries)
        {
            var entry = Protocol.Object(node, "schema reference");
            var revision = Protocol.Integer(entry, "revision");
            var path = Protocol.Text(entry, "path");
            var model = Protocol.ValidateSchema(Protocol.ReadObject(Protocol.SafePath(Root, path)));
            if (requireRuntimeSupport) RequireSupported(model, $"schema revision {revision}");
            if (Protocol.Text(model, "schemaId") != SchemaId || !schemas.TryAdd(revision, model))
                throw new CliException("MAGDB4204", "Schema identity mismatch or duplicate revision in history.");
            AddAsset(assets, path, Protocol.Bytes(model));
            schemaReferences.Add(new JsonObject { ["revision"] = revision, ["path"] = path, ["sha256"] = Protocol.Hash(assets[path]) });
        }
        if (schemas.Count != target || !schemas.Keys.SequenceEqual(Enumerable.Range(1, target)))
            throw new CliException("MAGDB4204", "History requires every schema revision from 1 through targetRevision, without gaps or extra revisions.");
        if (currentModel is not null && !Protocol.Equal(schemas[target], currentModel))
            throw new CliException("MAGDB4205", $"Untracked persisted model change in '{SchemaId}'. Run migrations add with the current exporter/model before deploying.");

        if (migrationEntries.Count != target - 1) throw new CliException("MAGDB4204", "Migration history must contain exactly one ordered transition between each adjacent revision.");
        var fromRevisions = new HashSet<int>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var migrationReferences = new JsonArray();
        foreach (var node in migrationEntries.OrderBy(node => Protocol.Integer(Protocol.Object(node, "migration reference"), "fromRevision")))
        {
            var entry = Protocol.Object(node, "migration reference");
            var from = Protocol.Integer(entry, "fromRevision");
            var to = Protocol.Integer(entry, "toRevision");
            var id = Protocol.Identifier(Protocol.Text(entry, "id"), "migration ID");
            if (to != from + 1 || !schemas.ContainsKey(from) || !schemas.ContainsKey(to) || !fromRevisions.Add(from) || !ids.Add(id))
                throw new CliException("MAGDB4204", "Conflicting, duplicate or non-adjacent migration history.");
            var path = Protocol.Text(entry, "path");
            var document = Protocol.ReadObject(Protocol.SafePath(Root, path));
            Protocol.Version(document);
            if (Protocol.Text(document, "schemaId") != SchemaId || Protocol.Text(document, "id") != id ||
                Protocol.Integer(document, "fromRevision") != from || Protocol.Integer(document, "toRevision") != to ||
                Protocol.Text(document, "fromSchemaHash") != Protocol.Hash(schemas[from]) || Protocol.Text(document, "toSchemaHash") != Protocol.Hash(schemas[to]))
                throw new CliException("MAGDB4204", $"Migration '{id}' does not match its history entry or schema fingerprints.");
            ValidateOperations(document, schemas[from], schemas[to], path, assets, allowPending, requireRuntimeSupport);
            AddAsset(assets, path, Protocol.Bytes(document));
            migrationReferences.Add(new JsonObject
            {
                ["id"] = id, ["fromRevision"] = from, ["toRevision"] = to, ["path"] = path, ["sha256"] = Protocol.Hash(assets[path])
            });
        }
        var baselineReferences = new JsonArray();
        var baselineIds = new HashSet<string>(StringComparer.Ordinal);
        var baselineLayouts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Protocol.Array(history, "baselines"))
        {
            var entry = Protocol.Object(node, "baseline");
            var id = Protocol.Identifier(Protocol.Text(entry, "id"), "baseline ID");
            var revision = Protocol.Integer(entry, "revision");
            if (!baselineIds.Add(id) || !schemas.TryGetValue(revision, out var managed) || !Protocol.Boolean(entry, "acknowledgedDataContract"))
                throw new CliException("MAGDB4203", "Baseline is duplicated, references an unknown revision, or lacks acknowledgement of the data contract.");
            var reference = Protocol.Object(entry["schema"], "baseline schema reference");
            var path = Protocol.Text(reference, "path");
            var baseline = Protocol.ValidateSchema(Protocol.ReadObject(Protocol.SafePath(Root, path)));
            if (requireRuntimeSupport) RequireSupported(baseline, $"baseline '{id}'");
            if (!Protocol.Equal(baseline, managed))
                throw new CliException("MAGDB4203", $"Baseline '{id}' must exactly match the complete canonical schema at its selected managed revision, including persisted field contracts.");
            if (!baselineLayouts.Add(Protocol.Canonical(Protocol.PhysicalSchema(baseline))))
                throw new CliException("MAGDB4203", $"Baseline '{id}' duplicates another baseline's physical layout, making browser adoption ambiguous. Retain one adoption baseline for each physical layout.");
            AddAsset(assets, path, Protocol.Bytes(baseline));
            baselineReferences.Add(new JsonObject
            {
                ["id"] = id, ["revision"] = revision,
                ["schema"] = new JsonObject { ["path"] = path, ["sha256"] = Protocol.Hash(assets[path]) }
            });
        }
        var manifest = new JsonObject
        {
            ["protocolVersion"] = 1, ["schemaId"] = SchemaId, ["targetRevision"] = target,
            ["requiredCapabilities"] = history["requiredCapabilities"]?.DeepClone() ?? new JsonArray(),
            ["schemas"] = new JsonArray(schemaReferences.OrderBy(node => Protocol.Integer(Protocol.Object(node, "schema reference"), "revision")).Select(node => node!.DeepClone()).ToArray()),
            ["migrations"] = migrationReferences,
            ["baselines"] = new JsonArray(baselineReferences.OrderBy(node => Protocol.Text(Protocol.Object(node, "baseline"), "id"), StringComparer.Ordinal).Select(node => node!.DeepClone()).ToArray())
        };
        var recoveries = ValidateRecoveries(history, schemas, migrationReferences, assets);
        if (recoveries.Count != 0)
        {
            manifest["protocolVersion"] = 2;
            Protocol.Array(manifest, "requiredCapabilities").Add("resumableRepairV1");
            manifest["recoveries"] = recoveries;
        }
        VerifyPublishedLock(assets, manifest);
        return new(target, schemas[target], schemas, assets, manifest);
    }

    private void ValidateOperations(JsonObject migration, JsonObject source, JsonObject target, string path, SortedDictionary<string, byte[]> assets, bool allowPending, bool requireRuntimeSupport)
    {
        if (Protocol.Text(source, "numericFormat") != Protocol.Text(target, "numericFormat"))
            throw new CliException("MAGDB4206", "Protocol 1 cannot change numeric storage codecs. Preserve the existing numericFormat until a dedicated storage adapter is available.");
        var mode = Protocol.Text(migration, "mode");
        if (mode is not ("atomic" or "resumable")) throw new CliException("MAGDB4102", "Migration mode must be atomic or resumable.");
        var physical = MigrationPlanner.Named(Protocol.Array(Protocol.PhysicalSchema(source), "stores"));
        var pending = false;
        foreach (var node in Protocol.Array(migration, "operations"))
        {
            var operation = Protocol.Object(node, "operation");
            var op = Protocol.Text(operation, "op");
            if (op == "manual")
            {
                pending = true;
                if (!allowPending) throw new CliException("MAGDB4206", $"Migration '{Protocol.Text(migration, "id")}' requires review: {operation["reason"]?.GetValue<string>() ?? "unresolved manual operation"}");
                continue;
            }
            if (!Protocol.Operations.Contains(op)) throw new CliException("MAGDB4102", $"Unsupported migration operation '{op}'.");
            if (mode == "resumable" && !Protocol.DataOperations.Contains(op))
                throw new CliException("MAGDB4206", "Resumable migrations currently support data operations only. Separate structural changes into adjacent atomic migrations.");
            if (op == "createStore")
            {
                var definition = Protocol.Object(operation["definition"], "store definition");
                var checkedSchema = Protocol.ValidateSchema(new JsonObject { ["protocolVersion"] = 1, ["schemaId"] = SchemaId,
                    ["numericFormat"] = Protocol.Text(target, "numericFormat"), ["stores"] = new JsonArray(definition.DeepClone()) });
                if (requireRuntimeSupport) RequireSupported(checkedSchema, $"createStore in migration '{Protocol.Text(migration, "id")}'");
                var created = Protocol.Object(Protocol.Array(Protocol.PhysicalSchema(checkedSchema), "stores")[0], "store");
                if (!physical.TryAdd(Protocol.Text(created, "name"), created)) throw new CliException("MAGDB4207", "createStore targets an existing store.");
                continue;
            }
            var storeName = Protocol.Text(operation, "store");
            if (!physical.TryGetValue(storeName, out var store)) throw new CliException("MAGDB4207", $"Operation '{op}' references missing store '{storeName}'.");
            switch (op)
            {
                case "deleteStore":
                    RequireDataLoss(operation); physical.Remove(storeName); break;
                case "renameStore":
                    var destination = Protocol.Text(operation, "to");
                    Protocol.ValidateName(destination);
                    if (physical.ContainsKey(destination)) throw new CliException("MAGDB4207", "renameStore destination already exists.");
                    physical.Remove(storeName); store["name"] = destination; physical.Add(destination, store); break;
                case "createIndex":
                    var index = Protocol.Object(operation["index"], "index");
                    Protocol.ValidateKeyPath(index["keyPath"], false); Protocol.Boolean(index, "unique");
                    if (Protocol.Boolean(index, "multiEntry") && index["keyPath"] is JsonArray) throw new CliException("MAGDB4207", "A compound index cannot use multiEntry.");
                    var indexName = Protocol.Text(index, "name");
                    Protocol.ValidateName(indexName);
                    if (Protocol.Array(store, "indexes").Any(item => Protocol.Text(Protocol.Object(item, "index"), "name") == indexName)) throw new CliException("MAGDB4207", $"Index '{indexName}' already exists.");
                    Protocol.Array(store, "indexes").Add(Protocol.PhysicalIndex(index)); store["indexes"] = Protocol.SortNamed(Protocol.Array(store, "indexes")); break;
                case "deleteIndex":
                    var indexes = Protocol.Array(store, "indexes");
                    var existing = indexes.FirstOrDefault(item => Protocol.Text(Protocol.Object(item, "index"), "name") == Protocol.Text(operation, "name"));
                    if (existing is null) throw new CliException("MAGDB4207", "deleteIndex references an index that does not exist.");
                    indexes.Remove(existing); break;
                case "renameField":
                    ValidateField(Protocol.Text(operation, "from")); ValidateField(Protocol.Text(operation, "to")); break;
                case "setMissing":
                    ValidateField(Protocol.Text(operation, "path"));
                    if (!operation.ContainsKey("value")) throw new CliException("MAGDB4207", "setMissing requires an explicit value (which may be null).");
                    break;
                case "deleteField":
                    ValidateField(Protocol.Text(operation, "path")); RequireDataLoss(operation); break;
                case "transform":
                    var modulePath = Protocol.RelativeModule(path, Protocol.Text(operation, "module"));
                    if (!modulePath.EndsWith(".js", StringComparison.Ordinal) && !modulePath.EndsWith(".mjs", StringComparison.Ordinal))
                        throw new CliException("MAGDB4104", "Custom transform modules must be .js or .mjs files.");
                    var moduleBytes = File.ReadAllBytes(Protocol.SafePath(Root, modulePath));
                    Protocol.CheckHash(Protocol.Text(operation, "sha256"), moduleBytes, modulePath);
                    if (operation.ContainsKey("exportName")) Protocol.Text(operation, "exportName");
                    AddAsset(assets, modulePath, moduleBytes); break;
            }
        }
        if (!pending)
        {
            var expected = Protocol.PhysicalSchema(target);
            var actual = new JsonObject { ["stores"] = new JsonArray(physical.Values.Select(store => (JsonNode?)store.DeepClone()).ToArray()) };
            if (!Protocol.Equal(actual, expected)) throw new CliException("MAGDB4207", $"Migration '{Protocol.Text(migration, "id")}' does not produce its declared physical target schema.");
        }
    }

    internal string Publish(JsonObject? currentModel, string outputDirectory, bool seal = true)
    {
        using var writeLock = Lock();
        var package = Validate(currentModel);
        var output = Path.Combine(Path.GetFullPath(outputDirectory), SchemaId);
        if (output == Root || output.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new CliException("MAGDB4302", "Publish output must be separate from migration source.");
        var allAssets = new SortedDictionary<string, byte[]>(package.Assets, StringComparer.Ordinal) { ["manifest.json"] = Protocol.Bytes(package.Manifest) };
        // Check every existing output before creating anything. A release directory is immutable.
        foreach (var (path, bytes) in allAssets)
        {
            var target = Protocol.SafePath(output, path);
            if (File.Exists(target) && !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                throw new CliException("MAGDB4302", $"Published artifact '{path}' differs. Use a new release output directory; artifacts are immutable.");
        }
        foreach (var (path, bytes) in allAssets.Where(pair => pair.Key != "manifest.json")) WriteOutput(output, path, bytes);
        WriteOutput(output, "manifest.json", allAssets["manifest.json"]);
        if (seal)
        {
            var hashes = new JsonObject();
            foreach (var (path, bytes) in package.Assets) hashes[path] = Protocol.Hash(bytes);
            AtomicWrite(PublishedLockPath, Protocol.Bytes(new JsonObject { ["protocolVersion"] = 1, ["schemaId"] = SchemaId,
                ["assets"] = hashes, ["manifest"] = package.Manifest.DeepClone() }));
        }
        return Path.Combine(output, "manifest.json");
    }

    private void VerifyPublishedLock(SortedDictionary<string, byte[]> assets, JsonObject manifest)
    {
        if (!File.Exists(PublishedLockPath)) return;
        var published = Protocol.ReadObject(PublishedLockPath); Protocol.Version(published);
        if (Protocol.Text(published, "schemaId") != SchemaId) throw new CliException("MAGDB4301", "Published lock belongs to another schema.");
        foreach (var pair in Protocol.Object(published["assets"], "published assets"))
        {
            Protocol.SafePath(Root, pair.Key);
            if (!assets.TryGetValue(pair.Key, out var bytes)) throw new CliException("MAGDB4301", $"Previously published asset '{pair.Key}' was removed from the supported history.");
            if (pair.Value is not JsonValue value || !value.TryGetValue<string>(out var hash)) throw new CliException("MAGDB4301", "Invalid published asset hash.");
            Protocol.CheckHash(hash, bytes, pair.Key);
        }
        var previous = Protocol.Object(published["manifest"], "published manifest");
        if (Protocol.Integer(previous, "targetRevision") > Protocol.Integer(manifest, "targetRevision"))
            throw new CliException("MAGDB4301", "Published targetRevision cannot be rolled back.");
        foreach (var kind in new[] { "schemas", "migrations", "baselines", "recoveries" })
        {
            if (kind == "recoveries" && !previous.ContainsKey(kind)) continue;
            if (!manifest.ContainsKey(kind))
                throw new CliException("MAGDB4301", $"Previously published {kind} references cannot be removed.");
            var key = kind == "schemas" ? "revision" : "id";
            var current = Protocol.Array(manifest, kind);
            foreach (var node in Protocol.Array(previous, kind))
            {
                var old = Protocol.Object(node, "published reference");
                var match = current.FirstOrDefault(item => Protocol.Equal(Protocol.Object(item, "reference")[key], old[key]));
                if (match is null || !Protocol.Equal(match, old))
                    throw new CliException("MAGDB4301", $"Previously published {kind} reference '{old[key]}' was changed or removed.");
            }
        }
    }

    private JsonObject ReadHistory()
    {
        if (!File.Exists(HistoryPath)) throw new CliException("MAGDB4201", $"No history exists for '{SchemaId}'. Run migrations add Initial first.");
        var history = Protocol.ReadObject(HistoryPath); Protocol.Version(history);
        if (Protocol.Text(history, "schemaId") != SchemaId) throw new CliException("MAGDB4204", "History schemaId differs from its directory.");
        return history;
    }

    private FileStream Lock()
    {
        Directory.CreateDirectory(Root);
        try { return new FileStream(Path.Combine(Root, ".magicdb.write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new CliException("MAGDB4303", "Another tool process holds this migration source lock. Retry after it finishes."); }
    }
    private void WriteNew(string relative, byte[] bytes)
    {
        var path = Protocol.SafePath(Root, relative);
        if (File.Exists(path)) throw new CliException("MAGDB4302", $"Refusing to overwrite '{relative}'.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes); file.Flush(flushToDisk: true);
    }
    private static void WriteOutput(string root, string relative, byte[] bytes)
    {
        var path = Protocol.SafePath(root, relative);
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new CliException("MAGDB4302", $"Published artifact '{relative}' differs.");
            return;
        }
        AtomicWrite(path, bytes, overwrite: false);
    }
    internal static void AtomicWrite(string path, byte[] bytes, bool overwrite = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            try { File.Move(temporary, path, overwrite); }
            catch (IOException) when (!overwrite && File.Exists(path))
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new CliException("MAGDB4302", $"Concurrent publish produced different bytes for '{path}'.");
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void AddAsset(SortedDictionary<string, byte[]> assets, string path, byte[] bytes)
    {
        if (path == "manifest.json" || path == "history.json" || path == "published.lock.json") throw new CliException("MAGDB4104", "Reserved artifact path.");
        if (assets.TryGetValue(path, out var existing) && !existing.AsSpan().SequenceEqual(bytes)) throw new CliException("MAGDB4104", $"Asset path '{path}' has conflicting content.");
        assets[path] = bytes;
    }
    private static void RequireDataLoss(JsonObject operation)
    {
        if (!Protocol.Boolean(operation, "allowDataLoss", optional: true)) throw new CliException("MAGDB4206", "Destructive operations require allowDataLoss:true in the reviewed migration.");
    }
    private static void ValidateField(string field)
    {
        Protocol.ValidateName(field);
        if (field.Contains('.') || field is "__proto__" or "prototype" or "constructor") throw new CliException("MAGDB4207", $"Built-in transforms require a safe top-level field: '{field}'.");
    }
    private static bool HasProtection(JsonObject schema) => Protocol.Array(schema, "stores").Any(node =>
    {
        var store = Protocol.Object(node, "store");
        return store["encryptedFields"] is JsonArray { Count: > 0 } || store["exposedFields"] is not null;
    });
    private static void RequireSupported(JsonObject schema, string context)
    {
        if (HasProtection(schema))
            throw new CliException("MAGDB4208", $"Protected storage in {context} is unsupported by this preview: an authenticated migration adapter is not implemented. Export/list and migration draft authoring remain available, but validation, baseline adoption, testing, and publishing are blocked. See the CLI guide's protected-storage limitation.");
    }
    private JsonObject Result(string status, int revision, bool review, string message) => new()
    {
        ["schemaId"] = SchemaId, ["status"] = status, ["revision"] = revision, ["requiresReview"] = review, ["message"] = message,
        ["directory"] = Root
    };
    internal sealed record Package(int TargetRevision, JsonObject TargetSchema, SortedDictionary<int, JsonObject> Schemas, SortedDictionary<string, byte[]> Assets, JsonObject Manifest);
}
