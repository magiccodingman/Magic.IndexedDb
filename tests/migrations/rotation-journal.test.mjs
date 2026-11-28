import assert from 'node:assert/strict';
import test from 'node:test';
import { importEncryptionKey } from '../../Magic.IndexedDb/wwwroot/utilities/encryptionEnvelope.js';
import { sealRotationJournal, verifyRotationJournal } from '../../Magic.IndexedDb/wwwroot/utilities/rotationJournal.js';

globalThis.location = { origin: 'https://rotation.example.test' };
const generate = () => importEncryptionKey(crypto.getRandomValues(new Uint8Array(32)));
const keys = new Map([['old', await generate()], ['new', await generate()]]);
const vault = { database: 'catalog', account: 'account' };
const lease = () => {};
const state = {
    version: 1, rotationId: 'operation-1', sourceKeyId: 'old', targetKeyId: 'new',
    mode: 'online', modelHash: 'model', policyFingerprint: 'policy', writeKeyId: 'new', priorKeyIds: ['old'],
    sequence: 1, phase: 'converting', storeIndex: 0, afterKey: ['documents', 42],
    converted: 2, verified: 0, paused: false
};

test('both keys authenticate exact rotation progress and the native compound checkpoint', async () => {
    const document = await sealRotationJournal(state, keys, vault, lease);
    assert.deepEqual(await verifyRotationJournal(document, keys, vault, lease), state);
    await assert.rejects(verifyRotationJournal(document, new Map([['new', keys.get('new')]]), vault, lease), /KEY_UNAVAILABLE/);
    await assert.rejects(verifyRotationJournal(document, new Map(keys).set('old', await generate()), vault, lease), /STATE_INVALID/);
});

test('journal binds mode, account, database, write generation and checkpoint', async () => {
    const document = await sealRotationJournal(state, keys, vault, lease);
    for (const patch of [{ mode: 'exclusive' }, { afterKey: ['documents', 43] }, { writeKeyId: 'old' }]) {
        await assert.rejects(verifyRotationJournal({ ...document, state: { ...state, ...patch } }, keys, vault, lease), /STATE_INVALID/);
    }
    await assert.rejects(verifyRotationJournal(document, keys, { ...vault, account: 'other' }, lease), /STATE_INVALID/);
    await assert.rejects(verifyRotationJournal(document, keys, { ...vault, database: 'other' }, lease), /STATE_INVALID/);
});

test('mixed checkpoints, missing seals and revoked leases cannot authenticate', async () => {
    const first = await sealRotationJournal(state, keys, vault, lease);
    const second = await sealRotationJournal({ ...state, sequence: 2, converted: 3 }, keys, vault, lease);
    await assert.rejects(verifyRotationJournal({ ...second, sourceSeal: first.sourceSeal }, keys, vault, lease), /STATE_INVALID/);
    await assert.rejects(verifyRotationJournal({ ...first, targetSeal: undefined }, keys, vault, lease), /STATE_INVALID/);
    let checks = 0;
    await assert.rejects(verifyRotationJournal(first, keys, vault, () => {
        if (++checks > 1) throw new Error('authorization revoked');
    }), /authorization revoked/);
});

test('foreground write fence changes at verification but not on ordinary progress', async () => {
    const { sealRotationFence, verifyRotationFence } = await import('../../Magic.IndexedDb/wwwroot/utilities/rotationFence.js');
    const target = keys.get('new');
    const fence = await sealRotationFence(state, target, vault, lease);
    await verifyRotationFence(fence, { ...state, sequence: 2, converted: 3 }, target, vault, lease);
    await assert.rejects(verifyRotationFence(fence, { ...state, phase: 'verifying' }, target, vault, lease), /STATE_INVALID/);
    await assert.rejects(verifyRotationFence(fence, { ...state, mode: 'exclusive' }, target, vault, lease), /STATE_INVALID/);
});

test('admission rejects two non-extractable references to the same key material', async () => {
    const { assertDistinctRotationKeys } = await import('../../Magic.IndexedDb/wwwroot/utilities/rotationFence.js');
    const bytes = crypto.getRandomValues(new Uint8Array(32));
    const first = await importEncryptionKey(bytes);
    const second = await importEncryptionKey(bytes);
    bytes.fill(0);
    await assert.rejects(assertDistinctRotationKeys(first, second, 'original', vault, lease), /KEY_REUSED/);
    await assertDistinctRotationKeys(first, keys.get('new'), 'original', vault, lease);
});
