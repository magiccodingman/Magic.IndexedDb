# Migration CLI

`Magic.IndexedDb.Cli` is the independently packaged .NET tool for Magic IndexedDB
3.0. Its command is `magicdb`. The CLI discovers a persisted model, keeps reviewed
migration source in Git, validates the complete retained history, and publishes a
static migration package. The browser engine consumes that package without loading
C# or contacting a migration API.

The package version starts at `3.0.0-preview.1`. The portable document protocol is
version `1`; application schema revisions are separate positive integers. Updating
a NuGet package does not increment a database revision.

## Installation and the first schema

The tool requires the .NET 10 runtime. Building a C# exporter also requires its
selected SDK and target framework. After the package is published to your feed,
pin the version in a repository tool manifest:

```sh
dotnet new tool-manifest
dotnet tool install Magic.IndexedDb.Cli --version 3.0.0-preview.1
dotnet tool run magicdb -- --help
```

For a local checkout, pack and install the actual tool without publishing it:

```sh
dotnet pack Magic.IndexedDb.Cli/Magic.IndexedDb.Cli.csproj -c Release -o artifacts/packages
dotnet tool install Magic.IndexedDb.Cli --version 3.0.0-preview.1 --add-source artifacts/packages
```

The examples below use `magicdb` as shorthand. With a local tool manifest, use
`dotnet tool run magicdb --` in its place. A global installation also exposes the
`magicdb` executable directly.

```sh
magicdb databases list --project tools/App.Schema/App.Schema.csproj
magicdb migrations add Initial --project tools/App.Schema/App.Schema.csproj --database clients
magicdb migrations validate --project tools/App.Schema/App.Schema.csproj --database clients
magicdb migrations publish --project tools/App.Schema/App.Schema.csproj --database clients --output wwwroot/db/releases/2026-11-25
```

The initial command creates revision 1 as a complete schema snapshot. A new browser
database can bootstrap at the current target snapshot. There is no pretend
revision-zero migration and no need to replay historical transformations against
an empty database.

Commands never prompt. `--non-interactive` is accepted for explicit CI intent.
`--json` produces machine-readable results on stdout and diagnostics on stderr.

## An explicit design-time exporter

The CLI uses the same `MagicSchemaExporter` and schema resolver as the runtime.
Table membership, persisted JSON names, keys, indexes, numeric encodings, nested
field descriptors, serializer conditions, and explicit codec identities therefore
have one C# definition.

Create a small console companion that references your shared model project and the
runtime package. It must be executable and opt in with this property:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <MagicIndexedDbDesignTime>true</MagicIndexedDbDesignTime>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/App.Models/App.Models.csproj" />
    <PackageReference Include="Magic.IndexedDb" Version="3.0.0-preview.1" />
  </ItemGroup>
</Project>
```

Its `Program.cs` names the table and repository types belonging to this application:

```csharp
using Magic.IndexedDb.Migrations;
using App.Models;

return MagicSchemaExporter.Run(
    args,
    tableTypes: [typeof(Client), typeof(Order)],
    repositoryTypes: [typeof(AppDatabases)]);
```

`Run` recognizes `--magic-schema-export`, writes the neutral JSON envelope to
stdout, and writes failures to stderr with a nonzero exit. Use
`MagicSchemaOptions` to provide the same numeric modes, protection declarations,
and serializer options that the application uses. See the runnable
[consumer fixture](../../tests/MigrationCliConsumer/Program.cs).

The tool evaluates and builds this project through `dotnet` in the project's
directory, honoring its `global.json`, project references, configuration, and
selected framework. It then launches the compiled exporter in a separate process.
It does not reflectively load an application's assemblies into the tool process,
start Blazor, or evaluate arbitrary startup services. Model configuration used by
the exporter must be available without a browser session. The exporter is code you
own and run, so normal project build and executable behavior still applies.

Use `--framework` when the exporter multi-targets, and `--configuration` when
conditional compilation changes the model:

```sh
magicdb schema export --project tools/App.Schema/App.Schema.csproj --framework net10.0 --configuration Release --output artifacts/current-schema.json
```

A directory is accepted only when it contains exactly one `.csproj`. Solutions and
ordinary application projects are rejected with actionable diagnostics. A
separate exporter per application makes project-specific database membership
explicit; shared model assemblies can still be reused.

## Multiple databases and language independence

An exporter returns an envelope such as:

```json
{"protocolVersion":1,"schemas":[{"protocolVersion":1,"schemaId":"clients","numericFormat":"legacy","requiredCapabilities":[],"stores":[]}]}
```

`databases list` lists every exported logical schema. A command that changes or
validates history requires `--database ID` or `--all` when more than one schema is
available. Each schema has its own source directory, revision counter, manifest,
and release history. `--all` processes independent databases; it does not create a
cross-database transaction.

`schemaId` identifies a logical history. The runtime's physical database name can
identify an individual account, tenant, or instance using that history. Keep
deployment environment names and account identifiers out of an otherwise identical
schema's history. Separate histories are appropriate when models actually differ.

Every model-consuming command also accepts `--schema FILE` instead of `--project`.
That file can contain one schema document or the envelope above. A TypeScript
adapter, another language, or a manual workflow can emit the same contract. The CLI
planner and browser migrations do not require a C# assembly in that mode.

The schema ID and migration/baseline identifiers use 1–128 ASCII letters, digits,
dots, underscores, and hyphens, starting with a letter or digit. Asset references
must remain within that schema's directory. Path traversal, symbolic links in
asset paths, duplicate JSON properties, duplicate identities, unsupported protocol
versions, and unknown required capabilities fail validation.

## Source history and automatic revisions

The default source root is `MagicMigrations`; change it with `--directory PATH`.
A history can contain these files:

```text
MagicMigrations/clients/history.json
MagicMigrations/clients/schemas/0001.json
MagicMigrations/clients/schemas/0002.json
MagicMigrations/clients/migrations/0002_AddStatus.json
MagicMigrations/clients/custom/add-status.js
MagicMigrations/clients/baselines/legacy-v2.json
MagicMigrations/clients/published.lock.json
```

Commit the source documents, custom modules, and `published.lock.json`. Ignore the
temporary coordination file `.magicdb.write.lock`; it contains no migration
history and is ignored by this repository.

After changing your application model:

```sh
magicdb migrations add AddStatus --project tools/App.Schema/App.Schema.csproj --database clients
```

The tool compares the current persisted model with the latest source snapshot,
assigns the next integer revision, and writes one adjacent transition. It validates
the existing history first. An unchanged model returns `status: "unchanged"` and
leaves the revision alone. The tool never manufactures different migrations for
development, staging, and production when their persisted models are identical.

The planner handles these decisions:

| Change | Generated result |
| --- | --- |
| New store | `createStore` with its full definition |
| Added, removed, or changed index | Explicit index operations |
| Newly nullable field or relaxed nullability | New schema revision without an invented backfill |
| New required field | `manual` decision requesting an explicit default or transform |
| Changed codec, type, nested shape, or serializer condition | `manual` decision |
| Removed field/store or ambiguous rename | `manual` decision requiring a reviewed operation |
| Changed primary key or protection policy | `manual` decision; unsupported transitions remain blocked |

A draft containing `manual` returns exit `3` **after writing the draft**. This is
an intentional review stop, not a lost migration. Validation and publishing reject
unresolved drafts. Review and replace the decision in the migration JSON, then run
validation and browser tests. Destructive `deleteStore` and `deleteField`
operations require `allowDataLoss: true` in that reviewed document.

An explicit data-only change uses:

```sh
magicdb migrations add NormalizeNames --schema artifacts/current-schema.json --database clients --empty --mode resumable
```

This also begins with a `manual` operation. Replace it with the reviewed data
operations. `atomic` is the default. Protocol 1 resumable transitions support data
operations with stable primary keys; put structural changes in separate adjacent
atomic migrations. Numeric format transitions are not supported by this protocol
implementation, even if a draft's `manual` marker is removed.

## Custom JavaScript transformations

Built-in data operations are `renameField`, `setMissing`, and `deleteField`; they
operate on safe top-level fields. More involved historical record handling uses a
custom JavaScript module that the tool preserves byte for byte.

For example, create `MagicMigrations/clients/custom/normalize-name.js`:

```js
export function transform(record) {
  return { ...record, displayName: String(record.displayName ?? "").trim() };
}
```

Get the exact module digest:

```sh
magicdb hash MagicMigrations/clients/custom/normalize-name.js
```

Replace the draft operation with the following, substituting that digest:

```json
{
  "op": "transform",
  "store": "clients",
  "module": "../custom/normalize-name.js",
  "exportName": "transform",
  "sha256": "sha256:REPLACE_WITH_EXACT_64_CHARACTER_DIGEST"
}
```

`module` is relative to the migration JSON file. `exportName` defaults to
`transform`. Only `.js` and `.mjs` module files are accepted by the CLI. A changed
module without its matching digest fails validation. Once published, a changed
digest also fails the immutable history check; create a new migration and new
module path instead.

The function receives historical persisted records. It must return the replacement
record synchronously; the engine owns record writes and transaction/checkpoint
completion. Keep the primary key stable. Do not make network requests, use
asynchronous functions, or import additional modules from the hook: protocol 1
loads verified self-contained module bytes before entering IndexedDB transactions.
Static validation checks references and hashes; the JavaScript engine and browser
tests check executable behavior. The CLI does not claim to prove that arbitrary
business logic preserves every record's meaning.

## Adopting an existing database

Baseline planning declares which known legacy layout corresponds to a managed
revision. It does not connect to a browser, inspect the user's live records,
invent C# classes from samples, or adopt an unknown database automatically.

First create a managed snapshot that describes the **complete existing physical
layout**, with the old numeric storage format. Preserve extra stores if they
already exist. Export or write a neutral legacy schema describing that layout and
record the baseline:

```sh
magicdb migrations baseline --database clients --legacy-schema legacy-clients.json --revision 1 --id legacy-v2 --acknowledge-data-contract
```

The CLI requires an exact match of store names, primary keys, auto-increment flags,
indexes, and numeric format at the selected revision. The acknowledgement means
you reviewed historical field/converter semantics that physical IndexedDB metadata
cannot prove. The browser still validates the actual database before adoption.

If the desired new model differs, add subsequent migrations from this complete
legacy snapshot to the desired model. Do not remove extra stores from the baseline
to make validation pass. The [v3 migration guide](migrations.md) explains runtime
adoption, compatibility errors, events, and recovery behavior.

## Validation and deterministic static publishing

```sh
magicdb migrations validate --project tools/App.Schema/App.Schema.csproj --all --json
magicdb migrations publish --project tools/App.Schema/App.Schema.csproj --all --output wwwroot/db/releases/release-42 --json
```

Validation compares the current model with the target snapshot and verifies:

- Complete adjacent history from revision 1 through the target, with no duplicate
  IDs, gaps, forks, or conflicting asset references.
- Schema fingerprints on both ends of each migration and exact hook digests.
- Supported operations and explicit destructive decisions.
- A simulated physical result equal to the declared target store/index layout.
- Valid legacy mappings and all previously published reference/hash bindings.

For an intentionally model-free artifact job, use `--history-only` instead of
`--project` or `--schema`. Results explicitly set `modelCompared: false`. A normal
deployment should include the model comparison; the tool never silently omits it.

Publishing writes `OUTPUT/SCHEMA_ID/manifest.json`, canonical schema/migration JSON,
and unchanged hook bytes. Digests are SHA-256 of the published UTF-8 bytes. The
runtime and CLI link one canonical JSON implementation; the JavaScript contract
is verified against the same Unicode/numeric test corpus.

Publishing also updates the source `published.lock.json`. It records released
asset hashes and manifest reference bindings. Later validation rejects editing,
rebinding, or deleting those released assets. Commit this file along with the
release change. Like source history, the lock is reviewed source, not a signature
against an attacker who can rewrite your repository. Hashes provide artifact
consistency; normal HTTPS and trusted deployment provide origin authenticity.

Use a new output directory for each release. Existing different output bytes are
never replaced, including a changed manifest. An identical rebuild is accepted.
The manifest is written after its assets. Deploy the complete directory before
activating an application build that points to its manifest; keep supported old
release directories available for returning clients and cached older builds.

Source history and supported old migration assets are retained in this first
version. There is no history pruning or arbitrary squash command. Fresh databases
use the target snapshot; returning databases use their retained transition path.

## CI, browser tests, and release environments

Pin the tool version, restore it, build the exporter, validate against the selected
environment's model, run browser migration tests, and publish the same deterministic
source package. The CLI performs no NuGet pushes and creates no releases or tags.
Runtime/CLI package publication is a separate release workflow concern.

The repository exercises a real package with:

```sh
bash .github/scripts/test-migration-cli.sh
node --test tests/migrations/protocol.test.mjs
```

The first script packs the `.nupkg`, installs it into an isolated tool directory,
executes the installed command, builds a real consumer exporter, verifies model
drift and immutable deterministic output, and runs the CLI contract tests. It
leaves `artifacts/migration-cli/published/cli-fixture/manifest.json` for independent
browser tests. The migration workflow runs that fixture through Chromium, Firefox,
Linux WebKit, and macOS WebKit. See
[migration test coverage](../contributing/migration-testing.md) for invocation,
evidence, and recovery scenarios.

`migrations test` can integrate an application's own executable browser harness:

```sh
magicdb migrations test --schema artifacts/current-schema.json --database clients --runner ./tools/run-migration-tests
```

The tool validates and publishes temporary assets without sealing the source,
then runs the executable with `--manifest ABSOLUTE_PATH`. No shell is involved.
The runner writes logs to stderr and writes one JSON object to stdout:

```json
{"testsExecuted":12,"failed":0}
```

It must return exit 0, a positive `testsExecuted`, and zero failures. A missing
runner, no discovered tests, malformed output, or failed tests cannot produce a
success result. The runner is responsible for real browser execution and the
application's historical data fixtures; this command does not bundle a browser.

## Diagnostics

| Exit | Meaning |
| --- | --- |
| `0` | Command completed successfully |
| `2` | Usage error, missing input, or ambiguous database selection |
| `3` | Validation error or a generated draft requiring review |
| `4` | Exporter, build, or test-runner failure |
| `5` | Filesystem/process-start access or IO failure |
| `130` | Cancelled |

Each failure includes a stable `MAGDB` code, message, exit code, and documentation
link. Common diagnostic groups are:

| Codes | Next action |
| --- | --- |
| `MAGDB4001`–`MAGDB4003` | Correct command arguments and select the intended schema |
| `MAGDB4101`–`MAGDB4104` | Correct JSON/protocol, duplicate metadata, or asset paths |
| `MAGDB4201`–`MAGDB4207` | Create/review history, resolve drift/manual operations, or repair an invalid plan |
| `MAGDB4301` | Restore published immutable content; create a new migration for changes |
| `MAGDB4302` | Use a new output directory or inspect pre-existing unreferenced files |
| `MAGDB4303` | Wait for the other writer on this source history to finish |
| `MAGDB4401`–`MAGDB4405` | Correct the exporter project, SDK/framework selection, build, or JSON output |
| `MAGDB4501` | Correct/execute the browser test runner and inspect its stderr |
| `MAGDB4901` | Correct filesystem permissions, paths, or executable availability |

A cancellation or crash does not erase user-owned migration files. Inspect any
unreferenced new revision files before retrying; the tool refuses to overwrite
them. For interrupted browser migrations, use runtime inspection, diagnostic
events, and supported retry/resume paths described in the migration guide. Never
rewrite published history or delete a user's database to hide an error.
