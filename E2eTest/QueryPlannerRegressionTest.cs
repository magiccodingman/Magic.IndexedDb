using System.Text.Json;
using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public sealed class QueryPlannerRegressionTest : TestBase<QueryPlannerRegressionPage>
{
    [TestMethod]
    public async Task Independent_indexed_AND_preserves_intersection()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.IndependentIndexedAndPreservesIntersection);

        var branch = FindStage(run.TraceJson, "branch-classified",
            details => details.GetProperty("inputConditionCount").GetInt32() == 2);
        var properties = branch.GetProperty("details").GetProperty("properties")
            .EnumerateArray().Select(value => value.GetString()).ToArray();

        CollectionAssert.AreEquivalent(new[] { "Name", "TestInt" }, properties,
            "The regression must exercise one AND branch containing both independent indexed predicates.");

        Assert.AreEqual("OK", run.Output,
            "Two independently indexed conditions joined by AND must retain intersection semantics.\n" +
            FormatTrace(run.TraceJson));
    }

    [TestMethod]
    public async Task Compound_index_preserves_residual_predicate()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.CompoundIndexPreservesResidualPredicate);

        var branch = FindStage(run.TraceJson, "branch-classified",
            details => details.GetProperty("strategy").GetString() == "compound-index");
        var details = branch.GetProperty("details");

        Assert.AreEqual(3, details.GetProperty("inputConditionCount").GetInt32());
        Assert.AreEqual(2, details.GetProperty("consumedConditionCount").GetInt32());
        Assert.AreEqual(1, details.GetProperty("residualConditionCount").GetInt32(),
            "This test intentionally contains one predicate not covered by the selected compound index.");

        Assert.AreEqual("OK", run.Output,
            "Selecting a compound index must not discard predicates outside that index.\n" +
            FormatTrace(run.TraceJson));
    }

    [TestMethod]
    public async Task Multiple_indexed_OR_branches_with_take_reconverge_without_invalid_cursor_entries()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.MultipleIndexedOrWithTakePreservesSemantics);

        var reconvergence = FindStage(run.TraceJson, "pagination-reconvergence");
        var details = reconvergence.GetProperty("details");

        Assert.AreEqual(2, details.GetProperty("indexedQueryCountBefore").GetInt32(),
            "The regression must exercise two independently indexed OR branches before pagination reconvergence.");
        Assert.AreEqual(0, details.GetProperty("undefinedCursorConditionCountAfter").GetInt32(),
            "Pagination reconvergence must preserve each indexed branch as a valid cursor condition set.\n" +
            FormatTrace(run.TraceJson));

        Assert.AreEqual("OK", run.Output,
            "A multi-index OR query with Take must preserve the query semantics rather than fail during cursor fallback.\n" +
            FormatTrace(run.TraceJson));
    }

    [TestMethod]
    public async Task Multiple_StartsWith_branches_preserve_prefix_semantics()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.MultipleStartsWithBranchesPreservePrefixSemantics);

        var prefixBranches = FindStages(run.TraceJson, "branch-classified")
            .Where(stage => stage.GetProperty("details").GetProperty("operations")
                .EnumerateArray().Any(operation => operation.GetString() == "StartsWith"))
            .ToList();

        Assert.AreEqual(2, prefixBranches.Count,
            "The regression must exercise the two prefix branches from the original OR predicate.");

        Assert.AreEqual("OK", run.Output,
            "Combining multiple indexed StartsWith branches must preserve prefix matching semantics.\n" +
            FormatTrace(run.TraceJson));
    }

    [TestMethod]
    public async Task Advanced_optimizer_dispatch_receives_compatible_shape()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.OptimizerDispatchTraceProbe);

        Assert.AreEqual("OK", run.Output,
            "The trace probe itself must remain a semantically correct query.");

        var dispatch = FindStage(run.TraceJson, "optimizer-dispatch");
        var details = dispatch.GetProperty("details");

        Assert.AreEqual("advancedOptimizeNestedOrFilter", details.GetProperty("optimizer").GetString());
        Assert.IsTrue(details.GetProperty("invokedWithCompatibleShape").GetBoolean(),
            "If the advanced optimizer is dispatched, it must receive the object-with-orGroups shape it declares.\n" +
            FormatTrace(run.TraceJson));
    }

    [TestMethod]
    public async Task Advanced_optimizer_preserves_OR_absorption_truth_table()
    {
        await using var disposablePage = new DisposablePage(await NewPageAsync());
        var resultJson = await disposablePage.Page.EvaluateAsync<string>(
            """
            async () => {
                const module = await import('/_content/Magic.IndexedDb/utilities/optimizeConditions.js');
                const condition = (property, value) => ({
                    property,
                    operation: 'Equal',
                    value,
                    isString: false,
                    caseSensitive: false
                });

                // (A == true && B == false) || A == true is equivalent to A == true.
                // The differing values deliberately avoid the separate cross-property dedupe defect,
                // isolating the subset-removal direction in this regression.
                const original = {
                    orGroups: [
                        { andGroups: [{ conditions: [condition('A', true), condition('B', false)] }] },
                        { andGroups: [{ conditions: [condition('A', true)] }] }
                    ]
                };

                const optimized = module.advancedOptimizeNestedOrFilter(original);
                const assignments = [
                    { A: false, B: false },
                    { A: false, B: true },
                    { A: true, B: false },
                    { A: true, B: true }
                ];

                const evaluate = (filter, values) => filter.orGroups.some(orGroup =>
                    (orGroup.andGroups ?? []).some(andGroup =>
                        (andGroup.conditions ?? []).every(candidate => values[candidate.property] === candidate.value)));

                const mismatches = assignments.filter(values =>
                    evaluate(original, values) !== evaluate(optimized, values));

                return JSON.stringify({ original, optimized, mismatches });
            }
            """);

        using var document = JsonDocument.Parse(resultJson);
        var mismatches = document.RootElement.GetProperty("mismatches");

        Assert.AreEqual(0, mismatches.GetArrayLength(),
            "Optimizer rewrite (A && B) || A must preserve the original truth table.\n" + resultJson);
    }

    [TestMethod]
    public async Task Advanced_optimizer_deduplication_preserves_distinct_properties()
    {
        await using var disposablePage = new DisposablePage(await NewPageAsync());
        var resultJson = await disposablePage.Page.EvaluateAsync<string>(
            """
            async () => {
                const module = await import('/_content/Magic.IndexedDb/utilities/optimizeConditions.js');
                const condition = property => ({
                    property,
                    operation: 'Equal',
                    value: true,
                    isString: false,
                    caseSensitive: false
                });

                const original = {
                    orGroups: [
                        { andGroups: [{ conditions: [condition('A'), condition('B')] }] }
                    ]
                };

                const optimized = module.advancedOptimizeNestedOrFilter(original);
                const assignments = [
                    { A: false, B: false },
                    { A: false, B: true },
                    { A: true, B: false },
                    { A: true, B: true }
                ];

                const evaluate = (filter, values) => filter.orGroups.some(orGroup =>
                    (orGroup.andGroups ?? []).some(andGroup =>
                        (andGroup.conditions ?? []).every(candidate => values[candidate.property] === candidate.value)));

                const mismatches = assignments.filter(values =>
                    evaluate(original, values) !== evaluate(optimized, values));

                return JSON.stringify({ original, optimized, mismatches });
            }
            """);

        using var document = JsonDocument.Parse(resultJson);
        var mismatches = document.RootElement.GetProperty("mismatches");

        Assert.AreEqual(0, mismatches.GetArrayLength(),
            "Canonicalization must not treat equal operation/value pairs on different properties as duplicates.\n" + resultJson);
    }

    private static JsonElement FindStage(
        string? traceJson,
        string stageName,
        Func<JsonElement, bool>? detailsPredicate = null)
    {
        var stages = FindStages(traceJson, stageName);
        var match = stages.FirstOrDefault(stage =>
            detailsPredicate is null || detailsPredicate(stage.GetProperty("details")));

        if (match.ValueKind != JsonValueKind.Undefined)
            return match;

        Assert.Fail($"Planner trace did not contain expected stage '{stageName}'.\n{FormatTrace(traceJson)}");
        return default;
    }

    private static IReadOnlyList<JsonElement> FindStages(string? traceJson, string stageName)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(traceJson), "Expected a structured query-planner trace.");

        using var document = JsonDocument.Parse(traceJson!);
        return document.RootElement
            .GetProperty("stages")
            .EnumerateArray()
            .Where(stage => stage.GetProperty("stage").GetString() == stageName)
            .Select(stage => stage.Clone())
            .ToList();
    }

    private static string FormatTrace(string? traceJson) =>
        string.IsNullOrWhiteSpace(traceJson)
            ? "Planner trace: <none>"
            : $"Planner trace: {traceJson}";
}
