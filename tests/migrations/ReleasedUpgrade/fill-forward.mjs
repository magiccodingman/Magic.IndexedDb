import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const directory = path.join('MagicMigrations', process.argv[2]);
const history = JSON.parse(await readFile(path.join(directory, 'history.json'), 'utf8'));
const migrationPath = path.join(directory, history.migrations.find(edge => edge.toRevision === 2).path);
const migration = JSON.parse(await readFile(migrationPath, 'utf8'));
const module = 'count-migration.mjs';
const body = 'export function transform(row) { return { ...row, MigrationCount: (row.MigrationCount ?? 0) + 1 }; }\n';
const unresolved = migration.operations.filter(operation => operation.op === 'manual');
assert.equal(unresolved.length, 1);
assert.equal(unresolved[0].field, 'MigrationCount');
assert.equal(unresolved[0].store, 'invoices');
assert.ok(migration.operations.some(operation => operation.op === 'createIndex' &&
    operation.store === 'invoices' && operation.index.keyPath === 'MigrationCount'));
migration.operations[migration.operations.indexOf(unresolved[0])] = {
    op: 'transform', store: 'invoices', module, exportName: 'transform',
    sha256: 'sha256:' + createHash('sha256').update(body).digest('hex')
};
await writeFile(path.join(path.dirname(migrationPath), module), body);
await writeFile(migrationPath, JSON.stringify(migration));
