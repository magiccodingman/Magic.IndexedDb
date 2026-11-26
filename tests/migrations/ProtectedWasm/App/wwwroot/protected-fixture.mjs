import * as api from './_content/Magic.IndexedDb/magicDB.js';

const physical = '__magic_protected__:published-migration-fixture';
const route = physical + ':seed';

async function rawRows() {
    const opening = indexedDB.open(physical);
    opening.onupgradeneeded = () => opening.transaction.abort();
    const db = await new Promise((resolve, reject) => {
        opening.onsuccess = () => resolve(opening.result);
        opening.onerror = () => reject(opening.error);
    });
    try {
        return await new Promise((resolve, reject) => {
            const request = db.transaction('records').objectStore('records').getAll();
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    } finally { db.close(); }
}

export async function seed() {
    if (!new URLSearchParams(location.search).has('released')) {
        await api.createDb({ name: route, encryption: { account: 'published-account', physicalName: physical },
            storeSchemas: [{ tableName: 'records', primaryKeyAuto: false, columnNamesInCompoundKey: ['Id'],
                indexes: ['Id'], uniqueIndexes: [], columnNamesInCompoundIndex: [], encryptedFields: ['Secret'] }] });
        await api.unlockProtectedDatabase(route, 'fixture-key', new Uint8Array(32).fill(55), true);
        for (const Id of [1, 2, 3]) await api.addItem({ dbName: route, storeName: 'records',
            record: { Id, Name: 'Public ' + Id, Secret: 'Private ' + Id, Visits: 0 } });
        api.releaseProtectedDatabase(route);
    }
    await assertOriginalCiphertext();
    globalThis.pauseProtectedFixture = true;
    globalThis.wholeProtectionObserved = 0;
    globalThis.assertWholeProtection = async () => {
        const rows = await rawRows();
        if (rows.length !== 3 || rows.some(row => Object.keys(row).sort().join(',') !== 'Id,__magic_record' ||
            !(row.__magic_record.ciphertext instanceof ArrayBuffer) || row.__magic_record.ciphertext.byteLength === 0))
            throw new Error('Whole-record ciphertext was not stored before declassification.');
        globalThis.wholeProtectionObserved++;
    };
}

export async function waitForHook() {
    const deadline = performance.now() + 15_000;
    while (!globalThis.protectedFixtureHookPending && performance.now() < deadline)
        await new Promise(resolve => setTimeout(resolve, 20));
    if (!globalThis.protectedFixtureHookPending) throw new Error('The authenticated transform did not start.');
}

export function releaseHook() { globalThis.pauseProtectedFixture = false; }
export function resetHookProbe() { globalThis.protectedFixtureHookPending = false; }

export async function assertOriginalCiphertext() {
    const rows = await rawRows();
    if (rows.length !== 3 || rows.some(row => !(row.Secret?.ciphertext instanceof ArrayBuffer) ||
        row.Secret.ciphertext.byteLength < 1 || row.Visits !== 0 || row.Name !== 'Public ' + row.Id))
        throw new Error('Original field ciphertext or uncommitted values changed.');
}

export async function assertFinalPlaintext() {
    const rows = await rawRows();
    if (globalThis.wholeProtectionObserved < 3 || rows.length !== 3 || rows.some(row =>
        row.Secret !== 'Private ' + row.Id || row.Name !== 'Public ' + row.Id || row.Visits !== 1 ||
        Object.hasOwn(row, '__magic_record')))
        throw new Error('The approved declassification did not preserve the logical records.');
}
