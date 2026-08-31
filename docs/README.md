# Magic IndexedDB documentation

Magic IndexedDB is a typed IndexedDB library for Blazor with a LINQ-style query API. It translates supported C# predicates into browser queries, uses indexes when possible, and falls back to cursor filtering when necessary.

The current version targets .NET 10. If your application still targets .NET 8, use an earlier compatible package.

Magic does not load an entire table into .NET just to apply a filter. Query planning and filtering happen in the browser, close to IndexedDB. This is especially useful for compound indexes, larger datasets, pagination, and predicates that combine `&&` and `||`.

IndexedDB is not SQL, so some query chains work differently from ordinary LINQ. The guides explain those differences where you are likely to encounter them.

Start with the four pages below. The guides and reference pages are there when you need to go deeper.

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
- [Adding, updating, and deleting records](reference/writes-and-transactions.md)
- [Serialization](reference/serialization.md)
- [Errors and cancellation](reference/errors-and-cancellation.md)
- [Browser storage and multiple tabs](reference/browser-support-and-storage.md)

## Architecture

- [How the query engine works](architecture/query-engine.md)
- [Universal predicate language](architecture/universal-predicate-language.md)

## Contributing

- [Testing Magic IndexedDB](contributing/testing.md)
- [Query planner diagnostics](contributing/query-planner-diagnostics.md)

## Upgrading and legacy versions

- [.NET 10 upgrade notes](upgrading/dotnet-10.md)
- [Version 1 documentation](../MagicIndexDbWiki/Version-1.0-Legacy.md)

## A few things to know

- Magic does not currently migrate existing data when a schema changes. Plan schema changes with the [schema evolution guide](guides/schema-evolution.md).
- You cannot call `OrderBy` after `Where`. Use `Cursor(predicate).OrderBy(...)`, or load the filtered records and sort them in .NET.
- `ToListAsync()` returns a materialized result with the requested query ordering applied.
- `AsAsyncEnumerable()` delivers records progressively, so records from different query branches may arrive out of order. Buffer and sort them if order matters.
- Application code should use the C# API. The JavaScript query modules are implementation details.
