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

The standalone suite currently registers 122 cases. Installed CLI fixtures add
four, for 126 cases per browser, including authenticated transitions and recovery. The two CLI recovery cases use published neutral
schema snapshots where the affected store's optional `recordCodec` changes
between omission and explicit null in each direction.
The required jobs also load the reviewed, pinned v1 engine to verify it rejects
a recovered journal. For local compatibility verification, extract
`Magic.IndexedDb/wwwroot/migrations/{migrationEngine,protocol}.js` from
`fbb55aa1f2eed1a5f3138f952ac1038bc8a5b4ad` into a directory and set
`MAGIC_V1_ENGINE` to that directory.

## Validate the portable documents without a browser

```bash
node --test tests/migrations/protocol.test.mjs tests/migrations/protected-protocol.test.mjs \
  tests/migrations/protected-adapter.test.mjs tests/migrations/registration.test.mjs
```

These tests check canonical JSON and SHA-256, unsupported capabilities, unsafe
operation plans, destructive/protection-loss approval, exact target structure,
authenticated policy and journal proofs, and typed checkpoint identities.
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

The script also authors and publishes a six-revision authenticated history that
adds and removes field/whole-record protection, then creates a public index. Its
reviewed protection-loss decisions and capability retention are checked by the
installed tool and the JavaScript protocol. It also publishes both optional-codec recovery fixtures with
the installed tool. It checks that a real codec identity change is rejected by
recovery authoring and that the rejected attempt leaves source history unchanged.

## Upgrade the released runtime package in a Blazor app

With the Wasm workload and Playwright browsers installed, run this from the
repository root. Choose a new output directory for each build.

```bash
MAGIC_UPGRADE_AOT=both bash tests/migrations/ReleasedUpgrade/build.sh artifacts/released-upgrade
for mode in trimmed aot; do
  MAGIC_UPGRADE_REQUIRE_ALL=1 node tests/migrations/ReleasedUpgrade/run.mjs artifacts/released-upgrade \
    tests/migrations/BrowserDriver/bin/Release/net10.0/.playwright/package/index.mjs chromium "$mode"
done
```

Use `firefox` or `webkit` on supported platforms. The historical binaries run
in a .NET 10 fixture host. Build both modes with `MAGIC_UPGRADE_AOT=both`, or
select `false` for trimmed and `true` for AOT. For a short local probe, supply
specific historical versions after the output path and omit
`MAGIC_UPGRADE_REQUIRE_ALL`. The package versions for the locally packed runtime
and installed CLI come from their projects.

The seven stable 2.x packages create the data. Two pinned v3 releases on the
same origin then adopt at revision 1 and apply a CLI-generated revision 2 that
backfills and indexes invoices. Close/reopen checks verify data, exact converters
and Lossless values, native keys and schema, and one committed history entry.
A non-idempotent fixture transform verifies that reopening does not replay it.
Two additional 2.0.7 cases cover an uncooperative writer and a cooperating
tab/worker bridge that stays fenced after an injected initialization failure.

The migration workflow builds the historical and managed static assets once
and shares them with Chromium, Firefox, Linux WebKit and macOS WebKit jobs.
Each configuration requires thirteen cases per mode: seven adoption/forward
upgrades, two cutover cases, one explicit Blazor recovery, one authenticated
Blazor lifecycle seeded by the released 2.0.7 protected adapter, and two in-place
Lossless upgrades seeded by the released 2.0.7 protected and plain adapters. No skips are
allowed. The recovery fixture publishes static assets with the installed CLI,
causes a partial migration, cancels during verification, resumes and opens the
application's later ordinary target. It asserts disposal of the observer. The
authenticated fixture adopts field ciphertext, revokes a pending transform through
the C# lock API, distinguishes caller-wait and browser cancellation, checks absent
and invalid credentials and account switching, writes whole-record ciphertext, and explicitly
declassifies it while retaining authenticated history. It checks raw intermediate
storage, stale released writer fencing, and typed CRUD in both published modes.
The Lossless upgrades check compound keys, index definitions, exact wide-number
wire strings, converter-shaped values, and raw field or whole-record envelopes.
The runner uploads reports and build
metadata. The release workflow requires migration validation, including this gate.

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
- Abrupt recovery page termination while a repair hook or verifier is pending
  after a durable checkpoint. A second page resumes the same package and
  confirms committed work is preserved and ordinary opening is blocked until
  verification finishes.
- Concurrent tabs, blocked native upgrades, and stale application revisions.
- Cancellation while another tab blocks an upgrade; late open requests cannot
  perform an upgrade after cancellation is reported.
- Cancellation while a custom asynchronous transform or module initialization
  is pending; its eventual result cannot commit data after cancellation.
- Observer isolation, metadata-only event payloads, and checkpoint key redaction.
- Immutable schema inputs even while migration ownership is pending.
- Rejection of protected schemas by the unauthenticated plain runner.
- Lazy protected registration without provisioning storage or requesting assets
  before authorization; conflicting registration cannot replace an active vault.
- Authenticated plaintext/field/whole-record transitions, allowlist changes,
  explicit protection-loss decisions, stable physical enrollment, and exact
  policy/journal proofs across lock, resume, termination, and approved recovery.
- Authenticated preflight rejects restored metadata and numeric-policy damage.
  Controlled CSP and service-worker cache tests require verified historical bytes.
  Two tabs and a worker migrate 1,024 protected records once.
- Managed Lossless numeric-policy tampering, reopen behavior, reserved metadata,
  and prevention of unguarded database handles.

## Preview support and evidence matrix

The workflow definition and the presence of a scenario are not test results.
Use reports from the exact commit under review for execution evidence.

| Boundary | Preview behavior | Automated coverage and remaining evidence |
| --- | --- | --- |
| Plain managed schemas | Fresh target bootstrap, atomic changes, and resumable transforms with stable keys | Portable document tests plus native browser scenarios, including rollback, interruption, and multiple releases |
| Same-format Lossless storage | Keeps the reserved numeric namespace and persisted numeric policy | Existing numeric E2E fixtures plus managed registration, tamper, and reopen scenarios; released 2.0.7 Lossless adoption, forward migration and reopen |
| Existing protected bootstrap and CRUD | Retains legacy behavior without a manifest; managed contexts pin policy and journal proof | Existing encryption E2E and new managed registration/ciphertext/lock cases |
| Protected migration manifests | Lazy registration, explicit authorization, authenticated adoption/rewrites and bounded recovery | Pure Web Crypto/protocol tests, installed CLI assets, native transition/termination/tamper cases; exact-head browser and published-WASM results required |
| Legacy adoption | Requires an explicit baseline whose physical layout and codec policy match | Constructed native layouts and the separate released-package Blazor fixture |
| Codec changes, key rotation, key rebuilding, and cross-database transfers | No general-purpose adapter in this preview | Fail-closed scenarios establish rejection where applicable; successful transition coverage is not claimed |
| Application data meaning | Custom transformations and application assertions remain necessary | The engine does not infer business invariants or validate arbitrary converters |
| Offline, quota, device, and process failures | Durable transactions/checkpoints are the recovery mechanism | Page interruption is covered by authored browser scenarios; full process, real device, quota, and offline deployment evidence remains release work |

The authenticated adapter is required for protected records. Raw JSON transforms
cannot bypass this boundary. New acceptance includes both protection directions,
whole-record allowlists, explicit loss decisions, wrong authorization, journal
proof tampering, page termination, metadata deletion, and in-place enrollment.
The [handoff](../engineering/protected-migrations-handoff.md) identifies remaining
real-environment coverage and the distinction between authored and executed tests.

## Evidence boundaries and further release work

The portable browser tests construct legacy layouts through the native IndexedDB
API. The released-package Blazor fixture is separate and verifies representative
unprotected Legacy and Lossless paths. Test your own old deployments, converters,
and business invariants before promoting a release. Authenticated protection has
separate cryptographic, lifecycle, transition, and authenticated recovery coverage.
Its new real-browser/application acceptance is independent of the earlier plain
migration approval.

Page termination tests exercise loss of a JavaScript execution context, not an OS
power failure or physical disk failure. Add process termination, quota exhaustion,
large real datasets, fully offline PWA upgrades, and mixed old/new application
deployments on supported physical devices. Record actual results and limitations.

Authenticated schema/policy changes require the new extension's own end-to-end
acceptance. Data-key rotation, staging migrations that change primary keys, and
cross-database transfers remain unsupported. Rejection tests establish fail-closed
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
the existing NuGet publish job. The release job uses one trusted login for both packages.
The NuGet policy must allow the CLI package ID and its first publication.
