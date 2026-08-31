# Behavioral contract index

This page is a map to Magic IndexedDB's canonical behavior documentation. It is intended for readers who need to move quickly from a method or concern to its exact contract without reading the beginner material sequentially.

| Concern | Canonical documentation |
|---|---|
| Registration, table queries, staged interfaces, CRUD signatures, and database scope | [Public API reference](public-api.md) |
| Supported and unsupported expression-tree shapes | [Query expression reference](query-expressions.md) |
| Index planning versus forced cursor evaluation | [`Where` versus `Cursor`](../guides/where-vs-cursor.md) |
| Valid fluent order, stable ordering, pagination, and materialized order | [Ordering and pagination](../guides/ordering-and-pagination.md) |
| Progressive delivery, ordering limitations, chunks, and stream cancellation | [Streaming results](../guides/streaming.md) |
| Insert/update/delete semantics, return counts, bulk atomicity, and generated keys | [Writes, bulk operations, and transactions](writes-and-transactions.md) |
| Persisted CLR shapes, constructors, converters, and numeric precision | [Serialization and persisted types](serialization.md) |
| Failure stages, exception categories, cancellation coverage, and recovery | [Errors, cancellation, and recovery](errors-and-cancellation.md) |
| Browser engines, origin scope, quota, eviction, deletion, and multiple tabs | [Browser support, storage, and multiple tabs](browser-support-and-storage.md) |
| Keys, indexes, persisted names, ignored properties, and constructors | [Schema attributes and constructors](schema-attributes.md) |
| Deployed model changes and the current migration status | [Schema evolution and migrations](../guides/schema-evolution.md) |
| Predicate translation, partitioning, optimization, and result transport | [Query engine architecture](../architecture/query-engine.md) |
| Language-neutral predicate and schema representation | [Universal predicate language](../architecture/universal-predicate-language.md) |

## Guarantee labels

The documentation uses three kinds of statements:

- **Supported contract** describes behavior applications may intentionally depend on for the documented release line.
- **Current implementation behavior** records an important present limitation or distinction that may be improved in a later compatible or breaking release.
- **Not guaranteed** marks assumptions an application must not make, such as bulk rollback, final stream arrival order, generated-key assignment, or permanent browser retention.

When changing one of these contracts, update its canonical page, add or revise a regression test where practical, and include the documentation change in the same pull request.
