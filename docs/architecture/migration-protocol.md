# Migration protocol v1

This document describes the implemented protocol in Magic IndexedDB 3. The browser
implementation is `Magic.IndexedDb/wwwroot/migrations/`; the CLI and C# exporter
produce its JSON contracts. See [the implementation contract](v3-implementation-contract.md)
for representative document shapes and [the user guide](../guides/migrations.md)
for operations and recovery.

## Architectural boundary

The C# resolver turns registered repositories and table definitions into a portable
persisted model. The CLI consumes that model, compares it to committed snapshots,
and publishes immutable static assets. The browser engine consumes those assets
and applies the same rules whether the caller is C#, JavaScript, or another future
translation layer. The engine never inspects CLR type names or loads assemblies.

Runtime, protocol, model revision, native IndexedDB version, and custom storage codec
version are independent. Changing an SDK or NuGet package must not accidentally
advance an application's data history.

## Documents and bytes

All documents are UTF-8 JSON. Schema fingerprints are SHA-256 of canonical JSON:
object keys sorted by UTF-16 ordinal order, array order preserved, no whitespace,
JSON.stringify-compatible string escaping and finite IEEE-754 number rendering.
Undefined, nonfinite values, and duplicate object members are not valid portable
source documents. C# and CLI use a shared canonical writer; a checked-in test corpus
is also executed by JavaScript. Non-JSON IndexedDB keys in private checkpoints use
native IndexedDB key comparison and are not passed through the document canonicalizer.

Asset hashes are SHA-256 of exact fetched bytes, including whitespace and line
endings. Digests are lowercase hexadecimal strings prefixed with `sha256:`. A
pretty-printed schema can have a different asset digest from its canonical model
fingerprint; these are deliberately separate values.

A schema document contains `protocolVersion`, `schemaId`, `numericFormat`, `stores`,
and `requiredCapabilities`. Each store describes its name, inline/compound/out-of-line
key path, auto-increment behavior, indexes, and persisted fields. Field descriptors
record data kind, nullability, codec, and relevant nested/collection/enum metadata.
These fields support drift detection and author review. They do not replace
application-specific record validation or infer the intended meaning of a change.

Schema IDs use a portable ASCII identifier. Store names beginning `__magic_` and
unsafe member paths are reserved. C# models supply stable names independently of
CLR names through the existing mapping rules. The engine's physical-layout
comparison uses only native key/index properties; descriptive future metadata does
not accidentally create an IndexedDB index.

## Manifest and history

A manifest pins one schema ID and target revision. It lists schema assets,
contiguous one-revision transitions, and optional supported legacy baselines. Each
asset reference contains a path and exact digest. Each migration reference contains
its ID and `fromRevision`/`toRevision`. Documents repeat those identities and carry
canonical `fromSchemaHash`/`toSchemaHash`, execution mode, operations, and capabilities.
A mismatch is an error before writes.

The CLI retains source in `MagicMigrations/<schemaId>/` and seals released bytes and
reference bindings in `published.lock.json`. Publish verifies the complete retained
source graph. The runtime fetches the manifest and only the schema/migration/module
bodies needed by that client's path. A fresh client fetches the current target
schema and creates it directly; old transformation bodies are not required.

Every existing client must have one unambiguous path from its committed revision.
Forks, duplicate IDs, missing transitions, changed applied migration hashes, and
unknown required operations fail. The preview retains history references and does
not implement implicit squashing or retention cutoffs. A future checkpoint/retention
capability must define how old clients recognize unsupported starting states.

The application pins an immutable manifest URL at its own HTTP(S) origin. Asset URLs
must share the manifest origin. Redirected bytes are still hash-checked. A manifest
is application deployment input, not a detached cryptographic signature; origin
security and trusted deployment remain the application's responsibility.

## Capability negotiation

The v1 engine advertises `atomic`, `resumable`, `legacyAdoption`, and
`customTransforms`. Unknown `protocolVersion` values, required capabilities,
operations, or numeric codecs fail closed. Optional metadata may be additive when
it does not change execution meaning. A feature that changes execution semantics
must require a recognized capability or a new protocol version; adding an ignored
field is not a valid way to weaken preconditions.

New adapters can consume the same model/history without teaching the engine a
language's type system. Future encryption, staging, rollback, checkpoint retention,
or codec conversion must have their own complete validation and recovery semantics
before they are advertised. No current API promises those unimplemented behaviors.

## Installed journal

The reserved `__magic_migrations__` object store holds state under key `state`:

```json
{
  "protocolVersion": 1,
  "schemaId": "clients",
  "revision": 2,
  "schemaHash": "sha256:<canonical-model-digest>",
  "status": "ready",
  "history": [{ "id": "0002_AddIndex", "sha256": "sha256:<migration-bytes>" }],
  "active": null
}
```

A resumable migration changes `status` to `migrating` while retaining the last fully
committed revision and model hash. Its `active` value contains migration identity,
asset digest, operation index, last processed native primary key, and processed
record count. The key is private engine state; diagnostic inspection replaces it
with a boolean `hasCheckpoint`. A ready journal cannot contain an active checkpoint.

Bootstrap can create the target revision with an empty applied history because no
old records were transformed. Adoption writes a journal at the verified baseline
revision without rewriting legacy records. Each completed transition appends its
identity/hash and advances revision/model hash in the committing transaction.

Native object-store shape is checked against the declared starting schema. Managed
lossless storage also retains the existing numeric policy store and validates that
policy on reads/writes through the public adapter. A numeric codec conversion cannot
be smuggled through an ordinary record transform.

## Ownership and readiness

An exclusive Web Lock named by physical database serializes migration attempts in
cooperating tabs and workers. Web Locks in a secure context is a v3 requirement; the
engine does not fall back to a weaker local boolean. Native upgrade transactions
serialize schema changes with all IndexedDB connections, including older clients.

The app's model and manifest inputs are copied before the first asynchronous wait.
All required assets and hook bytes are fetched and validated before the first
mutation. Custom modules execute from verified bytes, avoiding a second unverified
HTTP import. Importing a custom module executes application code, so top-level code
must not mutate databases or produce side effects outside the engine.

Connections close on `versionchange`. The Blazor/JavaScript adapter marks previously
ready handles incompatible and checks journal identity/revision/native version
before exposing a table for ordinary work. An older application cannot reinterpret
a newer database as its own schema. During resumable work, ordinary managed access
fails until the journal returns to ready. The protocol cannot prevent unrelated
same-origin code from directly using the native IndexedDB API; do not run an
independent writer against a migrating database.

## Transaction semantics

An atomic step executes structural operations and synchronous cursor transforms in
one native upgrade transaction. A thrown exception, rejected unique constraint, or
asynchronous custom result aborts that revision. Journal updates are in that same
transaction. Async boundaries such as fetch or key derivation must stay outside it.

A resumable step first commits a new native version and an in-progress journal. It
reads one ordered primary-key batch, performs supported transformations outside the
write transaction, then validates the still-current checkpoint and commits records
and new progress together. Primary keys must remain stable. A final transaction
checks the target layout and records readiness. Different revisions commit
independently; failing a later revision does not undo a previous successful revision.

This preview supports data-only resumable operations. Key changes, structural
staging and atomic swaps, cross-database moves, authenticated transforms, and
changing lossless numeric representation are rejected. No operation silently copies
ciphertext through the ordinary record pipeline.

Cancellation does not undo committed revisions/batches. A blocked native open has
handlers retained so a late upgrade aborts. An interrupted resumable hook may be
called again on retry if its batch never committed. Hook code must be deterministic
for the same source/configuration and must not depend on exactly-once external side
effects. Cancellation cannot terminate arbitrary synchronous JavaScript.

## Error and observation contract

Errors carry a stable code, retry indication, structured metadata, documentation
URL, and an optional local cause. Events report progress/failure without record
payloads, primary keys, secrets, or arbitrary hook error text. Observer failures are
isolated. The application owns UI, telemetry, reload timing, export, and any explicit
destructive recovery policy. The engine never clears storage after a failed migration.

Readiness, checkpoint, rollback, legacy adoption, stale clients, missing assets,
package drift, protection boundaries, and cross-language fingerprints are exercised
by the [validation suite](../contributing/migration-testing.md). Its real-browser
execution evidence is required separately from static and schema validation.
