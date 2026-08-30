# Query planner diagnostics

Magic IndexedDB debug mode now records a structured trace of the planning path used by the most recent query. The trace exists for development and test diagnostics only. It does not participate in query planning and must never change query semantics.

## Compatibility contract

Planner diagnostics and regression tests target the library's supported IndexedDB 2.0 behavior. IndexedDB 3.0 remains a draft and is not used as a correctness assumption or as a way to bypass IndexedDB 2.0 limitations.

## What the trace records

When `AddMagicBlazorDB(..., isDebug: true)` is used, the JavaScript planner records stages such as:

- source predicate and logical flattening;
- optimizer dispatch and before/after branch counts;
- whether the advanced optimizer received the shape it expects;
- cursor-forcing decisions;
- per-branch strategy selection (`single-index`, `compound-index`, or `cursor`);
- how many conditions a compound index consumed and how many remain outside the selected index;
- pagination reconvergence, including invalid or undefined cursor entries;
- indexed physical optimization, including input branch counts and output operations without recording query values;
- indexed or cursor execution selection and result counts where execution reaches completion;
- the final partition summary.

The physical-optimization stage is particularly useful for detecting semantic drift between logical planning and Dexie execution. For example, it can show that one logical AND branch containing two indexed conditions became two independent physical index queries, or that two prefix branches were rewritten into a single `In` operation.

The debug module exposes `getLastQueryPlannerTrace()` and `clearQueryPlannerTrace()` for diagnostics. Browser regression tests consume this structured object rather than parsing console text.

The current implementation is intentionally a lightweight **last-query development probe**, not a concurrent tracing subsystem. Overlapping queries can replace or interleave the global diagnostic trace. Tests therefore execute traced queries independently. If concurrent trace correlation becomes necessary, trace identity should be propagated through the planner rather than making planning depend on global diagnostic state.

## Testing philosophy

Planner tests use two independent checks:

1. **Semantic oracle:** execute the real query through IndexedDB/Dexie and compare the returned records with the equivalent in-memory LINQ predicate.
2. **Planner evidence:** inspect the structured trace to prove the intended predicate structure reached the relevant planning boundary and include the trace in semantic failure output.

Planner evidence should avoid turning a particular optimization strategy into a permanent behavioral contract when multiple correct physical plans are possible. The semantic result remains authoritative.

The C# contract suite separately verifies that the expression builder sends the intended boolean topology and all predicate conditions into the JavaScript planner. This establishes a useful correctness boundary: if the C# semantic tree is correct but the browser regression differs from the LINQ oracle, the defect lies after expression translation.

## Intentional red regressions

The initial diagnostics PR intentionally contains failing browser regressions for currently reproduced planner defects. It also invokes the currently dormant advanced optimizer directly with synthetic truth tables so optimizer rewrite laws can be validated independently of whether the live planner dispatches that optimizer correctly.

The tests are designed to become green only when the corresponding implementation defects are fixed in a follow-up change. Keeping reproduction and repair separate demonstrates that each regression test detects the pre-existing behavior rather than merely validating the eventual implementation.
