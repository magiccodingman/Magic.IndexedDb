// Exercise the installed tool's portable authoring boundary. These exact assets
// are served by every browser job; this script does not emulate runtime recovery.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { canonicalJson, sha256, validateManifest, validateMigration } from '../../Magic.IndexedDb/wwwroot/migrations/protocol.js';

const [magicdb, source, output, reportFile] = process.argv.slice(2);
const schemaId = 'protected-transition';
await mkdir(source, { recursive: true });
const modelPath = path.join(source, 'model.json');
const schema = {
    protocolVersion: 1, schemaId, numericFormat: 'legacy', requiredCapabilities: ['authenticatedMigrationsV1'],
    stores: [{ name: 'clients', keyPath: 'id', autoIncrement: false, indexes: [], fields: [
        { name: 'id', type: 'integer32', nullable: false, codec: 'json' },
        { name: 'name', type: 'string', nullable: false, codec: 'json' },
        { name: 'notes', type: 'string', nullable: false, codec: 'json' }
    ] }]
};
const store = schema.stores[0];
const historyPath = path.join(source, schemaId, 'history.json');

function run(args, expected = 0) {
    const result = spawnSync(magicdb, [...args, '--directory', source, '--json'], { encoding: 'utf8' });
    if (result.error) throw result.error;
    assert.equal(result.status, expected, [args.join(' '), result.stdout, result.stderr].join('\n'));
    return result;
}

async function add(name, approveLoss = false) {
    await writeFile(modelPath, JSON.stringify(schema));
    run(['migrations', 'add', name, '--schema', modelPath], approveLoss ? 3 : 0);
    if (approveLoss) {
        const denied = run(['migrations', 'validate', '--schema', modelPath], 3);
        assert.equal(JSON.parse(denied.stderr).code, 'MAGDB4212');
        const history = JSON.parse(await readFile(historyPath, 'utf8'));
        const file = path.join(source, schemaId, history.migrations.at(-1).path);
        const migration = JSON.parse(await readFile(file, 'utf8'));
        assert.equal(migration.operations.length, 1);
        assert.equal(migration.operations[0].op, 'reprotect');
        assert.equal(migration.operations[0].allowProtectionLoss, false);
        migration.operations[0].allowProtectionLoss = true;
        await writeFile(file, JSON.stringify(migration));
    }
    run(['migrations', 'validate', '--schema', modelPath]);
}

await add('PublicVault');
store.encryptedFields = ['notes'];
await add('ProtectNotes');
// An existing protected database can adopt precisely revision 2. The runtime
// still has to authenticate its saved policy and actual record representation.
run(['migrations', 'baseline', '--database', schemaId, '--legacy-schema', modelPath,
    '--revision', '2', '--id', 'protected-legacy', '--acknowledge-data-contract']);
delete store.encryptedFields;
store.exposedFields = ['id', 'name'];
await add('ProtectWholeRecord');
delete store.exposedFields;
store.encryptedFields = ['notes'];
await add('ReturnToFields', true);
delete store.encryptedFields;
await add('PublicAgain', true);
store.indexes.push({ name: 'byName', keyPath: 'name', unique: false, multiEntry: false });
await add('PublicIndex');
run(['migrations', 'publish', '--schema', modelPath, '--output', output]);

const published = path.join(output, schemaId);
const manifest = JSON.parse(await readFile(path.join(published, 'manifest.json'), 'utf8'));
validateManifest(manifest, schemaId);
assert.equal(manifest.targetRevision, 6);
assert.deepEqual(manifest.requiredCapabilities, ['authenticatedMigrationsV1']);
assert.equal(manifest.baselines.length, 1);
const schemas = new Map();
for (const entry of manifest.schemas) {
    const bytes = await readFile(path.join(published, entry.path));
    assert.equal(await sha256(bytes), entry.sha256);
    schemas.set(entry.revision, JSON.parse(bytes));
}
for (const entry of manifest.migrations) {
    const bytes = await readFile(path.join(published, entry.path));
    assert.equal(await sha256(bytes), entry.sha256);
    const document = JSON.parse(bytes);
    assert.ok(document.requiredCapabilities.includes('authenticatedMigrationsV1'));
    assert.equal(document.fromSchemaHash, await sha256(canonicalJson(schemas.get(entry.fromRevision))));
    assert.equal(document.toSchemaHash, await sha256(canonicalJson(schemas.get(entry.toRevision))));
    validateMigration(document, entry, schemas.get(entry.fromRevision), schemas.get(entry.toRevision), { authenticated: true });
    assert.throws(() => validateMigration(document, entry, schemas.get(entry.fromRevision), schemas.get(entry.toRevision)),
        error => error.code === 'MAGDB_PROTECTED_ADAPTER_REQUIRED');
    assert.equal(document.mode, entry.toRevision === 6 ? 'atomic' : 'resumable');
}
await writeFile(reportFile, JSON.stringify({
    installedPackage: true, schemaId, revisions: 6, transitions: 5, approvedProtectionLosses: 2,
    protectedBaselines: 1, portableValidationPassed: true, browserManifest: 'protected-transition/manifest.json'
}, null, 2) + '\n');
