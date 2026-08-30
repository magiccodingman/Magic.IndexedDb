using System.Text.Json;
using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public sealed class QueryPlannerRegressionTestHardening : TestBase<QueryPlannerHardeningPage>
{
    [TestMethod]
    public async Task Case_sensitive_StartsWith_uses_index_path()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.CaseSensitiveIndexedStartsWithUsesIndex);

        Assert.AreEqual("OK", run.Output,
            "Case-sensitive StartsWith must preserve semantics.\n" + FormatTrace(run.TraceJson));
        Assert.IsFalse(string.IsNullOrWhiteSpace(run.TraceJson),
            "Expected a structured planner trace for the hardening probe.");

        using var document = JsonDocument.Parse(run.TraceJson!);
        var matchedIndexedBranch = document.RootElement
            .GetProperty("stages")
            .EnumerateArray()
            .Where(stage => stage.GetProperty("stage").GetString() == "branch-classified")
            .Select(stage => stage.GetProperty("details"))
            .Any(details =>
                details.GetProperty("strategy").GetString() == "single-index"
                && details.GetProperty("operations").EnumerateArray()
                    .Any(operation => operation.GetString() == "StartsWith")
                && details.GetProperty("properties").EnumerateArray()
                    .Any(property => property.GetString() == "Name"));

        Assert.IsTrue(matchedIndexedBranch,
            "A case-sensitive StartsWith on indexed Name should use the single-index planner path, " +
            "not conservatively fall back to the cursor.\n" + FormatTrace(run.TraceJson));
    }

    private static string FormatTrace(string? traceJson) =>
        string.IsNullOrWhiteSpace(traceJson)
            ? "Planner trace: <none>"
            : $"Planner trace: {traceJson}";
}
