# Magic IndexedDB

[![NuGet version](https://img.shields.io/nuget/v/Magic.IndexedDb.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/Magic.IndexedDb/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Magic.IndexedDb.svg?logo=nuget&label=downloads)](https://www.nuget.org/packages/Magic.IndexedDb/)

Magic IndexedDB is a typed browser database library for Blazor. It lets you query IndexedDB with C# expressions while keeping filtering and query planning in the browser.

Instead of treating LINQ as an in-memory filter over an already-loaded collection, Magic IndexedDB translates supported predicates into an IndexedDB-aware query plan. It uses single-field and compound indexes where possible, partitions complex AND/OR expressions, and uses an optimized cursor engine for operations that IndexedDB cannot execute through an index.

[Documentation](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/README.md) · [NuGet](https://www.nuget.org/packages/Magic.IndexedDb/) · [.NET 10 upgrade notes](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/upgrading/dotnet-10.md) · [Issues](https://github.com/magiccodingman/Magic.IndexedDb/issues)

## Why use Magic IndexedDB?

- **Write browser database queries in C#.** Use strongly typed predicates instead of maintaining a separate JavaScript data-access layer.
- **Keep filtering close to the data.** Compatible equality, range, membership, ordering, and compound-key operations are planned around IndexedDB indexes.
- **Express real application logic.** Nested `&&` and `||` predicates are translated, partitioned, optimized, and de-duplicated by primary key.
- **Use indexes without giving up flexible queries.** `Where(...)` lets the planner use indexes where it can, while `Cursor(...)` handles scan-oriented queries.
- **Process large results progressively.** `AsAsyncEnumerable()` streams interop results so applications can begin processing before materializing the full returned collection.
- **Define schemas in C#.** Attributes describe primary keys, indexes, compound indexes, stored names, and databases.
- **Store practical object models.** Nested objects, collections, custom JSON converters, Unicode text, and constructor-based materialization are supported.

Magic IndexedDB is a strong fit for offline-first Blazor applications, progressive web apps, local browser caches, disconnected workflows, and client-side datasets that need more than simple key/value access.

## The query engine

Magic separates the C# API from the browser engine that plans and runs queries:

1. The C# library translates expressions and schema definitions.
2. A language-neutral model carries predicates, operations, and stored names to the browser.
3. The browser engine plans the query across primary keys, indexes, compound indexes, and cursor execution.

The public API is the C# library. Contributors can read about the [universal predicate language](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/architecture/universal-predicate-language.md) and [query engine](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/architecture/query-engine.md).

## How it works

1. The Blazor wrapper reads a supported C# expression tree.
2. Magic converts it into a language-neutral predicate tree.
3. The browser planner checks available primary keys, indexes, and compound indexes.
4. Compatible branches run as native IndexedDB queries through Dexie.js.
5. Remaining branches use the cursor engine, with metadata-first selection when pagination or first/last selection requires it.
6. Results return as a materialized list or a progressive async stream.

This feels familiar to LINQ users without pretending IndexedDB behaves like SQL or an in-memory collection.

## Requirements

The current codebase targets .NET 10.

The current package supports Blazor WebAssembly and Blazor applications using JavaScript interop over SignalR. Browser storage behavior and quota remain controlled by the user's browser.

## Quick start

Install the package:

```bash
dotnet add package Magic.IndexedDb
```

Register it in a standalone Blazor WebAssembly application:

```csharp
using Magic.IndexedDb;

builder.Services.AddMagicBlazorDB(
    BlazorInteropMode.WASM,
    builder.HostEnvironment.IsDevelopment());
```

Add the namespace and inject the scoped service into a Razor component:

```razor
@using Magic.IndexedDb
@inject IMagicIndexedDb MagicDb
```

Open a typed table query, write data, and execute a predicate:

```csharp
IMagicQuery<Person> people = await MagicDb.Query<Person>();

await people.AddAsync(new Person
{
    Name = "Ada Lovelace",
    Age = 36,
    IsActive = true
});

List<Person> results = await people
    .Where(person => person.Age >= 18 && person.IsActive)
    .ToListAsync();
```

Complex predicates use normal C# expression syntax:

```csharp
List<Person> matches = await people.Where(person =>
    (person.Age >= 18 && person.Age <= 30) &&
    (
        person.City == "New York" ||
        person.City == "San Francisco" ||
        person.Name.StartsWith("Ada")
    )).ToListAsync();
```

Process a result progressively when retaining the full returned list is unnecessary:

```csharp
await foreach (Person person in people
    .Where(person => person.IsActive)
    .AsAsyncEnumerable(cancellationToken))
{
    await ProcessAsync(person, cancellationToken);
}
```

Continue with [installation and configuration](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/getting-started/installation.md), [schema setup](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/getting-started/schema.md), and the [first complete workflow](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/getting-started/first-application.md).

## Query behavior worth knowing

- Start with `Where(...)`; it allows the engine to choose indexed, compound-indexed, and cursor branches.
- Use `Cursor(...)` for scan-oriented text matching or when you specifically need cursor execution.
- Magic's pagination chain is `Take(count).Skip(offset)` because of how its IndexedDB execution path composes limit and offset operations.
- `Where(...)` returns a staged query without `OrderBy`; use `Cursor(predicate).OrderBy(...)` or materialize and sort in .NET when filtering and ordering must be combined.
- `ToListAsync()` applies the requested materialized ordering.
- `AsAsyncEnumerable()` delivers records progressively; records from separate query branches may arrive out of order.
- `CountAsync()` on the root query counts the whole table; it is not currently a filtered-count operator.

The [`Where` versus `Cursor`](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/where-vs-cursor.md) and [ordering and pagination](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/ordering-and-pagination.md) guides explain these differences in detail.

## Documentation

The maintained documentation lives entirely in [`docs/`](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/README.md):

- [Installation](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/getting-started/installation.md)
- [Schema setup](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/getting-started/schema.md)
- [First application workflow](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/getting-started/first-application.md)
- [Querying guide](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/querying.md)
- [`Where` versus `Cursor`](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/where-vs-cursor.md)
- [Ordering and pagination](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/ordering-and-pagination.md)
- [Streaming results](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/streaming.md)
- [Database management](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/database-management.md)
- [Schema evolution](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/schema-evolution.md)
- [Public API reference](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/reference/public-api.md)
- [Query expression reference](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/reference/query-expressions.md)
- [Query engine architecture](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/architecture/query-engine.md)

Version 1 documentation remains available in the [legacy archive](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/MagicIndexDbWiki/Version-1.0-Legacy.md).

## Schema evolution

Magic IndexedDB does not currently migrate existing browser data when a C# model changes. Plan and test changes to stored names, indexes, primary keys, and required properties against data from the previous version of your application.

See [schema evolution and migrations](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/schema-evolution.md) before changing a deployed schema.

## Contributing

Issues and pull requests are welcome. Changes to expression translation, serialization, schema handling, or the JavaScript query engine should include focused unit tests and browser end-to-end coverage where applicable.

See [testing Magic IndexedDB](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/contributing/testing.md) for the local test commands and guidance on where to add tests.

## 🏆 Contributors Hall of Fame 🏆

Thank you to all contributors, whether large or small! This section is for the people who have put significant work, care, and energy into the project.

[@yueyinqiu](https://github.com/yueyinqiu) — I built this project in about two weeks in 2023, told nobody about it, then walked away and forgot about it. It was not until 2024 that I realized there were pull requests and tickets from other people. Yue provided significant contributions during that time and worked closely with me as we completed version 1 together. This project might have died without you, my friend, and you made it fun for me to come back and see it through. Together we finished version 1 and laid the foundation for version 2.

[@Ard2025](https://github.com/Ard2025) — Dude, you came out of left field in 2025 and became a powerhouse contributor! I swear you were a pest control exterminator in a past life because you just cannot stop killing bugs. You have also worked closely with me through valuable brainstorming sessions, major cleanup, refactoring, and much more since the version 2 alpha launch. Seriously, thank you—this project thrives because you are here.
