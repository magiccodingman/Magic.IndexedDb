using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Magic.IndexedDb.Cli;

internal static class SchemaSource
{
    internal static async Task<List<JsonObject>> LoadAsync(Arguments args, CancellationToken cancellationToken)
    {
        JsonObject input;
        if (args.Get("schema") is { } schemaFile) input = Protocol.ReadObject(Path.GetFullPath(schemaFile));
        else if (args.Get("project") is { } project) input = await ExportProjectAsync(project, args, cancellationToken);
        else throw new CliException("MAGDB4002", "Supply --schema MODEL.json or --project DESIGN_TIME.csproj. Use --history-only explicitly to validate committed history without comparing the current application model.", 2);

        return ReadSchemas(input);
    }

    internal static List<JsonObject> ReadSchemas(JsonObject input)
    {
        var schemas = input["schemas"] is JsonArray array
            ? array.Select(item => Protocol.ValidateSchema(Protocol.Object(item, "schema"))).ToList()
            : [Protocol.ValidateSchema(input)];
        if (input.ContainsKey("schemas")) Protocol.Version(input);
        if (schemas.Count == 0) throw new CliException("MAGDB4103", "The exporter returned no database schemas.");
        if (schemas.Select(schema => Protocol.Text(schema, "schemaId")).Distinct(StringComparer.Ordinal).Count() != schemas.Count)
            throw new CliException("MAGDB4103", "The exporter returned duplicate schema IDs.");
        return schemas.OrderBy(schema => Protocol.Text(schema, "schemaId"), StringComparer.Ordinal).ToList();
    }

    internal static List<JsonObject> Select(List<JsonObject> schemas, Arguments args, bool list = false)
    {
        if (args.Get("database") is { } id)
        {
            var selected = schemas.Where(schema => Protocol.Text(schema, "schemaId") == id).ToList();
            if (selected.Count != 1) throw new CliException("MAGDB4003", $"Schema '{id}' was not exported. Available: {string.Join(", ", schemas.Select(schema => Protocol.Text(schema, "schemaId")))}.", 2);
            return selected;
        }
        if (schemas.Count == 1 || args.Has("all") || list) return schemas;
        throw new CliException("MAGDB4003", $"More than one schema is available: {string.Join(", ", schemas.Select(schema => Protocol.Text(schema, "schemaId")))}. Choose --database ID or --all.", 2);
    }

    private static async Task<JsonObject> ExportProjectAsync(string input, Arguments args, CancellationToken cancellationToken)
    {
        var project = Path.GetFullPath(input);
        if (Directory.Exists(project))
        {
            var projects = Directory.GetFiles(project, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projects.Length != 1) throw new CliException("MAGDB4401", "Choose one explicit design-time .csproj; the directory contains zero or multiple projects.", 4);
            project = projects[0];
        }
        if (!File.Exists(project) || Path.GetExtension(project) != ".csproj") throw new CliException("MAGDB4401", $"Design-time project does not exist: '{project}'.", 4);
        var workingDirectory = Path.GetDirectoryName(project)!;
        var configuration = args.Get("configuration") ?? "Release";
        var framework = args.Get("framework");
        var properties = await ReadPropertiesAsync(project, workingDirectory, configuration, framework, cancellationToken);
        if (!string.Equals(properties["MagicIndexedDbDesignTime"]?.GetValue<string>(), "true", StringComparison.OrdinalIgnoreCase) ||
            properties["OutputType"]?.GetValue<string>() is not ("Exe" or "WinExe"))
            throw new CliException("MAGDB4402", "--project must point to a runnable schema-export companion with <MagicIndexedDbDesignTime>true</MagicIndexedDbDesignTime>. The CLI does not boot a Blazor app. Create a console project that calls MagicSchemaExporter.Run(args, tableTypes, repositoryTypes); see the CLI guide.", 4);
        if (framework is null)
        {
            framework = properties["TargetFramework"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(framework)) throw new CliException("MAGDB4403", "The exporter multi-targets. Select one target with --framework (for example net10.0).", 4);
        }
        var build = await RunProcessAsync(DotnetHost(), ["build", project, "--configuration", configuration, "--framework", framework,
            "--nologo", "--verbosity", "quiet", "-p:GeneratePackageOnBuild=false"], workingDirectory, cancellationToken);
        if (build.ExitCode != 0) throw new CliException("MAGDB4404", "Design-time build failed:\n" + Tail(build.StandardError + build.StandardOutput), 4);
        properties = await ReadPropertiesAsync(project, workingDirectory, configuration, framework, cancellationToken);
        var target = properties["TargetPath"]?.GetValue<string>();
        if (target is null || !File.Exists(target)) throw new CliException("MAGDB4404", "MSBuild did not produce the selected TargetPath.", 4);
        var export = await RunProcessAsync(DotnetHost(), [target, "--magic-schema-export"], workingDirectory, cancellationToken);
        if (export.ExitCode != 0) throw new CliException("MAGDB4405", "Schema exporter failed:\n" + Tail(export.StandardError), 4);
        try
        {
            using var document = JsonDocument.Parse(export.StandardOutput);
            Protocol.RejectDuplicateProperties(document.RootElement);
            return JsonNode.Parse(export.StandardOutput) as JsonObject ?? throw new JsonException("Expected a JSON object.");
        }
        catch (JsonException error)
        {
            throw new CliException("MAGDB4405", "Exporter stdout must contain only a schema JSON document. Send logs to stderr. " + error.Message, 4);
        }
    }

    private static async Task<JsonObject> ReadPropertiesAsync(string project, string cwd, string configuration, string? framework, CancellationToken token)
    {
        List<string> arguments = ["msbuild", project, "--nologo", "--verbosity:quiet",
            "-getProperty:MagicIndexedDbDesignTime,OutputType,TargetFramework,TargetFrameworks,TargetPath", "-p:Configuration=" + configuration];
        if (framework is not null) arguments.Add("-p:TargetFramework=" + framework);
        var result = await RunProcessAsync(DotnetHost(), arguments, cwd, token);
        if (result.ExitCode != 0) throw new CliException("MAGDB4404", "Project evaluation failed under the project's SDK:\n" + Tail(result.StandardError + result.StandardOutput), 4);
        try
        {
            return JsonNode.Parse(result.StandardOutput)?["Properties"] as JsonObject ?? throw new JsonException("Missing Properties.");
        }
        catch (JsonException error) { throw new CliException("MAGDB4404", "Invalid MSBuild property output: " + error.Message, 4); }
    }

    internal static string DotnetHost()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(host) && File.Exists(host)) return host;
        return "dotnet";
    }

    internal static async Task<ProcessResult> RunProcessAsync(string executable, IEnumerable<string> arguments, string cwd, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["DOTNET_NOLOGO"] = "true";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new CliException("MAGDB4404", $"Could not start '{executable}'.", 4);
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return new(process.ExitCode, await stdout, await stderr);
    }

    private static string Tail(string value) => value.Length <= 6000 ? value : value[^6000..];
    internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
