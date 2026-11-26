// Native transactions here contain only IndexedDB requests. Prepare encryption
// or authentication before opening the write transaction.
const requestResult = request => new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
});

const transactionDone = transaction => new Promise((resolve, reject) => {
    transaction.oncomplete = resolve;
    transaction.onabort = () => reject(transaction.error ?? new Error("Magic rotation transaction aborted."));
});

function bytes(value) {
    if (value instanceof ArrayBuffer) return new Uint8Array(value);
    if (ArrayBuffer.isView(value)) return new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
    return null;
}

function sameStoredValue(left, right) {
    if (Object.is(left, right)) return true;
    if (!left || !right || typeof left !== "object" || typeof right !== "object" ||
        Object.getPrototypeOf(left) !== Object.getPrototypeOf(right)) return false;

    const leftBytes = bytes(left);
    if (leftBytes) {
        const rightBytes = bytes(right);
        return leftBytes.length === rightBytes.length && leftBytes.every((value, index) => value === rightBytes[index]);
    }
    if (left instanceof Date) return left.getTime() === right.getTime();
    if (Array.isArray(left) && left.length !== right.length) return false;
    if (!Array.isArray(left) && Object.getPrototypeOf(left) !== Object.prototype) return false;
    const keys = Object.keys(left);
    return keys.length === Object.keys(right).length &&
        keys.every(key => Object.hasOwn(right, key) && sameStoredValue(left[key], right[key]));
}

export async function readRotationBatch(db, storeName, afterKey, batchSize) {
    if (!Number.isSafeInteger(batchSize) || batchSize < 1) throw new RangeError("Magic rotation requires a positive batch size.");
    const range = afterKey === null ? null : IDBKeyRange.lowerBound(afterKey, true);
    const tx = db.transaction(storeName, "readonly");
    const done = transactionDone(tx);
    const rows = [];
    try {
        await new Promise((resolve, reject) => {
            const cursor = tx.objectStore(storeName).openCursor(range);
            cursor.onerror = () => reject(cursor.error);
            cursor.onsuccess = () => {
                const row = cursor.result;
                if (!row) return resolve();
                rows.push({ key: row.primaryKey, record: row.value });
                if (rows.length === batchSize) resolve();
                else row.continue();
            };
        });
        await done;
    } catch (error) {
        await done.catch(() => {});
        throw error;
    }
    return rows;
}

// Check the exact authenticated metadata and stored rows before any writes.
// A concurrent delete, replacement, or public patch forces a fresh preparation.
export async function commitRotationBatch(db, expectations, writes, assertCurrent, signal) {
    signal?.throwIfAborted();
    assertCurrent();
    const names = [...new Set([...expectations, ...writes].map(item => item.store))];
    const tx = db.transaction(names, "readwrite");
    const done = transactionDone(tx);
    const abort = () => {
        try { tx.abort(); } catch { /* The transaction already finished. */ }
    };
    signal?.addEventListener("abort", abort, { once: true });
    try {
        const current = await Promise.all(expectations.map(item => requestResult(tx.objectStore(item.store).get(item.key))));
        signal?.throwIfAborted();
        assertCurrent();
        if (!expectations.every((item, index) => sameStoredValue(item.value, current[index]))) {
            await done;
            return false;
        }

        // Native record stores have inline keys. Metadata uses out-of-line keys.
        await Promise.all(writes.map(item => {
            const store = tx.objectStore(item.store);
            return requestResult(store.keyPath === null ? store.put(item.value, item.key) : store.put(item.value));
        }));
        signal?.throwIfAborted();
        assertCurrent();
        await done;
        return true;
    } catch (error) {
        abort();
        await done.catch(() => {});
        throw error;
    } finally {
        signal?.removeEventListener("abort", abort);
    }
}
