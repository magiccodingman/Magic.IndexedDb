using System.Text.Json;

namespace Magic.IndexedDb.Models;

internal class MagicJsPackage
{
    public int ProtocolVersion { get; set; } = 2;
    public bool YieldResults { get; set; } = false;
    public required string ModulePath { get; set; }
    public required string MethodName { get; set; }
    public JsonElement[] Parameters { get; set; } = [];
    public bool IsVoid { get; set; } = false;
}
