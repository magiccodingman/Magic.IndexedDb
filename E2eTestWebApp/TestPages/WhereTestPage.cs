using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components;
using System.Text.Json;
using TestBase.Models;

namespace E2eTestWebApp.TestPages;

[Route("/WhereTest")]
public class WhereTestPage(IMagicIndexedDb magic) : TestPageBase
{
    public async Task<string> Where1()
    {
        var db = await magic.Query<Person>();
        await db.AddAsync(new Person { _Age = 20, Name = "John" });
        await db.AddAsync(new Person { _Age = 25, Name = "Peter" });
        await db.AddAsync(new Person { _Age = 35, Name = "Bert" });

        var results = await db.Where(p => p._Age < 30).ToListAsync();

        return results.Count == 2 ? "OK" : "Incorrect";
    }
}