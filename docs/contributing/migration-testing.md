# Testing the 3.0 migration lifecycle

The migration contract suite uses actual IndexedDB in Chromium, Firefox, Linux
WebKit, and macOS WebKit. It loads the public JavaScript module directly from
static files. Neither Blazor nor a simulated IndexedDB implementation participates
in those tests. Existing .NET and browser query/CRUD tests remain separate gates.

## Run the browser contract suite

Install the .NET SDK selected by `global.json` and Node.js 22.16 or newer. From the
repository root:

```bash
dotnet build tests/migrations/BrowserDriver/BrowserDriver.csproj --configuration Release
node tests/migrations/BrowserDriver/bin/Release/net10.0/.playwright/package/cli.js install --with-deps chromium
node tests/migrations/run.mjs chromium tests/migrations/BrowserDriver/bin/Release/net10.0/.playwright/package/index.mjs
```

Use `firefox` or `webkit` in both commands for another engine. macOS does not need
`--with-deps`. The small driver project supplies the same pinned Playwright version
as the existing integration tests; it does not ship with either NuGet package.
An already-built `E2eTest` driver can also be passed as the second argument.
For a deliberately selected local browser installation, set
`MAGIC_BROWSER_EXECUTABLE=/absolute/path/to/browser`; CI uses the pinned Playwright
browser builds instead.

Each scenario gets a fresh browser context. Multi-tab and page-termination tests
keep the same context so storage survives closing a page. Every scenario has a
deadline; an upgrade blocked forever is a failure. The process fails if scenarios
fail, if an unhandled browser exception appears, or if the expected scenario count
is not executed. JSON and JUnit reports, failure logs, and screenshots are written
under `artifacts/migration-browser` (or the optional third argument).

## Validate the portable documents without a browser

```bash
node --test tests/migrations/protocol.test.mjs
```

These tests check canonical JSON and SHA-256, unsupported capabilities, unsafe
operation plans, destructive-change approval, and exact target structure.
`tests/migrations/canonical-vectors.json` is a shared language-neutral corpus for
the .NET and JavaScript serializers. It includes Unicode, lone surrogates,
ordinal property order, and JavaScript numeric formatting boundaries. Passing
these tests establishes document compatibility, not IndexedDB transaction or
browser behavior.

## Test the shipped CLI package

```bash
bash .github/scripts/test-migration-cli.sh
MAGIC_CLI_FIXTURE=artifacts/migration-cli/published/cli-fixture/manifest.json \
  node tests/migrations/run.mjs chromium tests/migrations/BrowserDriver/bin/Release/net10.0/.playwright/package/index.mjs
```

The script packs and installs the actual `Magic.IndexedDb.Cli` nupkg into a clean
tool directory. Its generated static package is then consumed by the browser
runner. This boundary test detects disagreements between CLI hashes/schema
serialization and JavaScript protocol interpretation that either side's unit
tests could miss.

## What the suite checks

- Fresh bootstrap at a target revision without downloading historical transforms.
- Identical schema definitions bound to independent physical database names.
- Repeated readiness without replaying completed transformations.
- Clients skipping releases, full-path preflight before the first write, and
  retrying a later failed migration without replaying earlier committed releases.
- Unsupported versions/capabilities, missing assets, altered bytes, unresolved
  operations, conflicting histories, and runtime/manifest model disagreement.
- Atomic backfills and index creation, unique-index conflicts, transform errors,
  forbidden asynchronous atomic hooks, and transaction rollback.
- Explicit approval for store deletion; no implicit destructive recovery.
- Field renames and destination collisions; unsafe field names; resumable key
  changes rejected instead of corrupting cursor checkpoints.
- Legacy physical-layout validation, including native version 10, extra stores,
  and missing indexes.
- Data and checkpoint durability after a failed batch, cancellation, or page
  termination; retry without double-applying earlier batches.
- Concurrent tabs, blocked native upgrades, and stale application revisions.
- Cancellation while another tab blocks an upgrade; late open requests cannot
  perform an upgrade after cancellation is reported.
- Cancellation while a custom asynchronous transform or module initialization
  is pending; its eventual result cannot commit data after cancellation.
- Observer isolation, metadata-only event payloads, and checkpoint key redaction.
- Immutable schema inputs even while migration ownership is pending.
- Rejection of protected schemas by the unauthenticated plain runner.
- Rejection of protected manifests through the public registration API before
  provisioning or changing a vault; authenticated CRUD and ciphertext survive
  the rejected request.
- Managed Lossless numeric-policy tampering, reopen behavior, reserved metadata,
  and prevention of unguarded database handles.

## Preview support and evidence matrix

The workflow definition and the presence of a scenario are not test results.
Use reports from the exact commit under review for execution evidence.

| Boundary | Preview behavior | Automated coverage and remaining evidence |
| --- | --- | --- |
| Plain managed schemas | Fresh target bootstrap, atomic changes, and resumable transforms with stable keys | Portable document tests plus native browser scenarios, including rollback, interruption, and multiple releases |
| Same-format Lossless storage | Keeps the reserved numeric namespace and persisted numeric policy | Existing numeric E2E fixtures plus managed registration, tamper, and reopen scenarios; historical package upgrade fixtures still required |
| Existing protected bootstrap and CRUD | Continues through the authenticated storage path | Existing encryption E2E fixtures plus ciphertext-preservation coverage after a rejected manifest |
| Protected migration manifests | Explicitly rejected with `MAGDB_PROTECTED_MIGRATION_UNSUPPORTED` before provisioning or writes | Public registration rejection tests; no authenticated migration execution is claimed |
| Legacy adoption | Requires an explicit baseline whose physical layout and codec policy match | Constructed native layouts, including native version 10; released-package origin/profile tests remain required |
| Codec changes, key rotation, key rebuilding, and cross-database transfers | No general-purpose adapter in this preview | Fail-closed scenarios establish rejection where applicable; successful transition coverage is not claimed |
| Application data meaning | Custom transformations and application assertions remain necessary | The engine does not infer business invariants or validate arbitrary converters |
| Offline, quota, device, and process failures | Durable transactions/checkpoints are the recovery mechanism | Page interruption is covered by authored browser scenarios; full process, real device, quota, and offline deployment evidence remains release work |

Protected records must never be routed through raw JSON transforms to bypass the
guard. A future authenticated adapter must verify the existing policy, work with
the caller's key/session lifecycle, and authenticate any resulting policy before
protected migration support is advertised. Supplying a manifest currently means
an error; it is never silently ignored.

## Evidence boundaries and further release work

The legacy tests construct documented 2.x physical layouts through the native
IndexedDB API. They are **not** evidence of running every historical published
Magic package. Before a stable 3.0 release, add fixtures that run the supported
published 2.x packages, preserve the exact origin/profile, and upgrade the app to
3.0. Include Legacy and Lossless numeric namespaces and authenticated encryption
policies. A generic sample layout cannot establish historical converter semantics.

Page termination tests exercise loss of a JavaScript execution context, not an OS
power failure or physical disk failure. Add process termination, quota exhaustion,
large real datasets, fully offline PWA upgrades, and mixed old/new application
deployments on supported physical devices. Record actual results and limitations.

Protected schema/policy changes, key rotation, staging migrations that change
primary keys, and cross-database transfers require dedicated end-to-end tests
before they can be advertised as supported. Rejection tests establish fail-closed
behavior, not successful transformation coverage.

The engine verifies physical schema, journal state, and transform output/key
requirements. It cannot infer application invariants or prove that a custom
converter preserves every business rule. Add fixture assertions for required
fields, enums, serializer choices, relationships, and application-specific data
meaning alongside each custom migration.

Asynchronous resumable transforms receive `context.signal`. Use it for fetches
or other cancellable work. Cancelling the runner releases migration ownership
and prevents a late returned value from committing; JavaScript cannot forcibly
terminate an arbitrary promise or undo side effects performed by custom code.
Custom transforms should have no external side effects and should cooperate
with the signal when possible.

The release workflow calls `validate-migrations.yml` as a required dependency of
the existing NuGet publish job. CLI publication itself remains a separately
configured release task; this change neither supplies publishing credentials nor
publishes a package from a PR.
