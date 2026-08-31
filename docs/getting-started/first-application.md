# Your first workflow

After [registering the service](installation.md) and [defining a table](schema.md), obtain an `IMagicQuery<T>` for that table. The query object is both the entry point for querying and the table-scoped CRUD API.

The lifecycle example below is appropriate for standalone Blazor WebAssembly. In a prerendered server application, start IndexedDB work only after the component becomes interactive because JavaScript interop is unavailable during prerendering.

```razor
@page "/people"
@using Magic.IndexedDb
@inject IMagicIndexedDb MagicDb

@code {
    private List<Person> people = [];

    protected override async Task OnInitializedAsync()
    {
        IMagicQuery<Person> table = await MagicDb.Query<Person>();

        await table.AddAsync(new Person
        {
            FirstName = "Ada",
            LastName = "Lovelace"
        });

        people = await table
            .Where(person => person.LastName == "Lovelace")
            .ToListAsync();
    }
}
```

## Create

```csharp
await table.AddAsync(person, cancellationToken);
await table.AddRangeAsync(people, cancellationToken);
```

For an auto-incrementing key, leave the new record's key at its default value. A unique-index violation or invalid key is reported by the browser operation.

`AddAsync` and `AddRangeAsync` do not return generated keys or write an auto-generated key back into the supplied object. Re-query a newly inserted row before using that row with `UpdateAsync` or `DeleteAsync` when your code needs the generated primary key.

## Read

```csharp
List<Person> all = await table.ToListAsync();

List<Person> adults = await table
    .Where(person => person.Age >= 18)
    .ToListAsync();

Person? first = await table.FirstOrDefaultAsync(
    person => person.ExternalId == externalId);

int totalRows = await table.CountAsync();
```

`CountAsync()` on `IMagicQuery<T>` counts the entire table. It is not a filtered-count operator.

## Update

Updates identify records by their primary key:

```csharp
person.LastName = "Byron";

int updated = await table.UpdateAsync(person, cancellationToken);
int updatedMany = await table.UpdateRangeAsync(people, cancellationToken);
```

The integer result is the number reported by the underlying bulk operation.

## Delete

```csharp
await table.DeleteAsync(person, cancellationToken);
int deletedMany = await table.DeleteRangeAsync(people, cancellationToken);
```

Delete operations also use each object's primary key.

## Clear a table

```csharp
await table.ClearTable();
```

`ClearTable()` permanently removes every record from that object store. It does not delete the database or its schema.

## Stream a large result

```csharp
await foreach (Person person in table
    .Where(person => person.Age >= 18)
    .AsAsyncEnumerable(cancellationToken))
{
    await ProcessAsync(person, cancellationToken);
}
```

Use streaming when progressive processing and lower peak result memory matter more than final arrival order. See [streaming results](../guides/streaming.md).

## Next steps

- Learn the full [query syntax](../guides/querying.md).
- Understand [`Where` versus `Cursor`](../guides/where-vs-cursor.md).
- Read the rules for [ordering and pagination](../guides/ordering-and-pagination.md).
