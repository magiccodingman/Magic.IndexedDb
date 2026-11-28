import assert from 'node:assert/strict';
import test from 'node:test';
import { importEncryptionKey } from '../../Magic.IndexedDb/wwwroot/utilities/encryptionEnvelope.js';
import { encryptedRecordMember, openRecord, sealRecord } from '../../Magic.IndexedDb/wwwroot/utilities/encryptionRecord.js';
import { openRotatingRecord, prepareRotatedRecord } from '../../Magic.IndexedDb/wwwroot/utilities/rotationRecord.js';

globalThis.location = { origin: 'https://rotation.example.test' };

const original = await importEncryptionKey(crypto.getRandomValues(new Uint8Array(32)));
const target = await importEncryptionKey(crypto.getRandomValues(new Uint8Array(32)));
const keys = new Map([['original', original], ['target', target]]);
const context = { database: 'example', account: 'owner', store: 'documents', primaryKey: ['catalog', 7] };
const row = { group: 'catalog', id: 7, label: 'Example', details: { exact: '9007199254740993' } };

for (const [name, policy, member] of [
    ['field', { fields: new Set(['details', 'optional']), exposed: null }, 'details'],
    ['record', { fields: new Set(), exposed: new Set(['group', 'id', 'label']) }, encryptedRecordMember],
]) {
    test(`authenticates and converts a ${name}-protected record without changing public or nested values`, async () => {
        const saved = await sealRecord(row, policy, original, 'original', context);
        assert.deepEqual(await openRotatingRecord(saved, policy, keys, context), { record: row, keyId: 'original' });
        const rotated = await prepareRotatedRecord(saved, policy, keys, 'target', context);
        assert.equal(rotated.label, row.label);
        assert.notDeepEqual(rotated[member].nonce, saved[member].nonce);
        assert.deepEqual(await openRotatingRecord(rotated, policy, new Map([['target', target]]), context),
            { record: row, keyId: 'target' });
        await assert.rejects(openRecord(rotated, policy, original, 'target', context), /authentication failed/);
        assert.equal(await prepareRotatedRecord(rotated, policy, keys, 'target', context), null);
    });

    test(`rejects unauthorized or invalid generation hints for a ${name}-protected record`, async () => {
        const saved = await sealRecord(row, policy, original, 'original', context);
        await assert.rejects(openRotatingRecord(saved, policy, new Map(), context), /KEY_UNAVAILABLE/);
        await assert.rejects(openRotatingRecord(saved, policy, keys, { ...context, database: 'different' }), /RECORD_INVALID/);
        const altered = structuredClone(saved);
        altered[member].keyId = 'target';
        await assert.rejects(prepareRotatedRecord(altered, policy, keys, 'target', context), /RECORD_INVALID/);
    });
}

test('records without protected members require no conversion', async () => {
    const policy = { fields: new Set(), exposed: null };
    assert.deepEqual(await openRotatingRecord(row, policy, new Map(), context), { record: row, keyId: null });
    assert.equal(await prepareRotatedRecord(row, policy, keys, 'target', context), null);
});
