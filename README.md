# Magic IndexedDB

Magic IndexedDB is a LINQ-style query provider for IndexedDB in Blazor. It translates supported C# expression trees into an IndexedDB-aware plan, uses single and compound indexes where possible, and falls back to an optimized cursor engine where necessary.

## Documentation

The maintained documentation now lives in [`docs/`](docs/README.md).

- [Installation](docs/getting-started/installation.md)
- [Schema setup](docs/getting-started/schema.md)
- [First application workflow](docs/getting-started/first-application.md)
- [Querying guide](docs/guides/querying.md)
- [`Where` versus `Cursor`](docs/guides/where-vs-cursor.md)
- [Ordering and pagination](docs/guides/ordering-and-pagination.md)
- [Public API reference](docs/reference/public-api.md)
- [How the query engine works](docs/architecture/query-engine.md)

Version 1 documentation remains available in the [legacy archive](MagicIndexDbWiki/Version-1.0-Legacy.md).

## Version compatibility

Magic IndexedDB 3 targets .NET 10. Applications that must remain on .NET 8 should continue using the 2.x NuGet line. Read the [version 3 upgrade guide](docs/upgrading/version-3.md) before upgrading.

The automated migration protocol is still under construction. Version 3 does not promise automatic schema migration; plan and test changes against existing browser data.

## Quick start

Install the package:

```bash
dotnet add package Magic.IndexedDb
```

Register it in a standalone Blazor WebAssembly application:

```csharp
builder.Services.AddMagicBlazorDB(
    BlazorInteropMode.WASM,
    builder.HostEnvironment.IsDevelopment());
```

Inject `IMagicIndexedDb`, open a typed table query, and execute a predicate:

```csharp
IMagicQuery<Person> people = await MagicDb.Query<Person>();

List<Person> results = await people
    .Where(person => person.Age >= 18 && person.IsActive)
    .ToListAsync();
```

For progressive processing:

```csharp
await foreach (Person person in people
    .Where(person => person.IsActive)
    .AsAsyncEnumerable(cancellationToken))
{
    await ProcessAsync(person, cancellationToken);
}
```

`ToListAsync()` applies requested materialized ordering. `AsAsyncEnumerable()` prioritizes progressive delivery and does not promise final arrival order.

## Contributing

Issues and pull requests are welcome. Changes to query translation, serialization, schema handling, or the JavaScript engine should include focused unit tests and browser end-to-end coverage where applicable.

## Contributors Hall of Fame

Thank you to every contributor, including these developers whose sustained work has had a significant impact on the project:

- [@yueyinqiu](https://github.com/yueyinqiu) helped complete version 1 and kept the project moving during its earliest maintenance period.
- [@Ard2025](https://github.com/Ard2025) has contributed extensive bug fixes, cleanup, refactoring, and design discussions throughout version 2 and beyond.
