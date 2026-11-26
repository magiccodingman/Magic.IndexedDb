// Application-owned bridge example. Every cooperating v2 writer uses this
// gate around its entire write transaction, including background workers.
const lockName = 'invoice-app:storage-cutover';
const databaseName = 'invoice-app:rollout-state';

function gateState(stopWrites) {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 1);
        request.onupgradeneeded = () => request.result.createObjectStore('gate');
        request.onerror = () => reject(request.error);
        request.onsuccess = () => {
            const db = request.result;
            const transaction = db.transaction('gate', stopWrites ? 'readwrite' : 'readonly');
            const store = transaction.objectStore('gate');
            const operation = stopWrites ? store.put(true, 'v2WritesStopped') : store.get('v2WritesStopped');
            transaction.onabort = () => { db.close(); reject(transaction.error); };
            transaction.oncomplete = () => { db.close(); resolve(operation.result); };
        };
    });
}

export function legacyWrite(write) {
    return navigator.locks.request(lockName, { mode: 'shared' }, async () => {
        if (await gateState(false)) throw new Error('APP_LEGACY_WRITES_STOPPED');
        return await write();
    });
}

export function beginCutover(checkPrerequisites, initialize) {
    return navigator.locks.request(lockName, async () => {
        // Application checks package availability, client support and whether
        // all acknowledged local work can be retained or synchronized.
        if (!await checkPrerequisites()) throw new Error('APP_CUTOVER_NOT_APPROVED');
        await gateState(true);
        // Leave the fence in place if v3 initialization fails. The application
        // can retry initialization or offer export/support without un-fencing.
        return await initialize();
    });
}
