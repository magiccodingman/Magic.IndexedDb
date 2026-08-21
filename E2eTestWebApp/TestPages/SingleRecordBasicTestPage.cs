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

    public async Task<string> DictionaryPropertyRoundTrip()
    {
        var db = await magic.Query<ContractRecord>();
        await db.AddAsync(new ContractRecord
        {
            Name = "Dictionary",
            Metadata = new Dictionary<string, object?>
            {
                ["count"] = 2,
                ["enabled"] = false,
                ["label"] = "value"
            }
        });

        var record = (await db.ToListAsync()).Single();
        return record.Metadata.Count == 3 &&
               ((JsonElement)record.Metadata["count"]!).GetInt32() == 2 &&
               !((JsonElement)record.Metadata["enabled"]!).GetBoolean() &&
               ((JsonElement)record.Metadata["label"]!).GetString() == "value"
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> NumericEnumWhere()
    {
        var db = await magic.Query<ContractRecord>();
        await db.AddRangeAsync([
            new ContractRecord { Name = "Readable", NumericAccess = ContractRecord.NumericStatus.Read },
            new ContractRecord { Name = "Writable", NumericAccess = ContractRecord.NumericStatus.Write }
        ]);

        var matches = await db
            .Where(record => record.NumericAccess == ContractRecord.NumericStatus.Write)
            .ToListAsync();

        return matches.Count == 1 && matches[0].Name == "Writable"
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> NamedEnumWhere()
    {
        var db = await magic.Query<ContractRecord>();
        await db.AddRangeAsync([
            new ContractRecord { Name = "Inactive", NamedAccess = ContractRecord.NamedStatus.Inactive },
            new ContractRecord { Name = "Active", NamedAccess = ContractRecord.NamedStatus.Active }
        ]);

        var matches = await db
            .Where(record => record.NamedAccess == ContractRecord.NamedStatus.Active)
            .ToListAsync();

        return matches.Count == 1 &&
               matches[0].Name == "Active" &&
               matches[0].NamedAccess == ContractRecord.NamedStatus.Active
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> RangeCrud()
    {
        var db = await magic.Query<Person>();
        Person[] records =
        [
            new() { _Id = 1, Name = "one", _Age = 10 },
            new() { _Id = 2, Name = "two", _Age = 20 },
            new() { _Id = 3, Name = "three", _Age = 30 }
        ];
        await db.AddRangeAsync(records);

        records[0]._Age = 11;
        records[2]._Age = 33;
        var updated = await db.UpdateRangeAsync([records[0], records[2]]);
        var afterUpdate = await db.OrderBy(person => person._Id).ToListAsync();
        var deleted = await db.DeleteRangeAsync([records[0], records[1]]);
        var remaining = await db.ToListAsync();

        return updated == 2 && deleted == 2 &&
               afterUpdate.Select(person => person._Age).SequenceEqual([11, 20, 33]) &&
               remaining.Count == 1 && remaining[0]._Id == 3
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> ClearAndPopulatedCount()
    {
        var db = await magic.Query<Person>();
        await db.AddRangeAsync([
            new Person { _Id = 1, Name = "one" },
            new Person { _Id = 2, Name = "two" }
        ]);
        var before = await db.CountAsync();
        await db.ClearTable();
        var after = await db.CountAsync();

        return before == 2 && after == 0 && (await db.ToListAsync()).Count == 0
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> UniqueConstraintFailureIsRecoverable()
    {
        var db = await magic.Query<Person>();
        var unique = Guid.NewGuid();
        await db.AddAsync(new Person { _Id = 1, Name = "first", GUIY = unique });

        var rejected = false;
        try
        {
            await db.AddAsync(new Person { _Id = 2, Name = "duplicate", GUIY = unique });
        }
        catch
        {
            rejected = true;
        }

        await db.AddAsync(new Person { _Id = 3, Name = "after-error" });
        var rows = await db.OrderBy(person => person._Id).ToListAsync();
        return rejected && rows.Select(person => person._Id).SequenceEqual([1, 3])
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> DatabaseLifecycle()
    {
        var database = await magic.Database(TestBase.Repository.IndexDbContext.Animal);
        var existsInitially = await database.DoesExistAsync();
        var openInitially = await database.IsOpenAsync();
        await database.CloseAsync();
        var closed = !await database.IsOpenAsync();
        var persistedAfterClose = await database.DoesExistAsync();
        await database.OpenAsync();
        var reopened = await database.IsOpenAsync();
        await database.DeleteAsync();
        var deleted = !await database.DoesExistAsync();

        return existsInitially && openInitially && closed && persistedAfterClose && reopened && deleted
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> MultipleDatabaseIsolation()
    {
        var client = await magic.Query<Person>();
        var employee = await magic.Query<Person>(person => person.Databases.Employee);
        await client.AddAsync(new Person { _Id = 1, Name = "client" });
        await employee.AddAsync(new Person { _Id = 2, Name = "employee" });

        var clientRows = await client.ToListAsync();
        var employeeRows = await employee.ToListAsync();
        return clientRows.Count == 1 && clientRows[0].Name == "client" &&
               employeeRows.Count == 1 && employeeRows[0].Name == "employee"
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> ExactMaterializedOrdering()
    {
        var db = await magic.Query<Person>();
        await db.AddRangeAsync([
            new Person { _Id = 1, Name = "oldest", _Age = 50 },
            new Person { _Id = 2, Name = "youngest", _Age = 20 },
            new Person { _Id = 3, Name = "middle", _Age = 30 }
        ]);

        var ascending = await db.OrderBy(person => person._Age).ToListAsync();
        var descending = await db.OrderByDescending(person => person._Age).ToListAsync();
        var ascendingIds = ascending.Select(person => person._Id).ToArray();
        var descendingIds = descending.Select(person => person._Id).ToArray();
        return ascendingIds.SequenceEqual([2, 3, 1]) && descendingIds.SequenceEqual([1, 3, 2])
            ? "OK"
            : $"Ascending: {string.Join(',', ascendingIds)}; descending: {string.Join(',', descendingIds)}";
    }

    public async Task<string> InMemoryWhereAfterPagination()
    {
        var db = await magic.Query<Person>();
        await db.AddRangeAsync(Enumerable.Range(1, 10)
            .Select(value => new Person { _Id = value, Name = $"person-{value}", _Age = value }));

        var rows = await db.OrderBy(person => person._Id)
            .Take(6)
            .WhereAsync(person => person._Id % 2 == 0);

        return rows.Select(person => person._Id).SequenceEqual([2, 4, 6])
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> CompoundKeyCrudAndQuery()
    {
        var db = await magic.Query<CompositeRecord>();
        CompositeRecord[] rows =
        [
            new() { Tenant = "alpha", Sequence = 1, Category = "work", Value = "one" },
            new() { Tenant = "alpha", Sequence = 2, Category = "work", Value = "two" },
            new() { Tenant = "beta", Sequence = 1, Category = "home", Value = "three" }
        ];
        await db.AddRangeAsync(rows);

        var matches = await db.Where(row => row.Tenant == "alpha" && row.Category == "work").ToListAsync();
        rows[1].Value = "updated";
        var updated = await db.UpdateAsync(rows[1]);
        await db.DeleteAsync(rows[0]);
        var remaining = await db.OrderBy(row => row.Sequence).ToListAsync();

        return matches.Count == 2 && updated == 1 && remaining.Count == 2 &&
               remaining.Any(row => row.Tenant == "alpha" && row.Sequence == 2 && row.Value == "updated") &&
               remaining.All(row => row.Tenant != "alpha" || row.Sequence != 1)
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> LargeUnicodeStream()
    {
        var db = await magic.Query<Person>();
        const string payload = "🧙‍♂️\n雪\t\\quoted\"";
        await db.AddRangeAsync(Enumerable.Range(1, 64)
            .Select(value => new Person
            {
                _Id = value,
                Name = $"person-{value}",
                Secret = string.Concat(Enumerable.Repeat(payload, 16))
            }));

        var streamed = new Dictionary<int, string>();
        await foreach (var person in db.AsAsyncEnumerable())
            streamed.Add(person._Id, person.Secret);

        return streamed.Count == 64 && streamed.Values.All(value => value == string.Concat(Enumerable.Repeat(payload, 16)))
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> StreamCancellationAndRecovery()
    {
        var db = await magic.Query<Person>();
        await db.AddRangeAsync(Enumerable.Range(1, 50)
            .Select(value => new Person { _Id = value, Name = $"person-{value}" }));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = false;
        try
        {
            await foreach (var _ in db.AsAsyncEnumerable(cancellation.Token))
            {
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        return canceled && await db.CountAsync() == 50 ? "OK" : "Incorrect";
    }

    public async Task<string> ConcurrentStreamsRemainIsolated()
    {
        var db = await magic.Query<Person>();
        await db.AddRangeAsync(Enumerable.Range(1, 40)
            .Select(value => new Person
            {
                _Id = value,
                Name = $"person-{value}",
                TestInt = value % 2
            }));

        static async Task<List<int>> ReadIds(IAsyncEnumerable<Person> stream)
        {
            var values = new List<int>();
            await foreach (var item in stream)
                values.Add(item._Id);
            return values;
        }

        var evensTask = ReadIds(db.Where(person => person.TestInt == 0).AsAsyncEnumerable());
        var oddsTask = ReadIds(db.Where(person => person.TestInt == 1).AsAsyncEnumerable());
        await Task.WhenAll(evensTask, oddsTask);

        return evensTask.Result.Count == 20 && oddsTask.Result.Count == 20 &&
               evensTask.Result.All(id => id % 2 == 0) && oddsTask.Result.All(id => id % 2 == 1)
            ? "OK"
            : "Incorrect";
    }

    public async Task<string> StorageEstimate()
    {
        var estimate = await magic.GetStorageEstimateAsync();
        return estimate.Quota >= 0 && estimate.Usage >= 0 && estimate.Quota >= estimate.Usage
            ? "OK"
            : "Incorrect";
    }
}
