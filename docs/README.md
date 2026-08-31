# Magic IndexedDB documentation

Magic IndexedDB brings a LINQ-style query provider to IndexedDB for Blazor. It translates C# expression trees into an IndexedDB-aware query plan, uses indexes and compound indexes when it can, and falls back to its cursor engine when the requested operation cannot be expressed as an indexed lookup.

Magic IndexedDB remains on its version 2 release line. The current codebase targets .NET 10; applications that must remain on .NET 8 should use an earlier compatible package release.

## Why it exists

A thin IndexedDB wrapper can offer methods that look like LINQ while still loading a large collection before filtering it. Magic IndexedDB instead treats the expression as query intent: it parses the predicate, partitions compatible branches, compresses index operations, and asks IndexedDB for the narrowest result it can produce.

That distinction matters for complex AND/OR predicates, compound indexes, pagination, and browser memory. When an operation cannot use an index, Magic's cursor engine evaluates it in the browser and, when selection additions require it, retains only the key and ordering metadata needed before fetching final records.

The engine uses Dexie.js for mature IndexedDB access while keeping query translation, optimization, serialization, and the C# fluent contract in Magic IndexedDB. Its universal predicate representation is also the architectural foundation for possible future wrappers in other languages.

Like LINQ to SQL, LINQ to IndexedDB has provider-specific rules. Reading the ordering, cursor, and schema-evolution guides is part of using the engine effectively.

The documentation is layered deliberately. The start-here path is enough to become productive; guides explain everyday decisions; reference and architecture pages provide the complete behavioral depth for advanced users. You do not need to read every page before creating a table.

## Start here

1. [Install Magic IndexedDB](getting-started/installation.md)
2. [Define a repository and table schema](getting-started/schema.md)
3. [Build your first database workflow](getting-started/first-application.md)
4. [Learn the query syntax](guides/querying.md)

## Guides

- [Querying](guides/querying.md) — filtering, execution methods, single-record queries, and supported expression shapes
- [`Where` versus `Cursor`](guides/where-vs-cursor.md) — when Magic can attempt index optimization and when to request a cursor explicitly
- [Ordering and pagination](guides/ordering-and-pagination.md) — `OrderBy`, `Take`, `Skip`, `TakeLast`, and `StableOrdering`
- [Streaming results](guides/streaming.md) — choosing between `ToListAsync()` and `AsAsyncEnumerable()`
- [Database management](guides/database-management.md) — open, close, delete, existence, and quota operations
- [Schema evolution](guides/schema-evolution.md) — the current migration status and how to change persisted models safely

## Reference

- [Behavioral contract index](reference/behavioral-contracts.md) — direct map from an operation or concern to its canonical contract
- [Public API reference](reference/public-api.md)
- [Schema attributes and constructors](reference/schema-attributes.md)
- [Query expression reference](reference/query-expressions.md)
- [Writes, bulk operations, and transactions](reference/writes-and-transactions.md)
- [Serialization and persisted types](reference/serialization.md)
- [Errors, cancellation, and recovery](reference/errors-and-cancellation.md)
- [Browser support, storage, and multiple tabs](reference/browser-support-and-storage.md)

## Architecture

- [How the query engine works](architecture/query-engine.md)
- [Universal predicate language](architecture/universal-predicate-language.md)

## Contributing

- [Testing and continuous integration](contributing/testing.md)
- [Query planner diagnostics](contributing/query-planner-diagnostics.md)
- [Maintaining the documentation contract](contributing/documentation-contracts.md)

## Upgrading and legacy versions

- [.NET 10 upgrade notes](upgrading/dotnet-10.md)
- [Version 1 documentation](../MagicIndexDbWiki/Version-1.0-Legacy.md)

## Important current-release status

- Automated schema migrations are still under construction. Treat schema changes as explicit application work and test them against existing browser data.
- `Where(...)` returns a staged query without `OrderBy`; use the documented cursor or in-memory alternatives for a filtered ordered result.
- `ToListAsync()` returns a materialized result with the requested query ordering applied.
- `AsAsyncEnumerable()` prioritizes progressive delivery and does not promise final arrival order. Materialize and sort afterward when order is part of your application contract.
- The C# API is the supported integration surface. The universal predicate representation is documented for contributors and future wrappers, but the JavaScript modules are currently internal package implementation details.
