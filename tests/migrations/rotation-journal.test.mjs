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
    mode: 'online', modelHash: 'model', policyFingerprint: 'policy', writeKeyId: 'new',
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
