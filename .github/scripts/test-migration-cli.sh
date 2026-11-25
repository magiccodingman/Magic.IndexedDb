#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
artifact_root="$repo_root/artifacts/migration-cli"
mkdir -p "$artifact_root"
scratch_root="$(mktemp -d)"
trap 'rm -rf "$scratch_root"' EXIT

# These directories contain generated fixture output only. Keep package/test logs
# when a command fails so CI uploads the evidence rather than reporting a false pass.
rm -rf "$artifact_root/source" "$artifact_root/published" "$artifact_root/repeated" "$artifact_root/packages" "$artifact_root/consumer"
mkdir -p "$artifact_root/packages" "$artifact_root/models"

dotnet pack Magic.IndexedDb.Cli/Magic.IndexedDb.Cli.csproj --configuration Release \
  --output "$artifact_root/packages" -p:GeneratePackageOnBuild=false > "$artifact_root/pack.log" 2>&1
package_version="$(dotnet msbuild Magic.IndexedDb.Cli/Magic.IndexedDb.Cli.csproj -nologo -verbosity:quiet -getProperty:Version)"
cat > "$scratch_root/NuGet.Config" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /></packageSources></configuration>
XML
dotnet tool install Magic.IndexedDb.Cli --version "$package_version" \
  --tool-path "$scratch_root/tool" --configfile "$scratch_root/NuGet.Config" \
  --add-source "$artifact_root/packages" > "$artifact_root/install.log" 2>&1
magicdb="$scratch_root/tool/magicdb"
"$magicdb" --help > "$artifact_root/help.txt"
"$magicdb" --version --json > "$artifact_root/version.json"

node --input-type=module - "$artifact_root" "$package_version" <<'JS'
import fs from 'node:fs';
import path from 'node:path';
const [root, version] = process.argv.slice(2);
const actual = JSON.parse(fs.readFileSync(path.join(root, 'version.json')));
if (actual.version !== version || actual.protocolVersion !== 1) throw new Error('Installed package version/protocol mismatch');
const model = {
  protocolVersion: 1, schemaId: 'cli-fixture', numericFormat: 'legacy', requiredCapabilities: [],
  stores: [{ name: 'items', keyPath: 'id', autoIncrement: false, indexes: [],
    fields: [{ name: 'id', type: 'integer32', nullable: false, codec: 'json' },
      { name: 'name', type: 'string', nullable: false, codec: 'json' }] }]
};
fs.writeFileSync(path.join(root, 'models/revision1.json'), JSON.stringify(model));
model.stores[0].indexes.push({ name: 'byName', keyPath: 'name', unique: false, multiEntry: false });
fs.writeFileSync(path.join(root, 'models/revision2.json'), JSON.stringify(model));
JS

"$magicdb" migrations add Initial --schema "$artifact_root/models/revision1.json" \
  --directory "$artifact_root/source" --non-interactive --json > "$artifact_root/add-initial.json"
"$magicdb" migrations add NoChange --schema "$artifact_root/models/revision1.json" \
  --directory "$artifact_root/source" --json > "$artifact_root/add-unchanged.json"

set +e
"$magicdb" migrations validate --schema "$artifact_root/models/revision2.json" \
  --directory "$artifact_root/source" --json > "$artifact_root/drift-stdout.json" 2> "$artifact_root/drift.json"
drift_exit=$?
set -e
if [[ "$drift_exit" -ne 3 ]]; then
  echo "Expected persisted model drift to fail with exit 3; received $drift_exit" >&2
  exit 1
fi

"$magicdb" migrations add AddNameIndex --schema "$artifact_root/models/revision2.json" \
  --directory "$artifact_root/source" --json > "$artifact_root/add-index.json"
"$magicdb" migrations validate --schema "$artifact_root/models/revision2.json" \
  --directory "$artifact_root/source" --json > "$artifact_root/validate.json"
"$magicdb" migrations publish --schema "$artifact_root/models/revision2.json" \
  --directory "$artifact_root/source" --output "$artifact_root/published" --json > "$artifact_root/publish.json"
"$magicdb" migrations publish --schema "$artifact_root/models/revision2.json" \
  --directory "$artifact_root/source" --output "$artifact_root/repeated" --json > "$artifact_root/publish-repeat.json"

node tests/migrations/author-codec-recovery.mjs "$magicdb" "$scratch_root/recovery-codecs" \
  "$artifact_root/published/cli-fixture/recovery-codecs" "$artifact_root/recovery-codec-contracts.json"
node tests/migrations/author-protected-transition.mjs "$magicdb" "$scratch_root/protected-transition" \
  "$artifact_root/published/cli-fixture" "$artifact_root/protected-transition-contract.json"

# Real installed-package -> MSBuild-selected project SDK -> isolated exporter ->
# shared runtime schema resolver. No application assembly is loaded by the tool.
"$magicdb" databases list --project tests/MigrationCliConsumer/MigrationCliConsumer.csproj \
  --framework net10.0 --configuration Release --json > "$artifact_root/consumer-list.json"
"$magicdb" schema export --project tests/MigrationCliConsumer/MigrationCliConsumer.csproj \
  --framework net10.0 --configuration Release --output "$artifact_root/consumer-model.json" --json > "$artifact_root/consumer-export.json"
"$magicdb" migrations add Initial --schema "$artifact_root/consumer-model.json" \
  --directory "$artifact_root/consumer" --json > "$artifact_root/consumer-add.json"
"$magicdb" migrations validate --project tests/MigrationCliConsumer/MigrationCliConsumer.csproj \
  --directory "$artifact_root/consumer" --json > "$artifact_root/consumer-validate.json"
set +e
"$magicdb" schema export --project Magic.IndexedDb.Cli/Magic.IndexedDb.Cli.csproj \
  --json > "$artifact_root/non-provider-stdout.json" 2> "$artifact_root/non-provider.json"
provider_exit=$?
set -e
if [[ "$provider_exit" -ne 4 ]]; then
  echo "An arbitrary application project must be rejected with exit 4; received $provider_exit" >&2
  exit 1
fi

dotnet build tests/Magic.IndexedDb.Cli.ContractTests/Magic.IndexedDb.Cli.ContractTests.csproj \
  --configuration Release > "$artifact_root/contract-build.log" 2>&1
dotnet tests/Magic.IndexedDb.Cli.ContractTests/bin/Release/net10.0/Magic.IndexedDb.Cli.ContractTests.dll \
  > "$artifact_root/contract-results.json"

node --input-type=module - "$artifact_root" <<'JS'
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { canonicalJson } from './Magic.IndexedDb/wwwroot/migrations/protocol.js';
const root = process.argv[2];
const read = relative => JSON.parse(fs.readFileSync(path.join(root, relative)));
assert.equal(read('add-unchanged.json').results[0].revision, 1);
assert.equal(read('add-unchanged.json').results[0].status, 'unchanged');
assert.equal(read('drift.json').code, 'MAGDB4205');
assert.equal(read('consumer-list.json').databases[0].schemaId, 'consumer-fixture');
assert.equal(read('consumer-validate.json').results[0].modelCompared, true);
const report = read('contract-results.json');
assert.ok(report.testsExecuted >= 32, 'No contract tests discovered');
assert.equal(report.failed, 0);
const codecRecovery = read('recovery-codec-contracts.json');
assert.deepEqual([codecRecovery.expected, codecRecovery.executed, codecRecovery.passed, codecRecovery.failed, codecRecovery.skipped], [3, 3, 3, 0, 0]);
const protectedTransition = read('protected-transition-contract.json');
assert.equal(protectedTransition.portableValidationPassed, true);
assert.equal(protectedTransition.transitions, 5);
const directory = path.join(root, 'published/cli-fixture');
const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'manifest.json')));
assert.equal(manifest.targetRevision, 2);
assert.equal(manifest.migrations.length, 1);
const hash = bytes => 'sha256:' + createHash('sha256').update(bytes).digest('hex');
for (const entry of [...manifest.schemas, ...manifest.migrations]) {
  const bytes = fs.readFileSync(path.join(directory, entry.path));
  assert.equal(hash(bytes), entry.sha256, `Exact-byte hash for ${entry.path}`);
  assert.equal(bytes.toString(), canonicalJson(JSON.parse(bytes)), `JavaScript canonical parity for ${entry.path}`);
  assert.deepEqual(bytes, fs.readFileSync(path.join(root, 'repeated/cli-fixture', entry.path)));
}
assert.deepEqual(fs.readFileSync(path.join(directory, 'manifest.json')), fs.readFileSync(path.join(root, 'repeated/cli-fixture/manifest.json')));
const result = { installedPackage: true, testsExecuted: report.testsExecuted, failed: 0,
  codecRecoveryTestsExecuted: codecRecovery.executed,
  protectedTransitionsValidated: protectedTransition.transitions,
  browserFixture: 'artifacts/migration-cli/published/cli-fixture/manifest.json', targetRevision: 2 };
fs.writeFileSync(path.join(root, 'summary.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify(result, null, 2));
JS
