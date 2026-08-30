using Magic.IndexedDb;
using Microsoft.AspNetCore.Components;
using TestBase.Data;
using TestBase.Models;

namespace E2eTestWebApp.TestPages;

[Route("/QueryPlannerHardening")]
public class QueryPlannerHardeningPage(IMagicIndexedDb magic) : TestPageBase
{
    public async Task<string> CaseSensitiveIndexedStartsWithUsesIndex()
    {
        var db = await magic.Query<Person>();
        await db.AddRangeAsync(PersonData.persons);

        var actual = await db
            .Where(person => person.Name.StartsWith("Ca", StringComparison.Ordinal))
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.Name.StartsWith("Ca", StringComparison.Ordinal));

        var result = RunTest("Case-sensitive StartsWith uses indexed semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }
}
