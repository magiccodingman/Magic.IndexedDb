using E2eTest.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

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
