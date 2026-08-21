# How the query engine works

Magic IndexedDB is a translator and planner, not an implementation of `IQueryable<T>` over an in-memory collection. Its job is to preserve a supported C# predicate's intent while mapping as much work as possible onto IndexedDB.

## 1. Expression translation

The C# wrapper receives an `Expression<Func<T, bool>>`. `UniversalExpressionBuilder<T>` maps supported binary expressions, method calls, nullable member access, dates, enums, and logical composition into a `FilterNode` tree.

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

An AND group stays indexed only when its conditions can be represented by a compatible native or compound index path. OR alternatives may become independent query branches. Query additions such as take, skip, first, last, and ordering can require a combined cursor plan so that an addition applies once to the full logical result.

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

Rows missing a property required by the cursor predicate are skipped because the predicate cannot be evaluated reliably for that row. This makes additive schema compatibility an application concern; it is not a substitute for migrations.

`StableOrdering()` suppresses inferred indexed predicate fields in cursor metadata ordering and uses the cursor's stable scan order. The current fluent surface does not combine `StableOrdering()` with an explicit `OrderBy` in the same cursor chain.

## 7. Result transport

`ToListAsync()` collects the query into a materialized list and applies the requested materialized ordering contract.

`AsAsyncEnumerable()` uses the streamed interop path. The .NET consumer drains chunks while JavaScript produces them, and duplicate primary keys are filtered by the engine. Progressive delivery does not promise final arrival order across query paths.

The internal interop envelope is versioned. Version 3 sends arguments as raw JSON elements rather than JSON strings nested inside JSON, preserving falsey values and avoiding extra parsing. The JavaScript reader retains support for the earlier envelope.

## Performance implications

- Selective indexes usually reduce scanned data most effectively.
- Compound indexes are valuable for common multi-field AND predicates.
- OR-heavy queries can fan out into multiple branches, although the optimizer can compress compatible alternatives.
- Cursor fallback is deliberately optimized, but it still scans records and cannot outperform a selective native index lookup in the general case.
- Streaming lowers returned-result pressure but the planner may still retain keys, metadata, de-duplication state, or a selected batch.

Measure representative browser workloads rather than assuming a LINQ expression's appearance predicts its exact IndexedDB plan.
