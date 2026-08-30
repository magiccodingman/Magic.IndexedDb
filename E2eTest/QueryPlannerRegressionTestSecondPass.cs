using System.Text.Json;
using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public sealed class QueryPlannerRegressionTestSecondPass : TestBase<QueryPlannerRegressionPage>
{
    [TestMethod]
    public async Task Cursor_OR_branch_correlation_preserves_original_pairs()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.CursorOrBranchCorrelationPreservesPairs);

        AssertSemanticSuccess(run,
            "Cursor reconstruction must preserve correlation between conditions that originated in the same OR branch.");
    }

    [TestMethod]
    public async Task Indexed_disjoint_range_OR_preserves_union()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.IndexedDisjointRangeOrPreservesUnion);

        AssertSemanticSuccess(run,
            "Disjoint indexed range branches must remain a union rather than being collapsed into one between/range query.");
    }

    [TestMethod]
    public async Task Indexed_same_direction_range_OR_is_commutative()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.IndexedSameDirectionRangeOrIsCommutative);

        AssertSemanticSuccess(run,
            "Reordering OR branches on the same indexed property must not change the result set.");
    }

    [TestMethod]
    public async Task Case_insensitive_indexed_StartsWith_preserves_semantics()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.CaseInsensitiveIndexedStartsWithPreservesSemantics);

        AssertSemanticSuccess(run,
            "OrdinalIgnoreCase StartsWith must not be executed as a case-sensitive IndexedDB prefix lookup.");
    }

    [TestMethod]
    public async Task Compatibility_pruning_honors_case_insensitive_string_semantics()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.CompatibilityPruningHonorsCaseInsensitiveStringSemantics);

        AssertSemanticSuccess(run,
            "Compatibility pruning must not discard a satisfiable equality + ignore-case prefix branch.");
    }

    [TestMethod]
    public async Task Cursor_string_equality_preserves_CSharp_case_sensitivity()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.CursorStringEqualityPreservesCSharpCaseSensitivity);

        AssertSemanticSuccess(run,
            "Ordinary C# string == is case-sensitive and must retain that meaning when forced onto the cursor path.");
    }

    [TestMethod]
    public async Task Empty_external_Contains_matches_nothing()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.EmptyExternalContainsMatchesNothing);

        AssertSemanticSuccess(run,
            "An empty membership set is logical false; it must not collapse into an empty filter that returns the table.");
    }

    [TestMethod]
    public async Task Mixed_constant_true_AND_predicate_preserves_identity()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.MixedConstantTrueAndPredicatePreservesIdentity);

        AssertSemanticSuccess(run,
            "A constant true inside an AND expression is an identity element and must not become a synthetic record property.");
    }

    [TestMethod]
    public async Task Mixed_constant_true_OR_predicate_is_universal_true()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.MixedConstantTrueOrPredicatePreservesIdentity);

        AssertSemanticSuccess(run,
            "A constant true inside an OR expression makes the whole predicate universally true.");
    }

    [TestMethod]
    public async Task Date_member_greater_than_preserves_whole_day_semantics()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.DateMemberGreaterThanPreservesWholeDaySemantics);

        AssertSemanticSuccess(run,
            "DateTime.Date > target compares whole dates, not the original timestamp against target midnight.");
    }

    [TestMethod]
    public async Task Date_member_less_than_or_equal_preserves_whole_day_semantics()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.DateMemberLessThanOrEqualPreservesWholeDaySemantics);

        AssertSemanticSuccess(run,
            "DateTime.Date <= target must include every timestamp occurring on the target calendar day.");
    }

    [TestMethod]
    public async Task Negated_equality_preserves_semantics_end_to_end()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.NegatedEqualityPreservesSemantics);

        AssertSemanticSuccess(run,
            "Negating equality must emit an operation understood by the JS predicate evaluator.");
    }

    [TestMethod]
    public async Task Negated_inequality_preserves_semantics_end_to_end()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.NegatedInequalityPreservesSemantics);

        AssertSemanticSuccess(run,
            "Negating inequality must become canonical equality rather than an unsupported operation token.");
    }

    [TestMethod]
    public async Task String_Equals_method_preserves_semantics_end_to_end()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.StringEqualsMethodPreservesSemantics);

        AssertSemanticSuccess(run,
            "Supported string.Equals translation must use an operation token the JS evaluator understands.");
    }

    [TestMethod]
    public async Task Cursor_rebuilder_preserves_OR_branch_correlation_truth_table()
    {
        await using var disposablePage = new DisposablePage(await NewPageAsync());
        var resultJson = await disposablePage.Page.EvaluateAsync<string>(
            """
            async () => {
                const module = await import('/_content/Magic.IndexedDb/utilities/rebuildNestedPredicate.js');
                const condition = (property, value) => ({
                    property,
                    operation: 'Equal',
                    value,
                    isString: false,
                    caseSensitive: true
                });

                const flattened = [
                    [condition('A', 1), condition('B', 2)],
                    [condition('A', 3), condition('B', 4)]
                ];
                const rebuilt = module.rebuildCursorConditionsToPredicateTree(flattened);

                const original = values => flattened.some(branch =>
                    branch.every(candidate => values[candidate.property] === candidate.value));

                const evaluateTree = (node, values) => {
                    if (node.nodeType === 'condition') {
                        return values[node.condition.property] === node.condition.value;
                    }
                    const results = (node.children ?? []).map(child => evaluateTree(child, values));
                    return node.operator === 'And'
                        ? results.every(Boolean)
                        : results.some(Boolean);
                };

                const assignments = [
                    { A: 1, B: 2 },
                    { A: 1, B: 4 },
                    { A: 3, B: 2 },
                    { A: 3, B: 4 },
                    { A: 9, B: 9 }
                ];

                const mismatches = assignments.filter(values =>
                    original(values) !== evaluateTree(rebuilt, values));

                return JSON.stringify({ flattened, rebuilt, mismatches });
            }
            """);

        using var document = JsonDocument.Parse(resultJson);
        Assert.AreEqual(0, document.RootElement.GetProperty("mismatches").GetArrayLength(),
            "Cursor predicate reconstruction must preserve branch correlation.\n" + resultJson);
    }

    [TestMethod]
    public async Task Compatibility_checker_preserves_satisfiable_ignore_case_prefix_branch()
    {
        await using var disposablePage = new DisposablePage(await NewPageAsync());
        var resultJson = await disposablePage.Page.EvaluateAsync<string>(
            """
            async () => {
                const module = await import('/_content/Magic.IndexedDb/utilities/areConditionsCompatible.js');
                const equality = {
                    property: 'Name', operation: 'Equal', value: 'Cathy',
                    isString: true, caseSensitive: true
                };
                const prefix = {
                    property: 'Name', operation: 'StartsWith', value: 'c',
                    isString: true, caseSensitive: false
                };

                return JSON.stringify({
                    compatible: module.areConditionsCompatible([equality], [prefix]),
                    equality,
                    prefix
                });
            }
            """);

        using var document = JsonDocument.Parse(resultJson);
        Assert.IsTrue(document.RootElement.GetProperty("compatible").GetBoolean(),
            "'Cathy' satisfies StartsWith('c', OrdinalIgnoreCase), so the branch cannot be pruned as contradictory.\n" + resultJson);
    }

    private static void AssertSemanticSuccess(PlannerTraceRunResult run, string message)
    {
        Assert.AreEqual("OK", run.Output, message + "\n" + FormatTrace(run.TraceJson));
    }

    private static string FormatTrace(string? traceJson) =>
        string.IsNullOrWhiteSpace(traceJson)
            ? "Planner trace: <none>"
            : $"Planner trace: {traceJson}";
}
