using Microsoft.Playwright;

namespace E2eTest.Extensions;
internal static class PageExtensions
{
    public static async ValueTask DeleteDatabaseAsync(this IPage page, string database)
    {
        _ = await page.EvaluateAsync<bool>("""
            database => new Promise((resolve, reject) => {
                const request = indexedDB.deleteDatabase(database);
                request.onsuccess = () => resolve(true);
                request.onerror = () => reject(request.error);
                request.onblocked = () => reject(new Error(`Deletion of ${database} was blocked.`));
            })
            """, database);
    }
}
