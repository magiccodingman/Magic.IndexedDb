namespace Magic.IndexedDb.Models;

internal class MagicJsPackage
{
    public bool YieldResults { get; set; } = false;
    public string ModulePath { get; set; }
    public string MethodName { get; set; }
    public string?[]? Parameters { get; set; }
    public bool IsVoid { get; set; } = false;

    public bool IsDebug { get; set; } = false;
}