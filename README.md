# Magic IndexedDB

Magic IndexedDB is a C#-first LINQ-to-IndexedDB query engine and typed browser database library for Blazor. It lets .NET applications query IndexedDB with C# expression trees while preserving the performance characteristics of a browser-native database.

Instead of treating LINQ as an in-memory filter over an already-loaded collection, Magic IndexedDB translates supported predicates into an IndexedDB-aware query plan. It uses single-field and compound indexes where possible, partitions complex AND/OR expressions, and uses an optimized cursor engine for operations that IndexedDB cannot execute through an index.

Beneath the current C# API is a language-neutral predicate and schema model. The C# wrapper is the first implementation, but the translation boundary is designed so other languages and frameworks can build wrappers that target the same browser query planner instead of recreating its indexing, cursor, and optimization logic.

[Documentation](docs/README.md) · [NuGet](https://www.nuget.org/packages/Magic.IndexedDb/) · [.NET 10 upgrade notes](docs/upgrading/dotnet-10.md) · [Issues](https://github.com/magiccodingman/Magic.IndexedDb/issues)

## Why use Magic IndexedDB?

- **Write browser database queries in C#.** Use strongly typed predicates instead of maintaining a separate JavaScript data-access layer.
- **Build on a universal query model.** Additional language wrappers can translate their native query intent into the same predicate tree and browser execution engine.
- **Keep filtering close to the data.** Compatible equality, range, membership, ordering, and compound-key operations are planned around IndexedDB indexes.
- **Express real application logic.** Nested `&&` and `||` predicates are translated, partitioned, optimized, and de-duplicated by primary key.
- **Choose the execution strategy deliberately.** `Where(...)` preserves opportunities for index optimization; `Cursor(...)` explicitly selects cursor evaluation when a scan is appropriate.
- **Process large results progressively.** `AsAsyncEnumerable()` streams interop results so applications can begin processing before materializing the full returned collection.
- **Define schemas in C#.** Tables describe their primary keys, indexes, compound indexes, persisted names, and valid databases through typed contracts.
- **Store practical object models.** The current release supports nested objects and collections, custom JSON converters, Unicode and escaped text, and explicit constructor materialization.

Magic IndexedDB is a strong fit for offline-first Blazor applications, progressive web apps, local browser caches, disconnected workflows, and client-side datasets that need more than simple key/value access.

## C# first, universal by design

Magic IndexedDB deliberately separates the language-facing wrapper from the engine that plans and executes browser queries:

1. A language wrapper translates native query expressions and schema definitions.
2. The universal layer represents predicates, logical groups, operations, query additions, and persisted schema names in a language-neutral form.
3. The browser engine partitions and optimizes that intent across primary keys, indexes, compound indexes, and cursor execution.

Today, the supported public wrapper is the C# and Blazor API. A future TypeScript, JavaScript, Python, or other language wrapper could produce the same universal intent and reuse the same IndexedDB engine rather than starting over. Building a wrapper still requires semantic translation, schema mapping, validation, and transport compatibility; the internal JavaScript protocol is not yet presented as an independently versioned public SDK.

See the [universal predicate language](docs/architecture/universal-predicate-language.md) and [query engine architecture](docs/architecture/query-engine.md) for the wrapper contract and execution model.

## How it works

1. The Blazor wrapper reads a supported C# expression tree.
2. Magic converts it into a language-neutral predicate tree.
3. The browser planner checks available primary keys, indexes, and compound indexes.
4. Compatible branches run as native IndexedDB queries through Dexie.js.
5. Remaining branches use the cursor engine, with metadata-first selection when pagination or first/last selection requires it.
6. Results return as a materialized list or a progressive async stream.

This provides a LINQ-oriented programming model without pretending IndexedDB is SQL or in-memory LINQ. The differences are documented so query behavior remains explicit and predictable.

## Requirements

Magic IndexedDB remains on its version 2 release line. The current codebase targets .NET 10; applications that must remain on .NET 8 should use an earlier compatible package release.

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

Continue with [installation and configuration](docs/getting-started/installation.md), [schema setup](docs/getting-started/schema.md), and the [first complete workflow](docs/getting-started/first-application.md).

## Query behavior worth knowing

- Start with `Where(...)`; it allows the engine to choose indexed, compound-indexed, and cursor branches.
- Use `Cursor(...)` when you intentionally want cursor execution, such as scan-oriented text matching or stable cursor pagination.
- Magic's pagination chain is `Take(count).Skip(offset)` because of how its IndexedDB execution path composes limit and offset operations.
- `ToListAsync()` applies the requested materialized ordering.
- `AsAsyncEnumerable()` prioritizes progressive delivery and does not promise final arrival order across query branches.
- `CountAsync()` on the root query counts the whole table; it is not currently a filtered-count operator.

The [`Where` versus `Cursor`](docs/guides/where-vs-cursor.md) and [ordering and pagination](docs/guides/ordering-and-pagination.md) guides explain these contracts in detail.

## Documentation

The maintained documentation lives entirely in [`docs/`](docs/README.md):

- [Installation](docs/getting-started/installation.md)
- [Schema setup](docs/getting-started/schema.md)
- [First application workflow](docs/getting-started/first-application.md)
- [Querying guide](docs/guides/querying.md)
- [`Where` versus `Cursor`](docs/guides/where-vs-cursor.md)
- [Ordering and pagination](docs/guides/ordering-and-pagination.md)
- [Streaming results](docs/guides/streaming.md)
- [Database management](docs/guides/database-management.md)
- [Schema evolution](docs/guides/schema-evolution.md)
- [Public API reference](docs/reference/public-api.md)
- [Query expression reference](docs/reference/query-expressions.md)
- [Query engine architecture](docs/architecture/query-engine.md)

Version 1 documentation remains available in the [legacy archive](MagicIndexDbWiki/Version-1.0-Legacy.md).

## Schema evolution

The automated migration protocol is still under construction. Magic IndexedDB does not automatically migrate existing browser data when a C# model changes. Plan and test persisted-name, index, primary-key, and required-property changes against data produced by the previously released application.

See [schema evolution and migrations](docs/guides/schema-evolution.md) before changing a deployed schema.

## Contributing

Issues and pull requests are welcome. Changes to expression translation, serialization, schema handling, or the JavaScript query engine should include focused unit tests and browser end-to-end coverage where applicable.

## 🏆 Contributors Hall of Fame 🏆

Thank you to all contributors, whether large or small! This section is for the people who have put significant work, care, and energy into the project.

[@yueyinqiu](https://github.com/yueyinqiu) — I built this project in about two weeks in 2023, told nobody about it, then walked away and forgot about it. It was not until 2024 that I realized there were pull requests and tickets from other people. Yue provided significant contributions during that time and worked closely with me as we completed version 1 together. This project might have died without you, my friend, and you made it fun for me to come back and see it through. Together we finished version 1 and laid the foundation for version 2.

[@Ard2025](https://github.com/Ard2025) — Dude, you came out of left field in 2025 and became a powerhouse contributor! I swear you were a pest control exterminator in a past life because you just cannot stop killing bugs. You have also worked closely with me through valuable brainstorming sessions, major cleanup, refactoring, and much more since the version 2 alpha launch. Seriously, thank you—this project thrives because you are here.
