import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const root = 'MagicMigrations/recovery-wasm';
const ordinal = Number(process.argv[2]);
const history = JSON.parse(await readFile(path.join(root, 'history.json'), 'utf8'));
const edge = history.migrations.find(item => item.toRevision === ordinal);
const file = path.join(root, edge.path);
const migration = JSON.parse(await readFile(file, 'utf8'));
assert.deepEqual(migration.operations.map(operation => operation.op), ['manual']);
if (ordinal === 2) {
    const body = 'export function transform(row) { if (row.Id === 2) throw new Error("Synthetic fault"); return { ...row, Value: 11 }; }';
    await writeFile(path.join(path.dirname(file), 'original.mjs'), body);
    migration.operations = [{ op: 'transform', store: 'records', module: 'original.mjs',
        sha256: 'sha256:' + createHash('sha256').update(body).digest('hex') }];
} else {
    assert.equal(ordinal, 3);
    const body = 'export function transform(row) { return { ...row, Normalized: true }; }';
    await writeFile(path.join(path.dirname(file), 'normalize.mjs'), body);
    migration.operations = [{ op: 'transform', store: 'records', module: 'normalize.mjs',
        sha256: 'sha256:' + createHash('sha256').update(body).digest('hex') }];
}
await writeFile(file, JSON.stringify(migration));
