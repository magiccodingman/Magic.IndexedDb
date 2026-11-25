# Version 3 implementation and validation handoff

## Purpose and status

PR: https://github.com/magiccodingman/Magic.IndexedDb/pull/147

Branch: `feat/v3-managed-migrations`, based on the repository's default branch
`master` at `b8036f2` (including the recent correctness, encryption, and lossless
numeric work). This is a development preview for the next major release:
`Magic.IndexedDb` and `Magic.IndexedDb.Cli` use `3.0.0-preview.1`; protocol v1 and
application revisions are independent. Stable release is intended to be 3.0.0 after
acceptance. The PR is deliberately draft until local browser/application validation
and remaining release decisions are complete.

The user authorized implementation, documentation, tests, and a PR. They did not ask
to merge or publish packages. They will configure the separate CLI NuGet publisher.

## Implemented boundaries

- One C# membership/model resolver used by runtime and opt-in design-time exporter.
- Default table routing, explicit includes/excludes, explicit-only membership, stable
  schema IDs, pinned manifests, and separately named plain/lossless instances.
- Installed .NET tool, neutral JSON input, isolated exporter process, deterministic
  snapshots/diffs, automatic revisions, manual review placeholders, baseline creation,
  full-history validation, immutable published bytes/reference lock, and static output.
- Browser protocol: direct fresh bootstrap, explicit legacy adoption, complete-path
  preflight, native atomic operations, resumable data-only batches, transactional
  checkpoints, immutable applied history, blocked/stale handling, and metadata-only
  diagnostic events/inspection.
- Existing CRUD/LINQ routing connected to readiness. The lossless numeric policy
  remains checked; encrypted migration requests are explicitly rejected before writes.
- C# observation, inspection, cancellation, retry/resume APIs, README/upgrade/protocol/
  schema/CLI documentation, unit/CLI/package checks, and a real-browser CI matrix.

## Explicitly unfinished features and limits

Do not describe these as supported or bypass their guards to get a test green:

1. Authenticated protected baseline adoption and record/schema migrations, key rotation,
   encryption policy changes, and protected codec changes need a dedicated adapter.
   Existing protected CRUD is retained. Supplying a protected migration manifest fails.
2. Resumable key-changing migrations, structural staging/swap, cross-database movement,
   numeric codec conversion, and automatic downgrade/rollback are unsupported.
3. There is no generic repair operation that replaces a partially executed resumable
   migration with different code. The original hash is required to resume. Design and
   validate any emergency repair protocol before stable release if it is required.
4. Fresh bootstrap creates schema only, with no historical data/seed hook replay.
   History pruning/compaction needs an explicit future retention contract.
5. The CLI's project input is an opt-in executable exporter, not arbitrary Roslyn
   inspection of an app. A future language front end can emit the neutral schema.
6. Schema validation proves identities/layout/plans, not business meaning or every
   record invariant. Custom transforms and legacy baselines need realistic fixtures.
7. Custom modules must be self-contained and need CSP `blob:` permission. Pin/cache
   immutable assets deliberately for offline use. Magic does not manage service workers.

## Evidence and commands

The creating environment can execute .NET unit/build/package checks and Node protocol
checks. Its process sandbox rejects Chromium startup (socket/ptrace permissions), so
no local real-browser scenario should be counted as a pass merely because the harness
was built. Use the PR's GitHub Actions artifacts and your local machine for browser
execution evidence. See [migration testing](../contributing/migration-testing.md).

```sh
dotnet test Magic.IndexedDb.UnitTests/Magic.IndexedDb.UnitTests.csproj -c Release
bash .github/scripts/test-migration-cli.sh
node --test tests/migrations/protocol.test.mjs
dotnet build tests/migrations/BrowserDriver/BrowserDriver.csproj -c Release
node tests/migrations/BrowserDriver/bin/Release/net10.0/.playwright/package/cli.js install --with-deps chromium firefox webkit
MAGIC_CLI_FIXTURE=artifacts/migration-cli/published/cli-fixture/manifest.json node tests/migrations/run.mjs chromium tests/migrations/BrowserDriver/bin/Release/net10.0/.playwright/package/index.mjs artifacts/migration-browser/chromium
```

Repeat the final command for Firefox and WebKit. Run the existing complete browser
and .NET workflows too, including WASM/AOT and numeric/encryption tests. The isolated
protocol driver does not replace Blazor integration coverage.

For environments where MSBuild worker sockets are blocked, .NET commands may need
`-m:1 -nodeReuse:false -p:UseSharedCompilation=false` and
`DOTNET_CLI_USE_MSBUILD_SERVER=0`. Browser startup restrictions cannot be represented
as successful execution or worked around by weakening application safeguards.

## Next agent's task

Use your existing source directory. Fetch the PR branch, preserve unrelated local
work, and work on `feat/v3-managed-migrations`. Read the PR description, this file,
[the protocol](../architecture/migration-protocol.md), [upgrade guide](../upgrading/v3.md),
and [testing guide](../contributing/migration-testing.md) before changing behavior.

First reproduce all CI and local browser results. Record exact source SHA, OS,
.NET SDK, browser/Playwright versions, commands, executed counts, skips, infrastructure
failures, and artifact paths. Do not report a validation-only pass as a browser pass.
Fix concrete defects and add regressions for the failure rather than loosening gates.

Then test actual 2.x installations using published historical packages, realistic
records, complete formerly all-table layouts, legacy converters, native versions,
lossless policies, and protected storage. The constructed-layout tests in this branch
do not prove every released package upgrade path. Verify multiple project exporters,
multiple logical schemas, tenant instances, custom codecs, trimmed/AOT builds, and
fresh bootstrap compared with every supported upgrade path.

Stress interrupted atomic and resumable work, repeated crashes around checkpoints,
concurrent tabs/workers, deliberately blocked native upgrades, quota errors, stale
apps/service workers, offline/partially cached assets, tampered metadata, incomplete
release uploads, and immutable applied/pending migration bytes. Check that diagnostics
contain no record values/keys and disposed event handlers do not remain subscribed.

Review unsupported capabilities as explicit release decisions. If implementing one,
require a complete adapter/protocol design and meaningful browser evidence. Do not
remove a protection check or introduce an automatic reset. Recovery for faulty
partially committed migrations deserves a specific design and tests before claiming
production readiness.

Keep commits on the PR branch. Do not merge, publish NuGet, create a stable release
tag, or enable CLI publishing credentials. The repository's release workflow already
requires migration validation; the user will prepare the separate CLI publisher.

## Required response for peer review

Return a concise executive status followed by:

1. PR/branch and exact tested commit SHA; any later untested changes.
2. Test matrix with pass/fail/skip/executed counts, commands, environments, and evidence.
3. Every bug found: reproducer, original failure, root cause, fix, regression, commit.
4. Data-preservation findings: old package/baseline versions, rows/invariants tested,
   atomic rollback, resumable restart, native schema/policy/journal consistency.
5. API/protocol decisions or compatibility changes needing the maintainer's judgment.
6. Remaining blockers and unsupported operations, separated from optional polish.
7. A merge-readiness recommendation with concrete evidence; do not merge yourself.

The user will bring that response back to the original agent for an independent peer
review and another iteration.
