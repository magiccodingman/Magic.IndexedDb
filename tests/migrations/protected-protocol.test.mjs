import test from 'node:test';
import assert from 'node:assert/strict';
import { validateMigration, requireProtectedSchema, requirePlainSchema, losesProtection, canonicalJson } from '../../Magic.IndexedDb/wwwroot/migrations/protocol.js';
import { portableProtectionPolicy } from '../../Magic.IndexedDb/wwwroot/utilities/protectionPolicy.js';

const cap = 'authenticatedMigrationsV1';
const digest = 'sha256:' + 'a'.repeat(64);
const model = policy => ({ protocolVersion: 1, schemaId: 'vault', numericFormat: 'legacy', requiredCapabilities: [cap],
    stores: [{ name: 'rows', keyPath: 'id', autoIncrement: false, indexes: [], fields: [
        { name: 'id', type: 'integer32', codec: 'json', nullable: false },
        { name: 'notes', type: 'string', codec: 'json', nullable: true }
    ], ...policy }] });
const ref = { id: 'change', fromRevision: 1, toRevision: 2, sha256: digest };
const document = operations => ({ protocolVersion: 1, schemaId: 'vault', id: ref.id, fromRevision: 1, toRevision: 2,
    fromSchemaHash: digest, toSchemaHash: digest, mode: 'resumable', requiredCapabilities: [cap], operations });
const validate = (from, to, operations, changes = {}) => validateMigration({ ...document(operations), ...changes }, ref, from, to, { authenticated: true });
const reject = (action, code) => assert.throws(action, error => error.code === code);

for (const [name, before, after, loss] of [
    ['plain to fields', {}, { encryptedFields: ['notes'] }, false],
    ['fields to plain', { encryptedFields: ['notes'] }, {}, true],
    ['fields to whole record', { encryptedFields: ['notes'] }, { exposedFields: ['id'] }, false],
    ['whole record to fields', { exposedFields: ['id'] }, { encryptedFields: ['notes'] }, true],
    ['expose another record member', { exposedFields: ['id'] }, { exposedFields: ['id', 'notes'] }, true],
    ['protect a previously exposed member', { exposedFields: ['id', 'notes'] }, { exposedFields: ['id'] }, false]
]) test(`authenticated protocol: ${name} requires exactly its protection-loss decision`, () => {
    const from = model(before), to = model(after);
    assert.equal(losesProtection(from.stores[0], to.stores[0]), loss);
    const operation = { op: 'reprotect', store: 'rows' };
    if (loss) reject(() => validate(from, to, [operation]), 'MAGDB_PROTECTION_LOSS_REQUIRES_APPROVAL');
    validate(from, to, [{ ...operation, ...(loss ? { allowProtectionLoss: true } : {}) }]);
});

test('authenticated protocol requires authorization and immutable capability negotiation', () => {
    const source = model({ encryptedFields: ['notes'] });
    const step = document([{ op: 'reprotect', store: 'rows' }]);
    reject(() => validateMigration(step, ref, source, source), 'MAGDB_PROTECTED_ADAPTER_REQUIRED');
    reject(() => validate(source, source, step.operations, { requiredCapabilities: [] }), 'MAGDB_CAPABILITY_REQUIRED');
    // All-public vaults retain their authenticated lifecycle.
    reject(() => validateMigration(document([]), ref, model({}), model({})), 'MAGDB_PROTECTED_ADAPTER_REQUIRED');
});

test('authenticated protocol prevents policy-only relabeling without rewriting records', () => {
    reject(() => validate(model({}), model({ encryptedFields: ['notes'] }), []), 'MAGDB_PROTECTION_REWRITE_REQUIRED');
});

test('authenticated protocol does not combine asynchronous record work with a version-change transaction', () => {
    const source = model({ encryptedFields: ['notes'] });
    reject(() => validate(source, source, [{ op: 'reprotect', store: 'rows' }], { mode: 'atomic' }), 'MAGDB_PROTECTION_PLAN_UNSUPPORTED');
    reject(() => validate(source, source, [{ op: 'reprotect', store: 'rows' }, { op: 'setMissing', store: 'rows', path: 'new', value: true }]), 'MAGDB_PROTECTION_PLAN_UNSUPPORTED');
});

test('authenticated protocol supports a separate public-index revision without record rewriting', () => {
    const source = model({ encryptedFields: ['notes'] }), target = structuredClone(source);
    const index = { name: 'byId', keyPath: 'id', unique: false, multiEntry: false };
    target.stores[0].indexes.push(index);
    validate(source, target, [{ op: 'createIndex', store: 'rows', index }], { mode: 'atomic' });
});

test('authenticated protocol rejects encrypted keys and native indexes', () => {
    reject(() => requireProtectedSchema(model({ encryptedFields: ['id'] })), 'MAGDB_PROTECTION_INVALID');
    const source = model({ encryptedFields: ['notes'] });
    source.stores[0].indexes = [{ name: 'byNotes', keyPath: 'notes', unique: false, multiEntry: false }];
    reject(() => requireProtectedSchema(source), 'MAGDB_PROTECTION_INVALID');
});

for (const nativePath of ['primary', 'index', 'compound index'])
test(`authenticated public ${nativePath} validates the adapter's top-level path contract`, () => {
    const source = model({});
    delete source.requiredCapabilities; // A public historical snapshot can precede capability enrollment.
    if (nativePath === 'primary') source.stores[0].keyPath = 'metadata.id';
    else source.stores[0].indexes.push({ name: 'byMetadata',
        keyPath: nativePath === 'index' ? 'metadata.kind' : ['id', 'metadata.kind'], unique: false, multiEntry: false });
    requirePlainSchema(source);
    reject(() => requireProtectedSchema(source), 'MAGDB_PROTECTION_INVALID');
    assert.throws(() => portableProtectionPolicy(source), /MAGDB_PROTECTION_POLICY_INVALID/);
});

test('authenticated public out-of-line stores retain their supported native policy', () => {
    const source = model({});
    source.stores[0].keyPath = null;
    source.stores[0].autoIncrement = true;
    requireProtectedSchema(source);
    portableProtectionPolicy(source);
});

test('authenticated transient public stores reject unsupported nested key paths', () => {
    const source = model({}), temporary = structuredClone(source.stores[0]);
    temporary.name = 'temporary'; temporary.keyPath = 'metadata.id';
    reject(() => validate(source, source, [{ op: 'createStore', definition: temporary },
        { op: 'deleteStore', store: 'temporary', allowDataLoss: true }], { mode: 'atomic' }), 'MAGDB_PROTECTION_INVALID');
});

test('authenticated protocol keeps downgrade approvals typed and snapshots unchanged', () => {
    const source = model({}), target = model({ encryptedFields: ['notes'] });
    const before = canonicalJson([source, target]);
    reject(() => validate(source, target, [{ op: 'reprotect', store: 'rows', allowProtectionLoss: 'yes' }]), 'MAGDB_PROTECTION_INVALID');
    validate(source, target, [{ op: 'reprotect', store: 'rows' }]);
    assert.equal(canonicalJson([source, target]), before);
});
