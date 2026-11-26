namespace Magic.IndexedDb.Cli;

internal sealed class Arguments
{
    private static readonly HashSet<string> Switches = ["all", "empty", "json", "non-interactive", "history-only", "acknowledge-data-contract", "help", "version"];
    private static readonly HashSet<string> Values = ["schema", "project", "database", "directory", "output", "framework", "configuration", "legacy-schema", "revision", "id", "mode", "runner", "module"];
    private readonly Dictionary<string, string?> options = new(StringComparer.Ordinal);
    public List<string> Positionals { get; } = [];

    public Arguments(string[] arguments)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            var item = arguments[index];
            if (item is "-h") item = "--help";
            if (item is "-v") item = "--version";
            if (!item.StartsWith("--", StringComparison.Ordinal))
            {
                if (item.StartsWith('-')) throw new CliException("MAGDB4001", $"Unknown option '{item}'. Use --help.", 2);
                Positionals.Add(item);
                continue;
            }
            var name = item[2..];
            if (options.ContainsKey(name)) throw new CliException("MAGDB4001", $"Option --{name} was supplied more than once.", 2);
            if (Switches.Contains(name)) options.Add(name, null);
            else if (Values.Contains(name))
            {
                if (++index >= arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal))
                    throw new CliException("MAGDB4001", $"Option --{name} requires a value.", 2);
                options.Add(name, arguments[index]);
            }
            else throw new CliException("MAGDB4001", $"Unknown option --{name}. Use --help.", 2);
        }
        if (Has("schema") && Has("project")) throw new CliException("MAGDB4001", "Choose exactly one of --schema or --project.", 2);
        if (Has("all") && Has("database")) throw new CliException("MAGDB4001", "Choose --all or --database, not both.", 2);
        if ((Has("framework") || Has("configuration")) && !Has("project")) throw new CliException("MAGDB4001", "--framework and --configuration require --project.", 2);
    }

    public bool Has(string name) => options.ContainsKey(name);
    public string? Get(string name) => options.GetValueOrDefault(name);
    public string Require(string name) => Get(name) ?? throw new CliException("MAGDB4001", $"--{name} is required.", 2);
    public void Only(params string[] allowed)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        set.UnionWith(["json", "non-interactive", "help"]);
        foreach (var name in options.Keys)
            if (!set.Contains(name)) throw new CliException("MAGDB4001", $"--{name} is not supported by this command.", 2);
    }
}
