using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public sealed class QueryPlannerRegressionTestCompoundKeys : TestBase<CompoundKeyPlannerRegressionPage>
{
    [TestMethod]
    public async Task Compound_primary_key_component_query_preserves_semantics()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.CompoundPrimaryKeyComponentQueryPreservesSemantics);

        Assert.AreEqual("OK", run.Output,
            "Querying one component of a compound primary key must preserve the expected record set regardless of the selected physical execution path.\n" +
            (run.TraceJson is null ? "Planner trace: <none>" : $"Planner trace: {run.TraceJson}"));
    }
}
