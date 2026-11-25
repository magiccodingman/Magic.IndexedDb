import { ensureDatabase } from './_content/Magic.IndexedDb/migrations/migrationEngine.js';

export async function seed() {
    const base = new URL('./releases/final/recovery-wasm/', location.href);
    const url = new URL('manifest.json', base);
    const manifest = await (await fetch(url)).json();
    const schema = await (await fetch(new URL(manifest.schemas[0].path, base))).json();
    await ensureDatabase({ name: 'recovery-wasm', schema });
    await new Promise((resolve, reject) => {
        const request = indexedDB.open('recovery-wasm');
        request.onerror = () => reject(request.error);
        request.onsuccess = () => {
            const db = request.result;
            const tx = db.transaction('records', 'readwrite');
            tx.oncomplete = () => { db.close(); resolve(); };
            tx.onabort = () => { db.close(); reject(tx.error); };
            for (const Id of [1, 2, 3]) tx.objectStore('records').add({ Id, Value: 1, Repairs: 0, Normalized: false });
        };
    });
    try {
        await ensureDatabase({ name: 'recovery-wasm', schema, manifestUrl: url.href, batchSize: 1 });
        throw new Error('Expected a partial migration.');
    } catch (error) {
        if (error.code !== 'MAGDB_HOOK_FAILED') throw error;
    }
    globalThis.pauseRecoveryVerifier = true;
    return manifest.recoveries[0].sha256;
}

export async function waitForVerifier() {
    const deadline = performance.now() + 15_000;
    while (!globalThis.recoveryVerifierEntered && performance.now() < deadline)
        await new Promise(resolve => setTimeout(resolve, 20));
    if (!globalThis.recoveryVerifierEntered) throw new Error('The recovery verifier did not start.');
}

export function releaseVerifier() { globalThis.pauseRecoveryVerifier = false; }
