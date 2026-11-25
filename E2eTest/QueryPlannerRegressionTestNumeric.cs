using System.Text.Json;
using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public sealed class QueryPlannerRegressionTestNumeric : TestBase<NumericTestPage>
{
    [TestMethod]
    public async Task LosslessNumbers_RoundTripThroughMaterializedAndStreamedInterop() =>
        Assert.AreEqual("OK", await RunTestPageMethodAsync(page => page.RoundTripAndStream));

    [TestMethod]
    public async Task NumericIndexes_PreservePredicateOrderingAndCursorSemantics()
    {
        var result = await RunTestPageMethodWithPlannerTraceAsync(page => page.IndexedPredicatesAndOrdering);
        Assert.AreEqual("OK", result.Output);
        AssertNativePlan(result.TraceJson, "compound-index");
    }

    [TestMethod]
    public async Task ExplicitNumericKeys_SupportExactLookupBulkUpdateAndDelete() =>
        Assert.AreEqual("OK", await RunTestPageMethodAsync(page => page.ExplicitKeysAndBulkCrud));

    [TestMethod]
    public async Task EqualDecimalScales_ShareUniqueIndexIdentityAndNativeOrder()
    {
        var result = await RunTestPageMethodWithPlannerTraceAsync(page => page.CanonicalUniqueIndexes);
        Assert.AreEqual("OK", result.Output);
        AssertNativePlan(result.TraceJson, "single-index");
    }

    [TestMethod]
    public async Task NativeAutoIncrement_UsesExactNumberKeysAndRejectsExhaustion() =>
        Assert.AreEqual("OK", await RunTestPageMethodAsync(page => page.NativeAutoIncrementKeys));

    [TestMethod]
    public async Task EncryptedNumbers_SurviveLockedPublicProjectionAndPatch() =>
        Assert.AreEqual("OK", await RunTestPageMethodAsync(page => page.ProtectedPublicAccess));

    private static void AssertNativePlan(string? traceJson, string strategy)
    {
        Assert.IsNotNull(traceJson, "The numeric query did not produce a planner trace.");
        using var document = JsonDocument.Parse(traceJson);
        var stages = document.RootElement.GetProperty("stages").EnumerateArray().ToArray();
        Assert.IsTrue(stages.Any(stage => stage.GetProperty("stage").GetString() == "branch-classified" &&
            stage.GetProperty("details").GetProperty("strategy").GetString() == strategy), traceJson);
        Assert.IsTrue(stages.Any(stage => stage.GetProperty("stage").GetString() == "indexed-execution"),
            "The numeric query must execute its native index.\n" + traceJson);
        Assert.IsFalse(stages.Any(stage => stage.GetProperty("stage").GetString() == "cursor-execution"),
            "This numeric query unexpectedly fell back to a cursor.\n" + traceJson);
    }
}
