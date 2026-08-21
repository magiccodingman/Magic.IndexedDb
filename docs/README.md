# Magic IndexedDB documentation

Magic IndexedDB brings a LINQ-style query provider to IndexedDB for Blazor. It translates C# expression trees into an IndexedDB-aware query plan, uses indexes and compound indexes when it can, and falls back to its cursor engine when the requested operation cannot be expressed as an indexed lookup.

Version 3 targets .NET 10. Applications that must remain on .NET 8 should use the 2.x NuGet line.

## Why it exists

A thin IndexedDB wrapper can offer methods that look like LINQ while still loading a large collection before filtering it. Magic IndexedDB instead treats the expression as query intent: it parses the predicate, partitions compatible branches, compresses index operations, and asks IndexedDB for the narrowest result it can produce.

That distinction matters for complex AND/OR predicates, compound indexes, pagination, and browser memory. When an operation cannot use an index, Magic's cursor engine evaluates it in the browser and, when selection additions require it, retains only the key and ordering metadata needed before fetching final records.

The engine uses Dexie.js for mature IndexedDB access while keeping query translation, optimization, serialization, and the C# fluent contract in Magic IndexedDB. Its universal predicate representation is also the architectural foundation for possible future wrappers in other languages.

Like LINQ to SQL, LINQ to IndexedDB has provider-specific rules. Reading the ordering, cursor, and schema-evolution guides is part of using the engine effectively.

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

- [Public API reference](reference/public-api.md)
- [Schema attributes and constructors](reference/schema-attributes.md)
- [Query expression reference](reference/query-expressions.md)

## Architecture

- [How the query engine works](architecture/query-engine.md)
- [Universal predicate language](architecture/universal-predicate-language.md)

## Upgrading and legacy versions

- [Upgrade to version 3](upgrading/version-3.md)
- [Version 1 documentation](../MagicIndexDbWiki/Version-1.0-Legacy.md)

## Important version 3 status

- Automated schema migrations are still under construction. Treat schema changes as explicit application work and test them against existing browser data.
- `ToListAsync()` returns a materialized result with the requested query ordering applied.
- `AsAsyncEnumerable()` prioritizes progressive delivery and does not promise final arrival order. Materialize and sort afterward when order is part of your application contract.
- The C# API is the supported integration surface. The universal predicate representation is documented for contributors and future wrappers, but the JavaScript modules are currently internal package implementation details.
