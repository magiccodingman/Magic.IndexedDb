using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Magic.IndexedDb.Cli;

/// <summary>Command entry point, also used by executable contract tests.</summary>
public static class CliApplication
{
    private const string Help = """
        Magic IndexedDB 3.0 migration tool (protocol 1)

        magicdb databases list --schema MODEL.json | --project EXPORTER.csproj
        magicdb schema export --project EXPORTER.csproj [--output MODEL.json]
        magicdb migrations add NAME --schema MODEL.json [--database ID|--all] [--empty]
        magicdb migrations validate --schema MODEL.json [--database ID|--all]
        magicdb migrations publish --schema MODEL.json --output RELEASE_DIRECTORY
        magicdb migrations baseline --database ID --legacy-schema OLD.json --revision N
            --acknowledge-data-contract [--id legacy-v2]
        magicdb migrations test --schema MODEL.json --runner EXECUTABLE
        magicdb hash FILE

        Common options:
          --directory PATH       Migration source root (default ./MagicMigrations).
          --schema FILE          Neutral schema or {protocolVersion:1,schemas:[...]}.
          --project CSPROJ       Explicit runnable schema-export companion project.
          --database ID          Select one logical schema; --all selects all.
          --framework TFM        Select a framework for a multi-target exporter.
          --configuration NAME   Exporter build configuration (default Release).
          --history-only         Validate/publish committed source without model drift check.
          --mode atomic|resumable Execution mode for a new migration (default atomic).
          --json                 Machine-readable results and diagnostics.
          --non-interactive      Accepted for CI; commands never prompt.
          --help, --version

        Publishing writes OUTPUT/SCHEMA_ID/manifest.json and seals published assets
        in published.lock.json. Use a new release output directory for each release.
        A test runner receives --manifest PATH and must return JSON with positive
        testsExecuted and failed:0. No browser test is claimed without a runner.
        Exit codes: 0 success, 2 usage, 3 validation/review, 4 exporter/runner, 5 IO, 130 cancelled.
        """;

    public static async Task<int> RunAsync(string[] arguments, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        var json = arguments.Contains("--json", StringComparer.Ordinal);
        try
        {
            var args = new Arguments(arguments);
            if (args.Has("help") || args.Positionals.Count == 0 && !args.Has("version"))
            {
                await output.WriteLineAsync(Help); return 0;
            }
            if (args.Has("version"))
            {
                args.Only("version");
                var version = typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
                await output.WriteLineAsync(json ? new JsonObject { ["version"] = version, ["protocolVersion"] = 1 }.ToJsonString() : version);
                return 0;
            }
            if (args.Positionals is ["hash", var file])
            {
                args.Only();
                var hash = Protocol.Hash(await File.ReadAllBytesAsync(file, cancellationToken));
                await output.WriteLineAsync(json ? new JsonObject { ["sha256"] = hash }.ToJsonString() : hash);
                return 0;
            }
            if (args.Positionals is ["databases", "list"])
            {
                args.Only("schema", "project", "database", "all", "framework", "configuration");
                var schemas = SchemaSource.Select(await SchemaSource.LoadAsync(args, cancellationToken), args, list: true);
                await WriteAsync(output, new JsonObject { ["databases"] = new JsonArray(schemas.Select(schema => (JsonNode?)new JsonObject
                {
                    ["schemaId"] = Protocol.Text(schema, "schemaId"), ["stores"] = Protocol.Array(schema, "stores").Count,
                    ["schemaHash"] = Protocol.Hash(schema)
                }).ToArray()) }, json);
                return 0;
            }
            if (args.Positionals is ["schema", "export"])
            {
                args.Only("schema", "project", "database", "all", "framework", "configuration", "output");
                var schemas = SchemaSource.Select(await SchemaSource.LoadAsync(args, cancellationToken), args, list: true);
                var envelope = new JsonObject { ["protocolVersion"] = 1, ["schemas"] = new JsonArray(schemas.Select(schema => (JsonNode?)schema.DeepClone()).ToArray()) };
                if (args.Get("output") is { } path)
                {
                    MigrationWorkspace.AtomicWrite(Path.GetFullPath(path), Protocol.Bytes(envelope));
                    await WriteAsync(output, new JsonObject { ["status"] = "exported", ["path"] = Path.GetFullPath(path), ["databases"] = schemas.Count }, json);
                }
                else await output.WriteLineAsync(Protocol.Canonical(envelope));
                return 0;
            }
            if (args.Positionals.Count < 2 || args.Positionals[0] != "migrations")
                throw new CliException("MAGDB4001", "Unknown command. Use --help.", 2);
            var command = args.Positionals[1];
            if (command == "add")
            {
                args.Only("schema", "project", "database", "all", "framework", "configuration", "directory", "empty", "mode");
                if (args.Positionals.Count != 3) throw new CliException("MAGDB4001", "migrations add requires one migration name.", 2);
                var name = args.Positionals[2];
                var schemas = SchemaSource.Select(await SchemaSource.LoadAsync(args, cancellationToken), args);
                var results = new JsonArray();
                foreach (var schema in schemas)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(Workspace(args, Protocol.Text(schema, "schemaId")).Add(schema, name, args.Has("empty"), args.Get("mode") ?? "atomic"));
                }
                await WriteAsync(output, new JsonObject { ["results"] = results }, json);
                return results.Any(item => Protocol.Boolean(Protocol.Object(item, "result"), "requiresReview")) ? 3 : 0;
            }
            if (args.Positionals.Count != 2) throw new CliException("MAGDB4001", "Unexpected positional arguments.", 2);
            if (command == "baseline")
            {
                args.Only("database", "directory", "legacy-schema", "revision", "id", "acknowledge-data-contract");
                var id = args.Require("database");
                if (!int.TryParse(args.Require("revision"), out var revision) || revision < 1) throw new CliException("MAGDB4001", "--revision must be a positive integer.", 2);
                var legacy = Protocol.ValidateSchema(Protocol.ReadObject(args.Require("legacy-schema")));
                var result = Workspace(args, id).AddBaseline(legacy, revision, args.Get("id") ?? "legacy-v2", args.Has("acknowledge-data-contract"));
                await WriteAsync(output, result, json); return 0;
            }
            if (command is not ("validate" or "publish" or "test")) throw new CliException("MAGDB4001", $"Unknown migrations command '{command}'. Use --help.", 2);
            var options = new List<string> { "schema", "project", "database", "all", "framework", "configuration", "directory", "history-only" };
            if (command == "publish") options.Add("output");
            if (command == "test") options.Add("runner");
            args.Only(options.ToArray());
            if (args.Has("history-only") && (args.Has("schema") || args.Has("project"))) throw new CliException("MAGDB4001", "--history-only cannot be combined with a current-model input.", 2);
            if (command == "test" && !args.Has("runner")) throw new CliException("MAGDB4501", "migrations test requires --runner EXECUTABLE. It must execute browser tests and report testsExecuted and failed counts. Use the repository migration browser harness in CI; no tests have run.", 4);
            var selected = await TargetsAsync(args, cancellationToken);
            // Validate every target before any publish output is written.
            foreach (var (id, schema) in selected) Workspace(args, id).Validate(schema);
            var summaries = new JsonArray();
            foreach (var (id, schema) in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var workspace = Workspace(args, id);
                if (command == "publish")
                {
                    var manifest = workspace.Publish(schema, args.Require("output"));
                    summaries.Add(new JsonObject { ["schemaId"] = id, ["status"] = "published", ["manifest"] = manifest });
                }
                else if (command == "test") summaries.Add(await TestAsync(workspace, schema, args.Require("runner"), cancellationToken));
                else
                {
                    var package = workspace.Validate(schema);
                    summaries.Add(new JsonObject { ["schemaId"] = id, ["status"] = "valid", ["revision"] = package.TargetRevision,
                        ["modelCompared"] = schema is not null, ["assets"] = package.Assets.Count });
                }
            }
            await WriteAsync(output, new JsonObject { ["results"] = summaries }, json);
            return 0;
        }
        catch (OperationCanceledException)
        {
            await DiagnosticAsync(error, new CliException("MAGDB4099", "Operation cancelled. Existing migration history has not been rolled back automatically.", 130), json);
            return 130;
        }
        catch (CliException exception) { await DiagnosticAsync(error, exception, json); return exception.ExitCode; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            await DiagnosticAsync(error, new CliException("MAGDB4901", exception.Message, 5), json); return 5;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or OverflowException)
        {
            await DiagnosticAsync(error, new CliException("MAGDB4101", "Invalid input: " + exception.Message), json); return 3;
        }
    }

    private static MigrationWorkspace Workspace(Arguments args, string id) => new(args.Get("directory") ?? "MagicMigrations", id);

    private static async Task<List<(string Id, JsonObject? Schema)>> TargetsAsync(Arguments args, CancellationToken token)
    {
        if (!args.Has("history-only")) return SchemaSource.Select(await SchemaSource.LoadAsync(args, token), args)
            .Select(schema => (Protocol.Text(schema, "schemaId"), (JsonObject?)schema)).ToList();
        var root = Path.GetFullPath(args.Get("directory") ?? "MagicMigrations");
        if (args.Get("database") is { } id) return [(Protocol.Identifier(id, "schemaId"), null)];
        var ids = Directory.Exists(root) ? Directory.GetDirectories(root).Where(path => File.Exists(Path.Combine(path, "history.json"))).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray() : [];
        if (ids.Length == 0) throw new CliException("MAGDB4201", "No migration history directories were found.");
        if (ids.Length > 1 && !args.Has("all")) throw new CliException("MAGDB4003", "Select --database ID or --all for multiple histories.", 2);
        return ids.Select(id => (Protocol.Identifier(id, "schemaId"), (JsonObject?)null)).ToList();
    }

    private static async Task<JsonObject> TestAsync(MigrationWorkspace workspace, JsonObject? schema, string runner, CancellationToken token)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "magicdb-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = workspace.Publish(schema, temporary, seal: false);
            var executable = runner.Contains(Path.DirectorySeparatorChar) ? Path.GetFullPath(runner) : runner;
            var result = await SchemaSource.RunProcessAsync(executable, ["--manifest", manifest], Directory.GetCurrentDirectory(), token);
            if (result.ExitCode != 0) throw new CliException("MAGDB4501", "Migration test runner failed: " + result.StandardError, 4);
            JsonObject report;
            try { report = JsonNode.Parse(result.StandardOutput) as JsonObject ?? throw new JsonException("Expected an object."); }
            catch (JsonException) { throw new CliException("MAGDB4501", "Test runner must write JSON {testsExecuted:N,failed:0} to stdout and logs to stderr.", 4); }
            int tests;
            try
            {
                tests = Protocol.Integer(report, "testsExecuted");
                if (Protocol.Integer(report, "failed", 0) != 0) throw new CliException("MAGDB4501", "Migration runner reported failing tests.", 4);
            }
            catch (CliException exception)
            {
                throw new CliException("MAGDB4501", "Test runner must report a positive testsExecuted count and zero failures. " + exception.Message, 4);
            }
            return new JsonObject { ["schemaId"] = workspace.SchemaId, ["status"] = "tested", ["testsExecuted"] = tests };
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }

    private static Task WriteAsync(TextWriter output, JsonNode result, bool json) => output.WriteLineAsync(result.ToJsonString(new JsonSerializerOptions(Protocol.JsonOptions) { WriteIndented = !json }));
    private static Task DiagnosticAsync(TextWriter output, CliException error, bool json) => output.WriteLineAsync(json
        ? new JsonObject { ["code"] = error.Code, ["message"] = error.Message, ["exitCode"] = error.ExitCode, ["documentation"] = error.Documentation }.ToJsonString(Protocol.JsonOptions)
        : $"{error.Code}: {error.Message}\nDocumentation: {error.Documentation}");
}
