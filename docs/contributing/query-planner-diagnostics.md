# Query planner diagnostics

Debug mode records how the browser planned the most recent query. This is useful when a query returns the wrong records or you need to understand why it used an index or cursor.

Enable it during registration:

```csharp
builder.Services.AddMagicBlazorDB(
    BlazorInteropMode.WASM,
    isDebug: true);
```

## What the trace contains

Depending on the query, the trace can show:

- the predicate before and after logical flattening;
- optimizer input and output branch counts;
- whether the query was forced through a cursor;
- whether each branch used a single index, compound index, or cursor;
- which conditions a compound index handled and which remained;
- pagination branches being brought back together;
- physical index operations without including the query values;
- result counts and the final partition summary.

The JavaScript debug module exposes `getLastQueryPlannerTrace()` and `clearQueryPlannerTrace()`. Browser tests read the structured trace instead of parsing console output.

Only the latest query is retained. Concurrent queries can overwrite or interleave the trace, so run a query by itself when diagnosing it.

## Testing a planner problem

A good planner test checks the result first and the trace second:

1. Run the query against IndexedDB.
2. Run the equivalent predicate against the same records with in-memory LINQ.
3. Compare the returned records.
4. Inspect the trace to see where the browser plan diverged.

The records are the important result. Avoid asserting every detail of the chosen plan when more than one plan would return the same correct answer.

The .NET tests verify the predicate tree produced from C#. Browser tests then verify what happens after that tree reaches JavaScript and IndexedDB. This split helps narrow a failure to expression translation or browser execution.

The planner targets IndexedDB 2.0 behavior. It does not rely on features that only exist in the IndexedDB 3.0 draft.
