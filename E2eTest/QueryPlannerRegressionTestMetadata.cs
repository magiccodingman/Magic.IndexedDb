using System.Text.Json;

namespace E2eTest;

[TestClass]
public sealed class QueryPlannerRegressionTestMetadata : TestBase<E2eTestWebApp.TestPages.QueryPlannerRegressionPage>
{
    [TestMethod]
    public async Task Compound_index_components_are_not_standalone_indexes()
    {
        await using var disposablePage = new DisposablePage(await NewPageAsync());
        var resultJson = await disposablePage.Page.EvaluateAsync<string>(
            """
            async () => {
                const module = await import('/_content/Magic.IndexedDb/utilities/utilityHelpers.js');
                const table = {
                    schema: {
                        primKey: { keyPath: 'Id' },
                        indexes: [
                            { keyPath: ['Tenant', 'Category'], unique: false }
                        ]
                    }
                };

                const metadata = module.buildIndexMetadata(table);
                return JSON.stringify({
                    tenantStandalone: metadata.indexes.has('Tenant'),
                    categoryStandalone: metadata.indexes.has('Category'),
                    compoundCount: metadata.compoundIndexes.size
                });
            }
            """);

        using var document = JsonDocument.Parse(resultJson);
        var root = document.RootElement;
        Assert.AreEqual(1, root.GetProperty("compoundCount").GetInt32());
        Assert.IsFalse(root.GetProperty("tenantStandalone").GetBoolean(),
            "A component of [Tenant+Category] is not automatically an IndexedDB 2.0 index named Tenant.\n" + resultJson);
        Assert.IsFalse(root.GetProperty("categoryStandalone").GetBoolean(),
            "A component of [Tenant+Category] is not automatically an IndexedDB 2.0 index named Category.\n" + resultJson);
    }

    [TestMethod]
    public async Task Compound_key_normalization_is_collision_free_for_string_components()
    {
        await using var disposablePage = new DisposablePage(await NewPageAsync());
        var resultJson = await disposablePage.Page.EvaluateAsync<string>(
            """
            async () => {
                const module = await import('/_content/Magic.IndexedDb/utilities/utilityHelpers.js');
                const keys = ['PartA', 'PartB'];
                const left = module.normalizeCompoundKey(keys, { PartA: 'a|b', PartB: 'c' });
                const right = module.normalizeCompoundKey(keys, { PartA: 'a', PartB: 'b|c' });
                return JSON.stringify({ left, right, equal: left === right });
            }
            """);

        using var document = JsonDocument.Parse(resultJson);
        Assert.IsFalse(document.RootElement.GetProperty("equal").GetBoolean(),
            "Distinct compound IndexedDB keys must never collapse to the same de-duplication identity.\n" + resultJson);
    }
}
