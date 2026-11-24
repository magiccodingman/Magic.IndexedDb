# How the query engine works

Magic IndexedDB is a translator and planner, not an implementation of `IQueryable<T>` over an in-memory collection. Its job is to preserve a supported C# predicate's intent while mapping as much work as possible onto IndexedDB.

## 1. Expression translation

The C# wrapper receives an `Expression<Func<T, bool>>`. `PredicateVisitor<T>` first expands supported captured `Any`/`All` shapes, including their empty-sequence truth values. `UniversalExpressionBuilder<T>` then maps supported binary expressions, method calls, nullable member access, dates, enums, constants, and logical composition into a `FilterNode` tree.

Persisted property mappings are resolved at this stage, so `[MagicName]` affects the property names sent to JavaScript.

## 2. Universal predicate tree

The tree contains two node types:

- A logical node with an `And` or `Or` operator and child nodes.
- A condition node with a property, operation, value, and string/case metadata.

This intermediate representation separates the language-specific expression parser from the IndexedDB planner. See [the universal predicate language](universal-predicate-language.md).

## 3. Normalization and flattening

JavaScript normalizes special cases such as null equality and flattens the tree into groups the planner can inspect. Compatible logical paths can be combined; incompatible paths remain separate so they can be assigned different execution strategies without changing the original truth conditions.

## 4. Partitioning

The planner builds index metadata from the Dexie table and classifies predicate groups as:

- Single-index queries
- Compound-index queries
- Cursor conditions

An AND group stays indexed only when its conditions can be represented by a compatible native or compound index path. A compound index is only a candidate producer: if the branch contains a residual predicate that the compound index does not cover, the current planner sends the complete branch to the cursor instead of dropping that predicate. OR alternatives may become independent query branches. Query additions such as take, skip, first, last, and ordering can require a combined cursor plan so that an addition applies once to the full logical result.

Calling `Cursor(...)` sets forced-cursor mode and bypasses index partitioning for that query.

## 5. Indexed-query optimization

Before execution, compatible index conditions may be compressed:

- Several equalities on one property can become `anyOf`.
- Compatible lower and upper bounds can become a range.
- Compound equality conditions can be reordered to match the declared compound index.
- Redundant query paths can be removed.

Independent indexed branches may execute concurrently. Results are de-duplicated using normalized primary keys before cursor results are appended.

## 6. Cursor execution

The cursor engine rebuilds its assigned conditions into a predicate tree and scans records in primary-key order.

There are two principal paths:

- Without pagination/first/last additions, matching full records are collected directly.
- When additions require selection or ordering, the engine collects the primary key and only the fields needed for filtering/order metadata, applies the additions, and then fetches the selected full records by primary key.

The cursor skips a row when it cannot evaluate the predicate because a required property is missing. If a new property will be queried, older records may need to be backfilled first.

`StableOrdering()` leaves inferred indexed predicate fields out of cursor ordering and uses the stable scan order instead. It cannot be combined with `OrderBy` in the same cursor chain.

One field inside a compound primary key is not independently orderable merely because it participates in that compound key. Ordering by such a component uses the cursor unless a standalone ordinary or unique index exists for the property.

## 7. Result transport

`ToListAsync()` collects the query into a list and applies the requested ordering.

`AsAsyncEnumerable()` uses the streaming path. .NET drains chunks while JavaScript produces them, and the engine removes duplicate primary keys. Records from separate query paths can arrive out of order.

The interop envelope has its own version. Arguments are sent as raw JSON elements rather than JSON strings inside JSON, which preserves values such as `0`, `false`, and `null` and avoids another parsing step. The JavaScript reader can also read the older envelope.

## Performance implications

- Selective indexes usually reduce scanned data most effectively.
- Compound indexes are valuable for common multi-field AND predicates.
- OR-heavy queries can fan out into multiple branches, although the optimizer can compress compatible alternatives.
- Cursor fallback is deliberately optimized, but it still scans records and cannot outperform a selective native index lookup in the general case.
- Streaming lowers returned-result pressure but the planner may still retain keys, metadata, de-duplication state, or a selected batch.

Measure representative browser workloads rather than assuming a LINQ expression's appearance predicts its exact IndexedDB plan.
