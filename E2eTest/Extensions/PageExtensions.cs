using E2eTest.Entities;
using Microsoft.Playwright;

namespace E2eTest.Extensions;
internal static class PageExtensions
{
    public static async ValueTask DeleteDatabaseAsync(this IPage page, string database)
    {
        _ = await page.EvaluateAsync($"indexedDB.deleteDatabase('{database}')");
        var databases = await page.EvaluateAsync<DatabaseInfo[]>("indexedDB.databases()");
        Assert.IsFalse(databases!.Any(x => x.Name == database));
    }
}
