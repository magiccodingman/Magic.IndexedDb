namespace Magic.IndexedDb.Cli;

internal sealed class CliException(string code, string message, int exitCode = 3) : Exception(message)
{
    public string Code { get; } = code;
    public int ExitCode { get; } = exitCode;
    public string Documentation => "https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/migration-cli.md#diagnostics";
}
