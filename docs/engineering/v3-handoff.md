# Version 3 implementation and validation handoff

## Purpose and status

PR: https://github.com/magiccodingman/Magic.IndexedDb/pull/147

Branch: `feat/v3-managed-migrations`, based on the repository's default branch
`master` at `b8036f2` (including the recent correctness, encryption, and lossless
numeric work), then merged with `5acef19` to include the subsequently merged
projection/record-DTO work from PR #145. Its custom converter fixture explicitly
declares the new versioned storage codec. This is a development preview for the next major release:
`Magic.IndexedDb` and `Magic.IndexedDb.Cli` use `3.0.0-preview.1`; protocol v1 and
application revisions are independent. Stable release is intended to be 3.0.0 after
acceptance. The PR is deliberately draft until local browser/application validation
and remaining release decisions are complete.

The user authorized implementation, documentation, tests, and a PR. They did not ask
to merge or publish packages. The release workflow uses the existing trusted publisher for both packages.

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
   migration with different code. The original hash is required to resume. Review the proposed emergency repair contract before stable release.
4. Fresh bootstrap creates schema only, with no historical data/seed hook replay.
   History pruning/compaction needs an explicit future retention contract.
5. The CLI's project input is an opt-in executable exporter, not arbitrary Roslyn
   inspection of an app. A future language front end can emit the neutral schema.
6. Schema validation proves identities/layout/plans, not business meaning or every
   record invariant. Custom transforms and legacy baselines need realistic fixtures.
7. Custom modules must be self-contained and need CSP `blob:` permission. Pin/cache
   immutable assets deliberately for offline use. Magic does not manage service workers.

## Evidence and rollout

The [reviewed hardening revision](https://github.com/magiccodingman/Magic.IndexedDb/commit/422bf0b7fab92cf70d6334d061736a95976310f2)
passed all 12 then-existing CI jobs. Separately, local Chrome, Firefox and Linux
WebKit ran 42 installed-package adoption cases across both managed build modes.
Those runs covered adoption through revision 1.

The next validation gate runs the released-package fixture on Chromium, Firefox,
Linux WebKit and macOS WebKit. One published artifact set is reused across those
jobs. Each job requires all seven historical packages to adopt, advance through
the CLI-produced revision 2 and reopen in trimmed and AOT Blazor builds. Each
mode also runs the two legacy writer cutover cases. Reports identify the source
SHA, actual engine and host versions, and execution counts. Refer to the current
PR check runs for exact-head outcomes and downloadable artifacts. The same
workflow is a prerequisite for the release publisher.

The [testing guide](../contributing/migration-testing.md) gives local commands
for both standalone and CLI-configured protocol runs and for the released
packages. The quota case injects a quota failure inside a real IndexedDB
transaction. The service-worker test controls which exact assets are served
from Cache Storage. The bridge covers a loaded tab and worker while offline.
Real disk exhaustion and production PWA install, activation and cache rollout
remain unverified.

## Decisions before stable release

- Review the [application-owned cutover](../guides/legacy-writer-cutover.md).
  Previously shipped uncooperative clients can write after adoption. A final
  2.x bridge helps installations where every active writer receives and honors it.
- Review the [partial recovery proposal](../architecture/adr-partial-migration-recovery.md).
  A deterministic hook failure now calls for developer intervention. An ordinary
  later revision cannot recover the interrupted edge. Keep resumable support in
  preview and hold stable release pending review of the explicit recovery contract.
- Keep the preview PR draft. The maintainer owns final release and separate
  verification of the trusted publisher scopes.

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
