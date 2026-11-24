# Testing Magic IndexedDB

Magic IndexedDB has .NET tests for deterministic library behavior and browser tests for anything that depends on JavaScript, IndexedDB, Blazor interop, or streaming.

## .NET tests

```bash
dotnet test Magic.IndexedDb.UnitTests/Magic.IndexedDb.UnitTests.csproj --configuration Release
```

These tests cover expression translation, schema generation, serialization, streaming bookkeeping, result validation, documentation links, and the public .NET API.

The public API is stored in `Magic.IndexedDb.UnitTests/PublicApiBaseline.txt`. If an API change is deliberate, review the diff and regenerate the file with:

```bash
UPDATE_PUBLIC_API_BASELINE=1 dotnet test Magic.IndexedDb.UnitTests/Magic.IndexedDb.UnitTests.csproj --configuration Release --filter PublicApiMatchesApprovedBaseline
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

The [query planner diagnostics](query-planner-diagnostics.md) page explains how to inspect the browser planner when a query returns the wrong records or chooses an unexpected path.
