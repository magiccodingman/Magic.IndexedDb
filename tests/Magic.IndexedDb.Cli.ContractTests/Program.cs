using Magic.IndexedDb.Cli;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// A separate process verifies the runner contract. This deliberately exercises
// malformed/empty reports; it does not pretend these fixtures are browser tests.
if (args is ["--manifest", var manifestPath])
{
    var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
    var schemaPath = manifest["schemas"]!.AsArray().Last()!["path"]!.GetValue<string>();
    var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(manifestPath)!, schemaPath)))!;
    switch (schema["runnerFixture"]?.GetValue<string>())
    {
        case "zero": Console.WriteLine("{\"testsExecuted\":0,\"failed\":0}"); return 0;
        case "failure": Console.WriteLine("{\"testsExecuted\":1,\"failed\":1}"); return 0;
        case "malformed": Console.WriteLine("not a JSON report"); return 0;
        default: return 1;
    }
}

var tests = new List<(string Name, Func<Task> Run)>();

Test("initial bootstrap contains one schema and no historical migration", async f =>
{
    await f.Initial();
    var history = f.History();
    Check(history["targetRevision"]!.GetValue<int>() == 1 && history["schemas"]!.AsArray().Count == 1 && history["migrations"]!.AsArray().Count == 0);
});
Test("unchanged model does not increment revision", async f =>
{
    await f.Initial(); await f.Ok("migrations", "add", "NoChange"); Check(f.History()["targetRevision"]!.GetValue<int>() == 1);
});
Test("index addition increments only this history and validates", async f =>
{
    await f.Initial(); f.AddIndex(); await f.Ok("migrations", "add", "AddName"); await f.Ok("migrations", "validate");
    Check(f.History()["targetRevision"]!.GetValue<int>() == 2);
});
Test("current model drift fails validation", async f =>
{
    await f.Initial(); f.AddIndex(); await f.Fail("MAGDB4205", "migrations", "validate");
});
Test("required field addition creates actionable draft and blocks publish", async f =>
{
    await f.Initial(); f.AddField("status", false); await f.Code(3, "migrations", "add", "Status");
    await f.Fail("MAGDB4206", "migrations", "validate"); Check(f.History()["targetRevision"]!.GetValue<int>() == 2);
});
Test("optional field addition requires no invented backfill", async f =>
{
    await f.Initial(); f.AddField("notes", true); await f.Ok("migrations", "add", "Notes"); await f.Ok("migrations", "validate");
});
Test("destructive drop is unresolved until explicitly authorized", async f =>
{
    await f.Initial(); f.Model["stores"] = new JsonArray(); f.Save(); await f.Code(3, "migrations", "add", "DropItems");
    f.ChangeMigration(m => m["operations"] = new JsonArray(new JsonObject { ["op"] = "deleteStore", ["store"] = "items" }));
    await f.Fail("MAGDB4206", "migrations", "validate");
    f.ChangeMigration(m => m["operations"]![0]!["allowDataLoss"] = true); await f.Ok("migrations", "validate");
});
Test("physical plan cannot claim an index it does not create", async f =>
{
    await f.Initial(); f.AddIndex(); await f.Ok("migrations", "add", "Index"); f.ChangeMigration(m => m["operations"] = new JsonArray());
    await f.Fail("MAGDB4207", "migrations", "validate");
});
Test("unknown protocol fails before source creation", async f =>
{
    f.Model["protocolVersion"] = 99; f.Save(); await f.Fail("MAGDB4102", "migrations", "add", "Initial"); Check(!File.Exists(f.HistoryPath));
});
Test("unknown required capability is rejected", async f =>
{
    f.Model["requiredCapabilities"] = new JsonArray("futureQuantumStorage"); f.Save(); await f.Fail("MAGDB4102", "migrations", "add", "Initial");
});
Test("duplicate store names are rejected", async f =>
{
    f.Model["stores"]!.AsArray().Add(f.Model["stores"]![0]!.DeepClone()); f.Save(); await f.Fail("MAGDB4103", "migrations", "add", "Initial");
});
Test("duplicate JSON properties cannot differ between parser implementations", async f =>
{
    File.WriteAllText(f.ModelPath, "{\"protocolVersion\":1,\"protocolVersion\":2}"); await f.Fail("MAGDB4101", "migrations", "add", "Initial");
});
Test("schema identities cannot traverse directories", async f =>
{
    f.Model["schemaId"] = "../outside"; f.Save(); await f.Fail("MAGDB4101", "migrations", "add", "Initial");
});
Test("history asset paths cannot traverse directories", async f =>
{
    await f.Initial(); var history = f.History(); history["schemas"]![0]!["path"] = "../outside.json"; f.WriteHistory(history);
    await f.Fail("MAGDB4104", "migrations", "validate");
});
Test("duplicate migration transitions are rejected", async f =>
{
    await f.Initial(); f.AddIndex(); await f.Ok("migrations", "add", "Index"); var history = f.History();
    history["migrations"]!.AsArray().Add(history["migrations"]![0]!.DeepClone()); f.WriteHistory(history); await f.Fail("MAGDB4204", "migrations", "validate");
});
Test("migration fingerprints bind both models", async f =>
{
    await f.Initial(); f.AddIndex(); await f.Ok("migrations", "add", "Index"); f.ChangeMigration(m => m["fromSchemaHash"] = "sha256:" + new string('0', 64));
    await f.Fail("MAGDB4204", "migrations", "validate");
});
Test("schema revision gaps cannot be published", async f =>
{
    await f.Initial(); var history = f.History(); history["targetRevision"] = 2; f.WriteHistory(history); await f.Fail("MAGDB4204", "migrations", "validate");
});
Test("unknown operation cannot fall through as no-op", async f =>
{
    await f.Initial(); f.AddIndex(); await f.Ok("migrations", "add", "Index"); f.ChangeMigration(m => m["operations"]![0]!["op"] = "futureIndex");
    await f.Fail("MAGDB4102", "migrations", "validate");
});
Test("resumable plan cannot include structural changes", async f =>
{
    await f.Initial(); f.AddIndex(); await f.Code(3, "migrations", "add", "Index", "--mode", "resumable"); await f.Fail("MAGDB4206", "migrations", "validate");
});
Test("empty migration starts as a draft instead of silently passing", async f =>
{
    await f.Initial(); await f.Code(3, "migrations", "add", "Backfill", "--empty"); await f.Fail("MAGDB4206", "migrations", "validate");
});
Test("publishing seals history and deterministically emits identical bytes", async f =>
{
    await f.Initial(); f.AddIndex(); await f.Ok("migrations", "add", "Index"); await f.Publish("release-a"); await f.Publish("release-b");
    Check(File.ReadAllBytes(f.Published("release-a")).SequenceEqual(File.ReadAllBytes(f.Published("release-b"))));
    Check(File.Exists(Path.Combine(f.Source, "fixture", "published.lock.json")));
});
Test("published migration content cannot be changed", async f =>
{
    await f.Initial(); f.AddIndex(); await f.Ok("migrations", "add", "Index"); await f.Publish("release");
    f.ChangeMigration(m => m["reviewNote"] = "changed released artifact"); await f.Fail("MAGDB4301", "migrations", "validate");
});
Test("published baseline cannot be rebound to an identical later snapshot", async f =>
{
    await f.Initial(); await f.Baseline(0, acknowledge: true); await f.Publish("release");
    await f.Code(3, "migrations", "add", "Data", "--empty");
    f.ChangeMigration(m => m["operations"] = new JsonArray());
    var history = f.History(); history["baselines"]![0]!["revision"] = 2; f.WriteHistory(history);
    await f.Fail("MAGDB4301", "migrations", "validate");
});
Test("published asset path cannot be rebound even to identical bytes", async f =>
{
    await f.Initial(); await f.Publish("release");
    var schema = Path.Combine(f.Source, "fixture", "schemas", "0001.json");
    File.Copy(schema, Path.Combine(f.Source, "fixture", "schemas", "renamed.json"));
    var history = f.History(); history["schemas"]![0]!["path"] = "schemas/renamed.json"; f.WriteHistory(history);
    await f.Fail("MAGDB4301", "migrations", "validate");
});
Test("published lock includes custom module exact bytes", async f =>
{
    await f.Initial(); await f.Code(3, "migrations", "add", "Transform", "--empty");
    var path = Path.Combine(f.Source, "fixture", "custom", "transform.js"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "export function transform(row) { return row; }\n");
    var hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    f.ChangeMigration(m => m["operations"] = new JsonArray(new JsonObject { ["op"] = "transform", ["store"] = "items", ["module"] = "../custom/transform.js", ["sha256"] = hash }));
    await f.Ok("migrations", "validate"); await f.Publish("release");
    File.AppendAllText(path, "// changed\n"); await f.Fail("MAGDB4301", "migrations", "validate");
});
Test("existing output cannot be replaced by another release", async f =>
{
    await f.Initial(); await f.Publish("release"); var original = File.ReadAllBytes(f.Published("release"));
    f.AddIndex(); await f.Ok("migrations", "add", "Index");
    await f.Fail("MAGDB4302", "migrations", "publish", "--output", Path.Combine(f.Root, "release"));
    Check(original.SequenceEqual(File.ReadAllBytes(f.Published("release"))));
});
Test("legacy baseline requires semantic acknowledgement", async f =>
{
    await f.Initial(); await f.Baseline(3, acknowledge: false);
});
Test("exact legacy baseline validates and publishes", async f =>
{
    await f.Initial(); await f.Baseline(0, acknowledge: true); await f.Publish("release");
    var manifest = JsonNode.Parse(File.ReadAllText(f.Published("release")))!; Check(manifest["baselines"]!.AsArray().Count == 1);
});
Test("extra legacy stores require a bridge, never inferred deletion", async f =>
{
    await f.Initial(); var legacy = f.Model.DeepClone(); var extra = legacy["stores"]![0]!.DeepClone(); extra["name"] = "surprise";
    legacy["stores"]!.AsArray().Add(extra); File.WriteAllText(f.LegacyPath, legacy.ToJsonString());
    await f.Baseline(3, acknowledge: true, write: false);
});
Test("custom transform module cannot escape migration root", async f =>
{
    await f.Initial(); await f.Code(3, "migrations", "add", "Transform", "--empty");
    f.ChangeMigration(m => m["operations"] = new JsonArray(new JsonObject { ["op"] = "transform", ["store"] = "items", ["module"] = "../../secret.js", ["sha256"] = "sha256:" + new string('0', 64) }));
    await f.Fail("MAGDB4104", "migrations", "validate");
});
Test("prototype-sensitive persisted names fail before publishing", async f =>
{
    f.Model["stores"]![0]!["fields"]![1]!["name"] = "__proto__"; f.Save();
    await f.Fail("MAGDB4103", "migrations", "add", "Initial");
});
Test("prototype-sensitive index key paths fail at model extraction", async f =>
{
    f.AddIndex(); f.Model["stores"]![0]!["indexes"]![0]!["keyPath"] = "metadata.constructor.value"; f.Save();
    await f.Fail("MAGDB4103", "migrations", "add", "Initial");
});
Test("numeric codec transition remains unsupported after removing draft marker", async f =>
{
    await f.Initial(); f.Model["numericFormat"] = "lossless-v1"; f.Save();
    await f.Code(3, "migrations", "add", "Codec");
    f.ChangeMigration(m => m["operations"] = new JsonArray());
    await f.Fail("MAGDB4206", "migrations", "validate");
});
Test("multiple databases require explicit selection and increment independently", async f =>
{
    var other = f.Model.DeepClone(); other["schemaId"] = "other";
    var envelope = new JsonObject { ["protocolVersion"] = 1, ["schemas"] = new JsonArray(f.Model.DeepClone(), other) };
    File.WriteAllText(f.ModelPath, envelope.ToJsonString());
    await f.Fail("MAGDB4003", "migrations", "add", "Initial");
    await f.Ok("migrations", "add", "Initial", "--all");
    f.AddIndex(); envelope["schemas"]![0] = f.Model.DeepClone(); File.WriteAllText(f.ModelPath, envelope.ToJsonString());
    await f.Ok("migrations", "add", "Index", "--database", "fixture");
    Check(f.History()["targetRevision"]!.GetValue<int>() == 2);
    var otherHistory = JsonNode.Parse(File.ReadAllText(Path.Combine(f.Source, "other", "history.json")))!;
    Check(otherHistory["targetRevision"]!.GetValue<int>() == 1);
});
Test("test command without runner fails instead of claiming tests ran", async f =>
{
    await f.Initial(); await f.Fail("MAGDB4501", "migrations", "test");
});
foreach (var scenario in new[] { "zero", "failure", "malformed" })
    Test($"external runner {scenario} report fails without sealing history", async f =>
    {
        f.Model["runnerFixture"] = scenario; f.Save(); await f.Initial();
        var executable = Path.Combine(AppContext.BaseDirectory, "Magic.IndexedDb.Cli.ContractTests" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var result = await f.Raw("migrations", "test", "--schema", f.ModelPath, "--directory", f.Source, "--runner", executable, "--json");
        Check(result.Code == 4 && result.Error.Contains("MAGDB4501", StringComparison.Ordinal));
        Check(result.Error.Contains(scenario == "malformed" ? "stdout" : "positive testsExecuted", StringComparison.Ordinal));
        Check(!File.Exists(Path.Combine(f.Source, "fixture", "published.lock.json")));
    });
Test("model comparison omission requires explicit history-only", async f =>
{
    await f.Initial(); var result = await f.Raw("migrations", "validate", "--directory", f.Source, "--json"); Check(result.Code == 2);
    result = await f.Raw("migrations", "validate", "--directory", f.Source, "--history-only", "--json"); Check(result.Code == 0 && result.Output.Contains("\"modelCompared\":false", StringComparison.Ordinal));
});
Test("unknown and repeated command options are usage errors", async f =>
{
    Check((await f.Raw("migrations", "validate", "--wat")).Code == 2);
    Check((await f.Raw("--version", "--version")).Code == 2);
});
Test("tool help and version distinguish protocol version", async f =>
{
    var result = await f.Raw("--version", "--json"); Check(result.Code == 0 && JsonNode.Parse(result.Output)!["protocolVersion"]!.GetValue<int>() == 1);
    Check((await f.Raw("--help")).Output.Contains("Magic IndexedDB 3.0", StringComparison.Ordinal));
});

var results = new JsonArray();
foreach (var (name, run) in tests)
{
    try { await run(); results.Add(new JsonObject { ["name"] = name, ["passed"] = true }); }
    catch (Exception error) { results.Add(new JsonObject { ["name"] = name, ["passed"] = false, ["error"] = error.ToString() }); }
}
var failed = results.Count(item => !item!["passed"]!.GetValue<bool>());
Console.WriteLine(new JsonObject { ["testsExecuted"] = results.Count, ["failed"] = failed, ["tests"] = results }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
return failed == 0 ? 0 : 1;

void Test(string name, Func<Fixture, Task> action) => tests.Add((name, async () => { using var fixture = new Fixture(); await action(fixture); }));
static void Check(bool value) { if (!value) throw new InvalidOperationException("Contract assertion failed."); }

sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "magicdb-contract-" + Guid.NewGuid().ToString("N"));
    public string Source => Path.Combine(Root, "source");
    public string ModelPath => Path.Combine(Root, "model.json");
    public string LegacyPath => Path.Combine(Root, "legacy.json");
    public string HistoryPath => Path.Combine(Source, "fixture", "history.json");
    public JsonObject Model { get; } = JsonNode.Parse("""
        {"protocolVersion":1,"schemaId":"fixture","numericFormat":"legacy","requiredCapabilities":[],"stores":[
          {"name":"items","keyPath":"id","autoIncrement":false,"indexes":[],"fields":[
            {"name":"id","type":"integer32","nullable":false,"codec":"json"},
            {"name":"name","type":"string","nullable":false,"codec":"json"}]}]}
        """)!.AsObject();
    public Fixture() { Directory.CreateDirectory(Root); Save(); }
    public void Save() => File.WriteAllText(ModelPath, Model.ToJsonString());
    public void AddIndex() { Model["stores"]![0]!["indexes"]!.AsArray().Add(new JsonObject { ["name"] = "byName", ["keyPath"] = "name", ["unique"] = false, ["multiEntry"] = false }); Save(); }
    public void AddField(string name, bool nullable) { Model["stores"]![0]!["fields"]!.AsArray().Add(new JsonObject { ["name"] = name, ["type"] = "string", ["nullable"] = nullable, ["codec"] = "json" }); Save(); }
    public JsonObject History() => JsonNode.Parse(File.ReadAllText(HistoryPath))!.AsObject();
    public void WriteHistory(JsonObject value) => File.WriteAllText(HistoryPath, value.ToJsonString());
    public string Published(string folder) => Path.Combine(Root, folder, "fixture", "manifest.json");
    public void ChangeMigration(Action<JsonObject> action)
    {
        var path = Path.Combine(Source, "fixture", History()["migrations"]!.AsArray().Last()!["path"]!.GetValue<string>());
        var migration = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); action(migration); File.WriteAllText(path, migration.ToJsonString());
    }
    public Task Initial() => Ok("migrations", "add", "Initial");
    public Task Publish(string directory) => Ok("migrations", "publish", "--output", Path.Combine(Root, directory));
    public async Task Baseline(int expected, bool acknowledge, bool write = true)
    {
        if (write) File.WriteAllText(LegacyPath, Model.ToJsonString());
        List<string> args = ["migrations", "baseline", "--directory", Source, "--database", "fixture", "--legacy-schema", LegacyPath, "--revision", "1", "--json"];
        if (acknowledge) args.Add("--acknowledge-data-contract");
        var result = await Raw(args.ToArray()); AssertExit(expected, result);
    }
    public async Task Ok(params string[] args) => await Code(0, args);
    public async Task Code(int expected, params string[] args)
    {
        var result = await Run(args); AssertExit(expected, result);
    }
    public async Task Fail(string code, params string[] args)
    {
        var result = await Run(args);
        if (result.Code == 0 || !result.Error.Contains(code, StringComparison.Ordinal)) throw new InvalidOperationException($"Expected {code}; exit {result.Code}, output {result.Output}, error {result.Error}");
    }
    private Task<Result> Run(string[] args) => Raw([.. args, "--schema", ModelPath, "--directory", Source, "--json"]);
    public async Task<Result> Raw(params string[] args)
    {
        using var stdout = new StringWriter(); using var stderr = new StringWriter();
        var code = await CliApplication.RunAsync(args, stdout, stderr); return new(code, stdout.ToString(), stderr.ToString());
    }
    private static void AssertExit(int expected, Result result)
    {
        if (result.Code != expected) throw new InvalidOperationException($"Expected exit {expected}; actual {result.Code}. Output {result.Output}; error {result.Error}");
    }
    public void Dispose() { Directory.Delete(Root, recursive: true); }
    public sealed record Result(int Code, string Output, string Error);
}
