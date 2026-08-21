using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components;
using Magic.IndexedDb.SchemaAnnotations;
using System.Text.Json;
using TestBase.Models;

namespace E2eTestWebApp.TestPages;

[Route("/SingleRecordBasicTest")]
public class SingleRecordBasicTestPage(IMagicIndexedDb magic) : TestPageBase
{
    public async Task<string> Add()
    {
        var db = await magic.Query<Person>();
        const string escaped = "slash\\ newline\n tab\t snowman ☃";
        await db.AddAsync(new Person
        {
            _Age = 20,
            Name = "John",
            Secret = escaped,
            Nested = new Nested { Value = escaped }
        });
        var results = await db.ToListAsync();

        var person = results.SingleOrDefault();
        return person?.Secret == escaped &&
               person.Nested.Value == escaped
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> Delete()
    {
        var db = await magic.Query<Person>();
        await db.AddAsync(new Person { _Id = 1, _Age = 20, Name = "John" });
        await db.DeleteAsync(new Person {_Id = 1, _Age = 20, Name = "John" });
        var results = await db.ToListAsync();

        return results.Count == 0 ? "OK" : "Incorrect";
    }

    public async Task<string> Update()
    {
        var db = await magic.Query<Person>();
        await db.AddAsync(new Person { _Id = 1, _Age = 20, Name = "John" });
        await db.UpdateAsync(new Person { _Id = 1, _Age = 25, Name = "John" });
        var results = await db.ToListAsync();

        return results.First()._Age == 25 ? "OK" : "Incorrect";
    }

    public async Task<string> GetAll()
    {
        var db = await magic.Query<Person>();
        await db.AddAsync(new Person { _Age = 20, Name = "John" });
        await db.AddAsync(new Person { _Age = 25, Name = "Peter" });
        await db.AddAsync(new Person { _Age = 35, Name = "Bert" });
        var results = await db.ToListAsync();

        return results.Count == 3 ? "OK" : "Incorrect";
    }

    public async Task<string> EmptyCount()
    {
        var db = await magic.Query<Person>();
        return await db.CountAsync() == 0 ? "OK" : "Incorrect";
    }

    public async Task<string> YieldAll()
    {
        var db = await magic.Query<Person>();
        await db.AddRangeAsync([
            new Person { Name = "One" },
            new Person { Name = "Two" },
            new Person { Name = "Three" }
        ]);

        var yielded = new List<Person>();
        await foreach (var person in db.AsAsyncEnumerable())
            yielded.Add(person);

        return yielded.Count == 3 ? "OK" : "Incorrect";
    }
}
