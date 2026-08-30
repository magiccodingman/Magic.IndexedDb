using Magic.IndexedDb;
using Microsoft.AspNetCore.Components;
using TestBase.Models;

namespace E2eTestWebApp.TestPages;

[Route("/CompoundKeyPlannerRegression")]
public class CompoundKeyPlannerRegressionPage(IMagicIndexedDb magic) : TestPageBase
{
    public async Task<string> CompoundPrimaryKeyComponentQueryPreservesSemantics()
    {
        CompositeRecord[] records =
        [
            new() { Tenant = "A", Sequence = 1, Category = "One", Value = "A1" },
            new() { Tenant = "A", Sequence = 2, Category = "Two", Value = "A2" },
            new() { Tenant = "B", Sequence = 1, Category = "One", Value = "B1" }
        ];

        var db = await magic.Query<CompositeRecord>();
        await db.AddRangeAsync(records);

        var actual = await db
            .Where(record => record.Sequence == 1)
            .ToListAsync();
        var expected = records.Where(record => record.Sequence == 1);

        var result = RunTest("Compound primary-key component query preserves semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }
}
