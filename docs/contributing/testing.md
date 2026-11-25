# Testing Magic IndexedDB

Magic IndexedDB has .NET tests for deterministic library behavior and browser tests for anything that depends on JavaScript, IndexedDB, Blazor interop, or streaming.

## .NET tests

```bash
dotnet test Magic.IndexedDb.UnitTests/Magic.IndexedDb.UnitTests.csproj --configuration Release
```

These tests cover expression translation, schema generation, serialization, streaming bookkeeping, result validation, documentation links, and the public .NET API.

The public API is stored in `Magic.IndexedDb.UnitTests/PublicApiBaseline.txt`. If an API change is deliberate, review the diff and regenerate the file with:

```bash
UPDATE_PUBLIC_API_BASELINE=1 dotnet test Magic.IndexedDb.UnitTests/Magic.IndexedDb.UnitTests.csproj --configuration Release --filter PublicApi_MatchesReviewedBaseline
```

Commit the updated baseline with the API change.

## Browser tests

Build the test project before installing Playwright:

```bash
dotnet build E2eTest/E2eTest.csproj --configuration Release
pwsh E2eTest/bin/Release/net10.0/playwright.ps1 install --with-deps chromium firefox webkit
```

Then run the suite in any browser you need:

```bash
dotnet test E2eTest/E2eTest.csproj --configuration Release --no-build -- Playwright.BrowserName=chromium Playwright.LaunchOptions.Headless=true
dotnet test E2eTest/E2eTest.csproj --configuration Release --no-build -- Playwright.BrowserName=firefox Playwright.LaunchOptions.Headless=true
dotnet test E2eTest/E2eTest.csproj --configuration Release --no-build -- Playwright.BrowserName=webkit Playwright.LaunchOptions.Headless=true
```

The browser suite covers database operations, query behavior, ordering and pagination, compound keys, serialization, streaming, cancellation, concurrent streams, and storage estimates.

CI runs both the .NET and browser suites. The workflow files under `.github/workflows/` are the source of truth for the exact jobs used by the repository.

## When adding tests

- Use .NET tests for translation, validation, and other deterministic behavior.
- Use browser tests when the result depends on IndexedDB, JavaScript, interop, or streaming.
- Compare ordered results item by item when testing ordering or pagination.
- Add a focused test for a bug so the same behavior cannot quietly return.
- Treat a public API baseline change as part of the API review, not as a routine test update.

## Check results across execution paths

The audit regression tests supplement the existing examples with comparisons against
independent expected results:

- `AuditExpressionRegressionTests` checks translated predicate behavior against
  compiled C# expressions, including nulls, captured values, conversions, and date
  boundaries. Unsupported member expressions must fail explicitly instead of
  querying a different property with the same name.
- `QueryPlannerRegressionTestExpressions` sends typed C# queries through real
  Blazor interop and compares both `Where` and forced `Cursor` results with in-memory
  LINQ. This catches mismatches between serialization, translation, and JavaScript.
- `QueryPlannerRegressionTestAudit` compares automatic planning and forced cursor
  execution with a separate reference evaluator using a fixed random seed. Its
  focused cases also cover contradiction handling, mixed OR branches, sparse
  indexes, compound lookups, ordering, and pagination. Positive planner assertions
  ensure complete indexes retain their native ordering path.
- `SerializationRegressionTests` and `SingleRecordBasicTestAudit` verify both value
  round trips and actual persisted keys. Include zero-valued explicit keys, compound
  keys, and a non-key property named `id`; checking only generated `id` keys can hide
  destructive write behavior.
- `AuditStreamingRegressionTests` tests cancellation with already-buffered results,
  late callbacks after disposal, incomplete records, and interop failure cleanup.

Keep the reference evaluator separate from the planner's comparison and optimization
helpers. A test that shares the same implementation can reproduce the same mistake.
In-memory IndexedDB shims are useful for local reproduction, but real-browser CI
remains the gate for IndexedDB transactions, indexes, and Blazor interop.

## Lossless numeric WebAssembly/AOT gate

The `WebAssembly AOT numeric integration` CI job publishes the small
`NumericWasmTest` host with `RunAOTCompilation=true` and runs it in Chromium. It
uses the same six typed scenarios as `QueryPlannerRegressionTestNumeric`, including
wide keys, native planner assertions, streaming, and protected public access.
The runner rejects a missing scenario or a non-browser runtime, and captures the
full failure plus browser logs when a scenario fails.

To reproduce with .NET's `wasm-tools` workload and Python 3 installed:

```bash
dotnet publish NumericWasmTest/NumericWasmTest.csproj -c Release -p:RunAOTCompilation=true -o artifacts/numeric-wasm
dotnet build E2eTest/E2eTest.csproj -c Release
node E2eTest/bin/Release/net10.0/.playwright/package/cli.js install --with-deps chromium
node .github/scripts/test-numeric-wasm.mjs
```

The same run reports three samples each for inserting 5,000 synthetic rows,
indexed ranges, and indexed ordering in Legacy and Lossless modes. It records
serialized UTF-8 payload size and median elapsed times without flaky timing
thresholds. Input values are exactly representable in both modes so the
comparison does not silently compare rounded Legacy data against exact data.
The timings include application serialization/interop and are not a universal
IndexedDB throughput claim. Results and publish logs are CI artifacts.

The [query planner diagnostics](query-planner-diagnostics.md) page explains how to inspect the browser planner when a query returns the wrong records or chooses an unexpected path.
