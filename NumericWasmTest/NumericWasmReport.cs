using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using TestBase.Numeric;

namespace NumericWasmTest;

public sealed class NumericWasmReport
{
    public string State { get; set; } = "running";
    public string Runtime { get; set; } = RuntimeInformation.RuntimeIdentifier;
    public string RuntimeVersion { get; set; } = Environment.Version.ToString();
    public bool IsBrowser { get; set; } = OperatingSystem.IsBrowser();
    public bool IsDynamicCodeCompiled { get; set; } = RuntimeFeature.IsDynamicCodeCompiled;
    public List<NumericScenarioResult> Scenarios { get; set; } = [];
    public List<NumericBenchmarkResult> Benchmark { get; set; } = [];
    public string? Failure { get; set; }
}

public sealed class NumericScenarioResult
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "running";
    public double ElapsedMilliseconds { get; set; }
    public JsonElement? PlannerTrace { get; set; }
    public string? Error { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NumericWasmReport))]
internal partial class NumericWasmReportJsonContext : JsonSerializerContext;
