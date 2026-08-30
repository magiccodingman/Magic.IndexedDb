using Magic.IndexedDb;
using Microsoft.AspNetCore.Components;
using TestBase.Models;

namespace E2eTestWebApp.TestPages;

[Route("/CompoundKeyPlannerRegression")]
public class CompoundKeyPlannerRegressionPage(IMagicIndexedDb magic) : TestPageBase
{
    private static CompositeRecord[] OrderingRecords() =>
    [
        new() { Tenant = "A", Sequence = 30, Category = "Gamma", Value = "A30" },
        new() { Tenant = "B", Sequence = 10, Category = "Alpha", Value = "B10" },
        new() { Tenant = "C", Sequence = 20, Category = "Beta", Value = "C20" }
    ];

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

    public async Task<string> CompoundPrimaryKeyComponentOrderByPreservesAscendingSequence()
    {
        var records = OrderingRecords();
        var db = await magic.Query<CompositeRecord>();
        await db.AddRangeAsync(records);

        var actual = await db
            .OrderBy(record => record.Sequence)
            .ToListAsync();
        var expected = records.OrderBy(record => record.Sequence);

        var result = RunTest(
            "OrderBy on a compound primary-key component preserves ascending sequence",
            actual,
            expected,
            ordered: true);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> CompoundPrimaryKeyComponentOrderByPreservesDescendingSequence()
    {
        var records = OrderingRecords();
        var db = await magic.Query<CompositeRecord>();
        await db.AddRangeAsync(records);

        var actual = await db
            .OrderByDescending(record => record.Sequence)
            .ToListAsync();
        var expected = records.OrderByDescending(record => record.Sequence);

        var result = RunTest(
            "OrderByDescending on a compound primary-key component preserves descending sequence",
            actual,
            expected,
            ordered: true);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> StandaloneIndexOrderByRemainsIndexedAndOrdered()
    {
        var records = OrderingRecords();
        var db = await magic.Query<CompositeRecord>();
        await db.AddRangeAsync(records);

        var actual = await db
            .OrderBy(record => record.Category)
            .ToListAsync();
        var expected = records.OrderBy(record => record.Category);

        var result = RunTest(
            "OrderBy on a real standalone secondary index remains ordered",
            actual,
            expected,
            ordered: true);
        return result.Success ? "OK" : result.Message;
    }
}
