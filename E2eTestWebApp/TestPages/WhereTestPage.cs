using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components;
using System.Text.Json;
using E2eTestWebApp.Models;

namespace E2eTestWebApp.TestPages;

[Route("/WhereTest")]
public class WhereTestPage(IMagicIndexedDb magic) : TestPageBase
{
    /*[MagicTable("Records", null)]
    private class Record
    {
        [MagicPrimaryKey("Id")]
        public int Id { get; set; }

        public int Int32Field { get; set; }
    }*/

    public async Task<string> Where1()
    {
        var db = await magic.Query<Person>();
        await db.AddAsync(new Person { Age = 20, Name = "John" });
        await db.AddAsync(new Person { Age = 25, Name = "Peter" });
        await db.AddAsync(new Person { Age = 35, Name = "Bert" });

        var results = await db.Where(p => p.Age < 30).ToListAsync();

        return results.Count == 2 ? "OK" : "Incorrect";
    }
}