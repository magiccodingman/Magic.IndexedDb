// These tests run without a browser. They cover the portable document contract,
// not IndexedDB transactions; run run.mjs for storage/recovery evidence.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import {
    canonicalJson, sha256, validateSchema, validateManifest, validateMigration,
    MigrationError, physicalStores, samePhysical
} from '../../Magic.IndexedDb/wwwroot/migrations/protocol.js';

const copy = value => structuredClone(value);
const checksum = 'sha256:' + 'a'.repeat(64);
const index = { name: 'byName', keyPath: 'name', unique: false, multiEntry: false };
const model = () => ({
    protocolVersion: 1, schemaId: 'clients', numericFormat: 'legacy', requiredCapabilities: [],
    stores: [{ name: 'clients', keyPath: 'id', autoIncrement: false, indexes: [], fields: [
        { name: 'id', type: 'integer32', nullable: false, codec: 'json' },
        { name: 'name', type: 'string', nullable: false, codec: 'json' }
    ] }]
});
const manifest = () => ({
    protocolVersion: 1, schemaId: 'clients', targetRevision: 2, requiredCapabilities: [],
    schemas: [1, 2].map(revision => ({ revision, path: `schemas/${revision}.json`, sha256: checksum })),
    migrations: [{ id: 'add-index', fromRevision: 1, toRevision: 2, path: 'migrations/2.json', sha256: checksum }],
    baselines: []
});
const migration = operations => ({
    protocolVersion: 1, schemaId: 'clients', id: 'add-index', fromRevision: 1, toRevision: 2,
    mode: 'atomic', fromSchemaHash: checksum, toSchemaHash: checksum, requiredCapabilities: [], operations
});
const rejected = (action, code) => assert.throws(action, error =>
    error instanceof MigrationError && error.code === code,
    `Expected a stable ${code} diagnostic.`);

const vectors = JSON.parse(await readFile(new URL('./canonical-vectors.json', import.meta.url), 'utf8'));
for (const vector of vectors) test(`canonical JSON conformance: ${vector.name}`, async () => {
    const actual = canonicalJson(JSON.parse(vector.json));
    assert.equal(actual, vector.canonical);
    const expectedHash = 'sha256:' + createHash('sha256').update(vector.canonical, 'utf8').digest('hex');
    assert.equal(await sha256(actual), expectedHash);
});

test('SHA-256 matches the standard abc vector and hashes asset bytes exactly', async () => {
    assert.equal(await sha256('abc'), 'sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
    assert.notEqual(await sha256('{"a":1}'), await sha256('{"a":1}\n'));
});

test('canonical JSON rejects non-finite numbers and undefined', () => {
    for (const value of [Infinity, -Infinity, NaN, undefined, { invalid: undefined }]) {
        rejected(() => canonicalJson(value), 'MAGDB_INVALID_DOCUMENT');
    }
});

test('unknown protocol and required capability reject at the document boundary', () => {
    rejected(() => validateSchema({ ...model(), protocolVersion: 2 }), 'MAGDB_PROTOCOL_UNSUPPORTED');
    rejected(() => validateSchema({ ...model(), requiredCapabilities: ['future-staging'] }), 'MAGDB_CAPABILITY_UNSUPPORTED');
});

test('reserved stores and unsafe index paths are rejected', () => {
    for (const name of ['__magic_migrations__', '__proto__', 'constructor']) {
        const input = model(); input.stores[0].name = name;
        rejected(() => validateSchema(input), 'MAGDB_SCHEMA_INVALID');
    }
    const input = model(); input.stores[0].indexes = [{ ...index, keyPath: 'owner.__proto__.value' }];
    rejected(() => validateSchema(input), 'MAGDB_SCHEMA_INVALID');
});

test('native IndexedDB compound key restrictions fail before planning', () => {
    const input = model(); input.stores[0].keyPath = ['id', 'name']; input.stores[0].autoIncrement = true;
    rejected(() => validateSchema(input), 'MAGDB_SCHEMA_INVALID');
    input.stores[0].autoIncrement = false; input.stores[0].indexes = [{ ...index, keyPath: ['id', 'name'], multiEntry: true }];
    rejected(() => validateSchema(input), 'MAGDB_SCHEMA_INVALID');
});

test('physical schema comparison preserves compound key order and ignores CLR-only fields', () => {
    const input = model(); input.stores[0].keyPath = ['id', 'name'];
    const actual = physicalStores(input);
    input.stores[0].fields.push({ name: 'extra', type: 'string', nullable: true, codec: 'json' });
    assert.equal(samePhysical(actual, input), true);
    input.stores[0].keyPath = ['name', 'id'];
    assert.equal(samePhysical(actual, input), false);
});

test('manifest rejects ambiguous branches and duplicate schema revisions', () => {
    const fork = manifest(); fork.migrations.push({ ...fork.migrations[0], id: 'other-branch' });
    rejected(() => validateManifest(fork, 'clients'), 'MAGDB_HISTORY_INVALID');
    const duplicate = manifest(); duplicate.schemas.push(copy(duplicate.schemas[0]));
    rejected(() => validateManifest(duplicate, 'clients'), 'MAGDB_MANIFEST_INVALID');
});

test('manifest rejects unsupported digests and another schema identity', () => {
    const input = manifest(); input.schemas[0].sha256 = 'md5:' + 'a'.repeat(32);
    rejected(() => validateManifest(input, 'clients'), 'MAGDB_ASSET_INVALID');
    rejected(() => validateManifest(manifest(), 'other'), 'MAGDB_SCHEMA_ID_MISMATCH');
});

test('index creation must produce the declared target', () => {
    const from = model(); const to = copy(from); to.stores[0].indexes.push(index);
    assert.doesNotThrow(() => validateMigration(migration([{ op: 'createIndex', store: 'clients', index }]), manifest().migrations[0], from, to));
    rejected(() => validateMigration(migration([]), manifest().migrations[0], from, to), 'MAGDB_TARGET_MISMATCH');
});

test('logical CLR field edits alone do not invent physical table changes', () => {
    const from = model(); const to = copy(from);
    to.stores[0].fields.push({ name: 'optional', type: 'string', nullable: true, codec: 'json' });
    assert.doesNotThrow(() => validateMigration(migration([]), manifest().migrations[0], from, to));
});

test('unresolved and unknown operations cannot masquerade as no-op upgrades', () => {
    const from = model();
    rejected(() => validateMigration(migration([{ op: 'manual', reason: 'Choose converter' }]), manifest().migrations[0], from, from), 'MAGDB_UNRESOLVED_MIGRATION');
    rejected(() => validateMigration(migration([{ op: 'future-rebuild' }]), manifest().migrations[0], from, from), 'MAGDB_OPERATION_UNSUPPORTED');
});

test('destructive operations require explicit allowDataLoss even with correct target', () => {
    const from = model(); const to = { ...copy(from), stores: [] };
    rejected(() => validateMigration(migration([{ op: 'deleteStore', store: 'clients' }]), manifest().migrations[0], from, to), 'MAGDB_DESTRUCTIVE_OPERATION');
    assert.doesNotThrow(() => validateMigration(migration([{ op: 'deleteStore', store: 'clients', allowDataLoss: true }]), manifest().migrations[0], from, to));
});

test('resumable mode rejects structural changes before any transaction', () => {
    const from = model(); const to = copy(from); to.stores[0].indexes.push(index);
    const plan = { ...migration([{ op: 'createIndex', store: 'clients', index }]), mode: 'resumable' };
    rejected(() => validateMigration(plan, manifest().migrations[0], from, to), 'MAGDB_OPERATION_UNSUPPORTED');
});

test('renaming stores proves exact resulting structure', () => {
    const from = model(); const to = copy(from); to.stores[0].name = 'customers';
    assert.doesNotThrow(() => validateMigration(migration([{ op: 'renameStore', store: 'clients', to: 'customers' }]), manifest().migrations[0], from, to));
    rejected(() => validateMigration(migration([{ op: 'renameStore', store: 'missing', to: 'customers' }]), manifest().migrations[0], from, to), 'MAGDB_OPERATION_INVALID');
});

test('unsafe field edits and missing default values reject', () => {
    const from = model();
    for (const path of ['__proto__', 'nested.value', 'constructor']) {
        rejected(() => validateMigration(migration([{ op: 'setMissing', store: 'clients', path, value: 1 }]), manifest().migrations[0], from, from), 'MAGDB_OPERATION_INVALID');
    }
    rejected(() => validateMigration(migration([{ op: 'setMissing', store: 'clients', path: 'newValue' }]), manifest().migrations[0], from, from), 'MAGDB_OPERATION_INVALID');
});

test('custom transformations require immutable module bytes', () => {
    const from = model();
    rejected(() => validateMigration(migration([{ op: 'transform', store: 'clients', module: 'transform.js' }]), manifest().migrations[0], from, from), 'MAGDB_ASSET_INVALID');
    assert.doesNotThrow(() => validateMigration(migration([{ op: 'transform', store: 'clients', module: 'transform.js', sha256: checksum }]), manifest().migrations[0], from, from));
});

test('generic record transforms cannot silently switch numeric storage formats', () => {
    const from = model(); const to = { ...copy(from), numericFormat: 'lossless-v1' };
    rejected(() => validateMigration(migration([]), manifest().migrations[0], from, to), 'MAGDB_CODEC_TRANSITION_UNSUPPORTED');
});
