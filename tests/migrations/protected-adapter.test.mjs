// Web Crypto/policy contracts run under Node. Real IndexedDB transaction and
// browser interruption coverage belongs to the migration browser matrix.
import assert from 'node:assert/strict';
import test from 'node:test';
import { createProtectedMigrationAdapter, isProtectedMigrationAdapter, equalEnvelope } from '../../Magic.IndexedDb/wwwroot/migrations/protectedMigrationAdapter.js';
import { importEncryptionKey, encryptValue, decryptValue } from '../../Magic.IndexedDb/wwwroot/utilities/encryptionEnvelope.js';
import { createProtectionPolicy, portableProtectionPolicy, matchesProtectionFingerprint } from '../../Magic.IndexedDb/wwwroot/utilities/protectionPolicy.js';
import { createProtectedStorage } from '../../Magic.IndexedDb/wwwroot/utilities/protectedStorage.js';

globalThis.location = { origin: 'https://migration.example.test' };
const keyBytes = crypto.getRandomValues(new Uint8Array(32));
const key = await importEncryptionKey(keyBytes);
const identity = { name: '__magic_protected__:fixture', account: 'account-1', keyId: 'key-1', key };
const model = (protection = { encryptedFields: ['notes'] }) => ({
    protocolVersion: 1, schemaId: 'fixture', numericFormat: 'legacy', stores: [{
        name: 'rows', keyPath: 'id', autoIncrement: false, indexes: [], fields: [], ...protection
    }]
});
const native = (protection = { encryptedFields: ['notes'] }) => [{
    tableName: 'rows', columnNamesInCompoundKey: ['id'], primaryKeyAuto: false,
    indexes: [], uniqueIndexes: [], columnNamesInCompoundIndex: [], ...protection
}];
const metadataContext = { database: identity.name, account: identity.account, store: '__magic_encryption', primaryKey: 'policy', member: null };
const binding = { schemaId: 'fixture', revision: 1, schemaHash: 'sha256:' + 'a'.repeat(64), migrationId: 'change', migrationHash: 'sha256:' + 'b'.repeat(64) };
const rejects = (promise, code) => assert.rejects(promise, error => error.code === code && error.cause === undefined);

test('managed policy authenticates its identity and rejects wrong keys, accounts, databases and models', async () => {
    const adapter = await createProtectedMigrationAdapter(identity);
    const schema = model();
    const envelope = await adapter.createPolicy(schema);
    await adapter.verifyPolicy(envelope, schema);
    assert.equal(isProtectedMigrationAdapter(adapter), true);
    assert.equal(isProtectedMigrationAdapter({ ...adapter }), false);
    for (const changed of [
        { account: 'another-account' }, { name: 'another-database' }, { keyId: 'another-key' },
        { key: await importEncryptionKey(crypto.getRandomValues(new Uint8Array(32))) }
    ]) await rejects((await createProtectedMigrationAdapter({ ...identity, ...changed })).verifyPolicy(envelope, schema), 'MAGDB_AUTHORIZATION_REJECTED');
    await rejects(adapter.verifyPolicy(envelope, model({ encryptedFields: ['name'] })), 'MAGDB_PROTECTION_POLICY_MISMATCH');
    const changed = structuredClone(envelope); new Uint8Array(changed.ciphertext)[0] ^= 1;
    await rejects(adapter.verifyPolicy(changed, schema), 'MAGDB_AUTHORIZATION_REJECTED');
});

test('legacy protected policy fingerprints stay exact and can be adopted semantically', async () => {
    const source = native();
    const legacy = createProtectionPolicy(source);
    assert.equal(legacy.policyFingerprint, '[["rows",["id"],false,[],[],[],["notes"],null]]');
    const saved = await encryptValue(JSON.stringify(['Magic.IndexedDb/policy', 1, legacy.policyFingerprint]), key, identity.keyId, metadataContext);
    const adapter = await createProtectedMigrationAdapter(identity);
    await adapter.verifyPolicy(saved, model());
    const managed = await adapter.createPolicy(model());
    assert.notDeepEqual(saved, managed);
    await adapter.verifyPolicy(managed, model());
});

test('legacy dual index annotations and lossless policy normalize without weakening native uniqueness', async () => {
    const source = native(); source[0].indexes = ['name']; source[0].uniqueIndexes = ['name'];
    const schema = model(); schema.numericFormat = 'lossless-v1';
    schema.stores[0].indexes = [{ name: 'name', keyPath: 'name', unique: true, multiEntry: false }];
    const legacy = createProtectionPolicy(source, { version: 1, mode: 'lossless' });
    assert.equal(matchesProtectionFingerprint(legacy.policyFingerprint, schema), true);
    const changed = structuredClone(schema); changed.stores[0].indexes[0].unique = false;
    assert.equal(matchesProtectionFingerprint(legacy.policyFingerprint, changed), false);
    changed.numericFormat = 'legacy';
    assert.equal(matchesProtectionFingerprint(legacy.policyFingerprint, changed), false);
    const bad = JSON.parse(legacy.policyFingerprint); bad.numericStorage.extra = true;
    assert.equal(matchesProtectionFingerprint(JSON.stringify(bad), schema), false);
});

test('a managed policy can protect its lifecycle after its last encrypted field becomes plaintext', async () => {
    const schema = model({});
    assert.throws(() => createProtectionPolicy(native({})));
    const policy = createProtectionPolicy(native({}), null, { managedSchema: schema, allowEmptyProtection: true });
    assert.equal(policy.policyFingerprint, portableProtectionPolicy(schema).policyFingerprint);
    const adapter = await createProtectedMigrationAdapter(identity);
    await adapter.verifyPolicy(await adapter.createPolicy(schema), schema);
    const different = model({}); different.stores[0].autoIncrement = true;
    assert.throws(() => createProtectionPolicy(native({}), null, { managedSchema: different, allowEmptyProtection: true }));
});

test('field protection transitions both ways preserve null, missing and ordinary values', async () => {
    const adapter = await createProtectedMigrationAdapter(identity);
    const protectedModel = model(), plain = model({});
    for (const row of [{ id: 1, notes: 'private-value', name: 'public' }, { id: 2, notes: null }, { id: 3 }]) {
        const encoded = await adapter.sealRecord(row, protectedModel, 'rows', row.id);
        assert.equal(encoded.notes.version, 1);
        assert.notEqual(encoded.notes, row.notes);
        const logical = await adapter.openRecord(encoded, protectedModel, 'rows', row.id);
        assert.deepEqual(logical, row);
        const unprotected = await adapter.sealRecord(logical, plain, 'rows', row.id);
        assert.deepEqual(unprotected, row);
        assert.deepEqual(await adapter.openRecord(await adapter.sealRecord(unprotected, protectedModel, 'rows', row.id), protectedModel, 'rows', row.id), row);
    }
});

test('whole-record and field transitions apply the intended public allowlist', async () => {
    const adapter = await createProtectedMigrationAdapter(identity);
    const row = { id: 1, name: 'public-or-private', notes: 'secret', futureMember: 'also-secret' };
    const whole = model({ exposedFields: ['id'] });
    const encoded = await adapter.sealRecord(row, whole, 'rows', row.id);
    assert.deepEqual(Object.keys(encoded).sort(), ['__magic_record', 'id']);
    const logical = await adapter.openRecord(encoded, whole, 'rows', row.id);
    assert.deepEqual(logical, row);
    const publicName = model({ exposedFields: ['id', 'name'] });
    const reencoded = await adapter.sealRecord(logical, publicName, 'rows', row.id);
    assert.equal(reencoded.name, row.name);
    assert.equal(Object.hasOwn(reencoded, 'futureMember'), false);
    assert.deepEqual(await adapter.openRecord(reencoded, publicName, 'rows', row.id), row);
    const field = await adapter.sealRecord(logical, model(), 'rows', row.id);
    assert.equal(field.name, row.name);
    assert.equal(field.notes.version, 1);
    assert.equal(Object.hasOwn(field, '__magic_record'), false);
});

test('record authentication rejects copied keys and damaged envelopes without exposing record contents', async () => {
    const adapter = await createProtectedMigrationAdapter(identity);
    const schema = model(), row = { id: 1, notes: 'secret-marker' };
    const encoded = await adapter.sealRecord(row, schema, 'rows', row.id);
    await rejects(adapter.openRecord(encoded, schema, 'rows', 2), 'MAGDB_PROTECTION_RECORD_INVALID');
    new Uint8Array(encoded.notes.ciphertext)[0] ^= 1;
    await rejects(adapter.openRecord(encoded, schema, 'rows', 1), 'MAGDB_PROTECTION_RECORD_INVALID');
    await assert.rejects(adapter.sealRecord({ ...row, __magic_record: 'secret-marker' }, schema, 'rows', 1), error =>
        error.code === 'MAGDB_PROTECTION_RECORD_INVALID' && !JSON.stringify(error).includes('secret-marker') && error.cause === undefined);
});

test('protected primary keys and indexes stay public and supported by the existing envelope format', async () => {
    assert.throws(() => portableProtectionPolicy(model({ encryptedFields: ['id'] })));
    assert.throws(() => portableProtectionPolicy(model({ exposedFields: [] })));
    const auto = model(); auto.stores[0].autoIncrement = true;
    assert.throws(() => portableProtectionPolicy(auto));
    const indexed = model(); indexed.stores[0].indexes = [{ name: 'notes', keyPath: 'notes', unique: false, multiEntry: false }];
    assert.throws(() => portableProtectionPolicy(indexed));
    const adapter = await createProtectedMigrationAdapter(identity);
    await rejects(adapter.sealRecord({ id: new Date(), notes: 'private' }, model(), 'rows', new Date()), 'MAGDB_PROTECTION_RECORD_INVALID');
});

test('migration fence rejects ordinary unlock and binds the exact interrupted edge', async () => {
    const adapter = await createProtectedMigrationAdapter(identity);
    const fence = await adapter.createFence(binding);
    await adapter.authenticateFence(fence, binding);
    await rejects(adapter.verifyPolicy(fence, model()), 'MAGDB_PROTECTION_POLICY_MISMATCH');
    await rejects(adapter.authenticateFence(fence, { ...binding, migrationHash: 'sha256:' + 'c'.repeat(64) }), 'MAGDB_PROTECTION_STATE_INVALID');
    assert.equal(equalEnvelope(fence, structuredClone(fence)), true);
    const another = await adapter.createFence(binding);
    assert.equal(equalEnvelope(fence, another), false);
});

test('journal authentication binds recovery progress and preserves Date and binary cursor identities', async () => {
    const adapter = await createProtectedMigrationAdapter(identity);
    for (const lastKey of ['key', 42, ['compound', 5], new Date('2024-01-01'), new Uint8Array([1, 2, 3])]) {
        const state = { protocolVersion: 3, revision: 1, status: 'recovering', history: [],
            active: { id: 'original', processedRecords: 2, lastKey },
            recovery: { phase: 'repair', repaired: 1, verified: 0, lastKey } };
        const seal = await adapter.sealState(state);
        await adapter.verifyState(seal, structuredClone(state));
        const changed = structuredClone(state); changed.recovery.repaired++;
        await rejects(adapter.verifyState(seal, changed), 'MAGDB_PROTECTION_STATE_INVALID');
        const changedKey = structuredClone(state); changedKey.active.lastKey = 'replacement';
        await rejects(adapter.verifyState(seal, changedKey), 'MAGDB_PROTECTION_STATE_INVALID');
    }
});

test('journal proof cannot be moved to a policy or another account', async () => {
    const adapter = await createProtectedMigrationAdapter(identity);
    const state = { status: 'ready', protocolVersion: 3, revision: 2, history: [] };
    const proof = await adapter.sealState(state);
    await rejects(adapter.verifyPolicy(proof, model()), 'MAGDB_AUTHORIZATION_REJECTED');
    const foreign = await createProtectedMigrationAdapter({ ...identity, account: 'foreign' });
    await rejects(foreign.verifyState(proof, state), 'MAGDB_PROTECTION_STATE_INVALID');
});

test('revoking a lease while crypto is pending discards its result', async () => {
    let active = true;
    const adapter = await createProtectedMigrationAdapter({ ...identity, assertCurrent() { if (!active) throw new Error('private application reason'); } });
    const pending = adapter.sealRecord({ id: 1, notes: 'private' }, model(), 'rows', 1);
    active = false;
    await rejects(pending, 'MAGDB_AUTHORIZATION_LOST');
    assert.throws(() => adapter.assertCurrent(), error => error.code === 'MAGDB_AUTHORIZATION_LOST' && error.cause === undefined);
});

test('aborted authorization and unsupported rotation fail before producing ciphertext', async () => {
    const controller = new AbortController(); controller.abort();
    await rejects(createProtectedMigrationAdapter({ ...identity, signal: controller.signal }), 'MAGDB_AUTHORIZATION_LOST');
    await rejects(createProtectedMigrationAdapter({ ...identity, targetKeyId: 'key-2' }), 'MAGDB_KEY_ROTATION_UNSUPPORTED');
    await rejects(createProtectedMigrationAdapter({ ...identity, key: new Uint8Array(8) }), 'MAGDB_AUTHORIZATION_REJECTED');
});

test('managed CRUD pins the ready journal and includes it in the write transaction', async () => {
    const adapter = await createProtectedMigrationAdapter(identity);
    const schema = model();
    const journal = { protocolVersion: 3, revision: 2, status: 'ready', schemaId: 'fixture', history: [], active: null };
    const seal = await adapter.sealState(journal);
    const data = new Map([
        ['__magic_encryption', new Map([['policy', await adapter.createPolicy(schema)], ['migration-state', seal]])],
        ['__magic_migrations__', new Map([['state', structuredClone(journal)]])],
        ['rows', new Map()]
    ]);
    let transactions = [], writes = 0;
    const tables = [...data.keys()].map(name => ({
        name, async get(key) { return structuredClone(data.get(name).get(key)); },
        async update() { writes++; return 1; }
    }));
    const db = { name: identity.name, tables, table: name => tables.find(table => table.name === name),
        async open() {}, async transaction(mode, stores, action) { transactions.push(stores.map(store => store.name)); return action(); } };
    const storage = createProtectedStorage(db, identity.account, native(), null, { managedSchema: schema, allowEmptyProtection: true });
    storage.setManagedReadiness({ journal, journalSeal: seal });
    await storage.unlock(identity.keyId, key, false);
    await storage.forTable('rows').patch(1, { name: 'allowed' });
    assert.equal(writes, 1);
    assert.deepEqual(transactions[0], ['rows', '__magic_encryption', '__magic_migrations__']);
    for (const corrupt of [
        () => data.get('__magic_migrations__').set('state', { ...journal, revision: 3 }),
        () => { const altered = structuredClone(seal); new Uint8Array(altered.ciphertext)[0] ^= 1; data.get('__magic_encryption').set('migration-state', altered); }
    ]) {
        data.get('__magic_migrations__').set('state', structuredClone(journal));
        data.get('__magic_encryption').set('migration-state', seal);
        corrupt();
        await rejects(storage.forTable('rows').patch(1, { name: 'must not write' }), 'MAGDB_PROTECTION_STATE_INVALID');
        assert.equal(writes, 1);
    }
    assert.throws(() => storage.setManagedReadiness({ journal: { ...journal, status: 'recovering' }, journalSeal: seal }),
        error => error.code === 'MAGDB_PROTECTION_STATE_INVALID');
});
