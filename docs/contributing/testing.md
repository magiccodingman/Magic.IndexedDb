# Testing Magic IndexedDB

Magic IndexedDB crosses C#, JavaScript interop, query planning, serialization, streaming, IndexedDB, and browser-specific behavior. The test system therefore has two required layers: deterministic .NET contract tests and browser integration tests against the actual Blazor application.

## Run the .NET tests

```bash
dotnet test Magic.IndexedDb.UnitTests/Magic.IndexedDb.UnitTests.csproj --configuration Release
```

These tests cover expression translation, schema generation and validation, serialization boundaries, chunked-stream bookkeeping, result validation, and a snapshot of the public .NET API. The API snapshot catches accidental additions, removals, and signature changes.

If a public API change is deliberate, review the complete diff first and then regenerate the snapshot explicitly:

```bash
UPDATE_PUBLIC_API_BASELINE=1 dotnet test Magic.IndexedDb.UnitTests/Magic.IndexedDb.UnitTests.csproj --configuration Release --filter PublicApiMatchesApprovedBaseline
```

Commit the reviewed `Magic.IndexedDb.UnitTests/PublicApiBaseline.txt` change with the implementation.

## Run the browser tests

Build the tests before installing Playwright so its generated installer is available:

```bash
dotnet build E2eTest/E2eTest.csproj --configuration Release
pwsh E2eTest/bin/Release/net10.0/playwright.ps1 install --with-deps chromium firefox webkit
```

Run one browser at a time:

```bash
dotnet test E2eTest/E2eTest.csproj --configuration Release --no-build -- Playwright.BrowserName=chromium Playwright.LaunchOptions.Headless=true
dotnet test E2eTest/E2eTest.csproj --configuration Release --no-build -- Playwright.BrowserName=firefox Playwright.LaunchOptions.Headless=true
dotnet test E2eTest/E2eTest.csproj --configuration Release --no-build -- Playwright.BrowserName=webkit Playwright.LaunchOptions.Headless=true
```

The suite validates CRUD and range operations, database lifecycle and isolation, query expressions, pagination and exact materialized ordering, compound keys, enum and dictionary serialization, constrained multi-chunk streaming, cancellation, concurrent streams, and quota access.

## Continuous integration

Every pull request runs:

- `Core validation`
- `Chrome integration`
- `Firefox integration`
- `Linux WebKit integration`
- `macOS WebKit integration`

Pushes to `master` run the same workflows again. A push to `release` starts `publish-nuget.yml`, which calls both validation workflows and does not build or publish the NuGet package until every validation job succeeds. This makes the release run a final independent gate even when the same commit passed on `master`.

The macOS WebKit job is valuable coverage for Apple's browser engine, but Playwright's WebKit build is not the branded Safari application and is not an iPhone or iPad device. Real Safari and iOS device coverage requires a separate device service or owned Apple test hardware; it should be added when credentials and a stable device-testing provider are available.

## Test design rules

- Test public behavior and compatibility contracts, not incidental implementation details.
- Compare ordered results positionally whenever ordering or pagination is under test.
- Keep unit tests deterministic and use browser tests for behavior that depends on JavaScript, IndexedDB, Blazor interop, or streaming.
- Add a regression test before or with every bug fix.
- Treat a public API snapshot update as an intentional compatibility decision, never routine test maintenance.
