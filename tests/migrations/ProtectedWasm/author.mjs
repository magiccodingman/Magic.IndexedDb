import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const [cli, targetFile] = process.argv.slice(2);
const exported = JSON.parse(await readFile(targetFile, 'utf8'));
const target = exported.schemas?.[0] ?? exported;
assert.equal(target.schemaId, 'protected-wasm');
assert.deepEqual(target.requiredCapabilities, ['authenticatedMigrationsV1']);
const fieldModel = structuredClone(target);
fieldModel.stores[0].encryptedFields = ['Secret'];
const recordModel = structuredClone(target);
recordModel.stores[0].exposedFields = ['Id'];
await writeFile('field-model.json', JSON.stringify(fieldModel));
await writeFile('record-model.json', JSON.stringify(recordModel));
await writeFile('public-model.json', JSON.stringify(target));

function run(args, expected = 0) {
    const result = spawnSync(cli, [...args, '--json'], { encoding: 'utf8' });
    if (result.error) throw result.error;
    assert.ok((Array.isArray(expected) ? expected : [expected]).includes(result.status),
        [args.join(' '), result.stdout, result.stderr].join('\n'));
}

async function transform(revision, body, allowProtectionLoss = false) {
    const root = 'MagicMigrations/protected-wasm';
    const history = JSON.parse(await readFile(path.join(root, 'history.json'), 'utf8'));
    const edge = history.migrations.find(item => item.toRevision === revision);
    const file = path.join(root, edge.path);
    const document = JSON.parse(await readFile(file, 'utf8'));
    const module = `transform-${revision}.mjs`;
    await writeFile(path.join(path.dirname(file), module), body);
    document.operations = [{ op: 'transform', store: 'records', module,
        sha256: 'sha256:' + createHash('sha256').update(body).digest('hex'),
        ...(allowProtectionLoss ? { allowProtectionLoss: true } : {}) }];
    await writeFile(file, JSON.stringify(document));
}

run(['migrations', 'add', 'AdoptFieldVault', '--schema', 'field-model.json']);
run(['migrations', 'baseline', '--database', 'protected-wasm', '--legacy-schema', 'field-model.json',
    '--revision', '1', '--id', 'legacy-vault', '--acknowledge-data-contract']);
run(['migrations', 'add', 'WrapWholeRecord', '--schema', 'record-model.json', '--mode', 'resumable'], [0, 3]);
await transform(2, `export async function transform(row) {
    if (row.Id === 2 && globalThis.pauseProtectedFixture) {
        globalThis.protectedFixtureHookPending = true;
        await new Promise(() => {});
    }
    return { ...row, Visits: row.Visits + 1 };
}`);
run(['migrations', 'validate', '--schema', 'record-model.json']);
run(['migrations', 'add', 'ApprovedPublicRecords', '--schema', 'public-model.json', '--mode', 'resumable'], 3);
await transform(3, `export async function transform(row) {
    await globalThis.assertWholeProtection();
    return row;
}`, true);
run(['migrations', 'validate', '--schema', 'public-model.json']);
run(['migrations', 'publish', '--schema', 'public-model.json', '--output', 'App/wwwroot/releases/final']);
