using System.Text.Json;
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
            FormatTrace(run.TraceJson));
    }

    [TestMethod]
    public async Task Compound_primary_key_component_OrderBy_requires_cursor_and_preserves_order()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.CompoundPrimaryKeyComponentOrderByPreservesAscendingSequence);

        Assert.AreEqual("OK", run.Output,
            "Ordering by one component of a compound primary key must preserve the requested sequence.\n" +
            FormatTrace(run.TraceJson));
        AssertPartitionRequiresCursor(run.TraceJson,
            "The complete query must remain on a semantics-safe cursor path when ordering by a compound primary-key component.");
    }

    [TestMethod]
    public async Task Compound_primary_key_component_OrderByDescending_requires_cursor_and_preserves_order()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.CompoundPrimaryKeyComponentOrderByPreservesDescendingSequence);

        Assert.AreEqual("OK", run.Output,
            "Ordering descending by one component of a compound primary key must preserve the requested sequence.\n" +
            FormatTrace(run.TraceJson));
        AssertPartitionRequiresCursor(run.TraceJson,
            "The complete query must remain on a semantics-safe cursor path when ordering descending by a compound primary-key component.");
    }

    [TestMethod]
    public async Task Standalone_secondary_index_OrderBy_preserves_order()
    {
        var run = await RunTestPageMethodWithPlannerTraceAsync(
            page => page.StandaloneIndexOrderByRemainsIndexedAndOrdered);

        Assert.AreEqual("OK", run.Output,
            "Ordering on a real standalone secondary index must preserve its requested sequence.\n" +
            FormatTrace(run.TraceJson));
    }

    [TestMethod]
    public async Task OrderBy_capability_uses_real_standalone_indexes_not_compound_key_components()
    {
        await using var disposablePage = new DisposablePage(await NewPageAsync());
        var resultJson = await disposablePage.Page.EvaluateAsync<string>(
            """
            async () => {
                const helpers = await import('/_content/Magic.IndexedDb/utilities/utilityHelpers.js');
                const validation = await import('/_content/Magic.IndexedDb/utilities/linqValidation.js');

                const compoundTable = {
                    schema: {
                        primKey: { keyPath: ['Tenant', 'Sequence'] },
                        indexes: [
                            { keyPath: 'Category', unique: false }
                        ]
                    }
                };
                const simplePrimaryKeyTable = {
                    schema: {
                        primKey: { keyPath: 'Id' },
                        indexes: []
                    }
                };

                const metadata = helpers.buildIndexMetadata(compoundTable);
                const simplePrimaryMetadata = helpers.buildIndexMetadata(simplePrimaryKeyTable);
                const addition = (additionFunction, property) => [{
                    additionFunction,
                    property,
                    intValue: 0
                }];

                return JSON.stringify({
                    tenantOrderRequiresCursor: validation.validateQueryAdditions(addition('orderBy', 'Tenant'), metadata),
                    sequenceOrderRequiresCursor: validation.validateQueryAdditions(addition('orderBy', 'Sequence'), metadata),
                    tenantOrderDescendingRequiresCursor: validation.validateQueryAdditions(addition('orderByDescending', 'Tenant'), metadata),
                    sequenceOrderDescendingRequiresCursor: validation.validateQueryAdditions(addition('orderByDescending', 'Sequence'), metadata),
                    categoryOrderRequiresCursor: validation.validateQueryAdditions(addition('orderBy', 'Category'), metadata),
                    categoryOrderDescendingRequiresCursor: validation.validateQueryAdditions(addition('orderByDescending', 'Category'), metadata),
                    simplePrimaryOrderRequiresCursor: validation.validateQueryAdditions(addition('orderBy', 'Id'), simplePrimaryMetadata),
                    simplePrimaryOrderDescendingRequiresCursor: validation.validateQueryAdditions(addition('orderByDescending', 'Id'), simplePrimaryMetadata),
                    tenantStandalone: metadata.primaryKeyIndexes.has('Tenant') || metadata.indexes.has('Tenant'),
                    sequenceStandalone: metadata.primaryKeyIndexes.has('Sequence') || metadata.indexes.has('Sequence'),
                    categoryStandalone: metadata.indexes.has('Category'),
                    simplePrimaryStandalone: simplePrimaryMetadata.primaryKeyIndexes.has('Id')
                });
            }
            """);

        using var document = JsonDocument.Parse(resultJson);
        var root = document.RootElement;

        Assert.IsFalse(root.GetProperty("tenantStandalone").GetBoolean(), resultJson);
        Assert.IsFalse(root.GetProperty("sequenceStandalone").GetBoolean(), resultJson);
        Assert.IsTrue(root.GetProperty("categoryStandalone").GetBoolean(), resultJson);
        Assert.IsTrue(root.GetProperty("simplePrimaryStandalone").GetBoolean(), resultJson);

        Assert.IsTrue(root.GetProperty("tenantOrderRequiresCursor").GetBoolean(),
            "The first component of a compound primary key must not masquerade as a standalone OrderBy index.\n" + resultJson);
        Assert.IsTrue(root.GetProperty("sequenceOrderRequiresCursor").GetBoolean(),
            "The trailing component of a compound primary key must not masquerade as a standalone OrderBy index.\n" + resultJson);
        Assert.IsTrue(root.GetProperty("tenantOrderDescendingRequiresCursor").GetBoolean(),
            "The first component of a compound primary key must not masquerade as a standalone OrderByDescending index.\n" + resultJson);
        Assert.IsTrue(root.GetProperty("sequenceOrderDescendingRequiresCursor").GetBoolean(),
            "The trailing component of a compound primary key must not masquerade as a standalone OrderByDescending index.\n" + resultJson);

        Assert.IsFalse(root.GetProperty("categoryOrderRequiresCursor").GetBoolean(),
            "A real standalone secondary index must remain eligible for indexed OrderBy validation.\n" + resultJson);
        Assert.IsFalse(root.GetProperty("categoryOrderDescendingRequiresCursor").GetBoolean(),
            "A real standalone secondary index must remain eligible for indexed OrderByDescending validation.\n" + resultJson);
        Assert.IsFalse(root.GetProperty("simplePrimaryOrderRequiresCursor").GetBoolean(),
            "A simple primary key is independently orderable and must remain eligible for indexed OrderBy validation.\n" + resultJson);
        Assert.IsFalse(root.GetProperty("simplePrimaryOrderDescendingRequiresCursor").GetBoolean(),
            "A simple primary key is independently orderable and must remain eligible for indexed OrderByDescending validation.\n" + resultJson);
    }

    private static void AssertPartitionRequiresCursor(string? traceJson, string message)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(traceJson), "Expected a structured query-planner trace.");
        using var document = JsonDocument.Parse(traceJson!);
        var partition = document.RootElement
            .GetProperty("stages")
            .EnumerateArray()
            .FirstOrDefault(stage => stage.GetProperty("stage").GetString() == "partition-decision");

        Assert.AreNotEqual(JsonValueKind.Undefined, partition.ValueKind,
            "Planner trace must contain partition-decision.\n" + FormatTrace(traceJson));
        Assert.IsTrue(
            partition.GetProperty("details").GetProperty("requiresCursor").GetBoolean(),
            message + "\n" + FormatTrace(traceJson));
    }

    private static string FormatTrace(string? traceJson) =>
        string.IsNullOrWhiteSpace(traceJson)
            ? "Planner trace: <none>"
            : $"Planner trace: {traceJson}";
}
