import assert from 'node:assert/strict';

// The public registration/unlock/CRUD boundary, using real Web Crypto and IndexedDB.
export function registerProtectedRegistrationTests({ test, schema, bundle, appendRevision, assets, jsonAsset, copy, registrationStore, inspect }) {
    function model(fields = ['secret']) {
        const result = schema();
        result.requiredCapabilities = ['authenticatedMigrationsV1'];
        result.stores[0].fields.push({ name: 'secret', type: 'string', nullable: false, codec: 'json' });
        result.stores[0].encryptedFields = fields;
        return result;
    }
    function plan(before, after, operations, options = {}) {
        return bundle(before, after, operations, { ...options,
            changeMigration: migration => { migration.requiredCapabilities = ['authenticatedMigrationsV1']; },
            changeManifest: manifest => { manifest.requiredCapabilities = ['authenticatedMigrationsV1']; }
        });
    }
    function initial(source) {
        const result = plan(source, source, []);
        result.manifest.targetRevision = 1;
        result.manifest.schemas = result.manifest.schemas.slice(0, 1);
        result.manifest.migrations = [];
        jsonAsset(result.base + '/manifest.json', result.manifest);
        return result;
    }
    function configuration(physical, route, source, manifestUrl) {
        return { name: physical + ':' + route, encryption: { account: 'registration-account', physicalName: physical },
            migrationSchema: source, migrationManifestUrl: manifestUrl,
            storeSchemas: [{ ...registrationStore(), encryptedFields: source.stores[0].encryptedFields }] };
    }

    test('authenticated registration authorizes before bootstrap and preserves ciphertext across lock', async ({ page }) => {
        const physical = '__magic_protected__:managed-bootstrap';
        const target = model();
        const config = configuration(physical, 'route', target, initial(target).url);
        const result = await page.evaluate(async ({ config, physical }) => {
            const api = await import('/engine/magicDB.js');
            await api.createDb(config);
            // Deferred authorization must use the registration snapshot, even
            // if a caller later reuses or mutates its original configuration.
            config.migrationSchema.schemaId = 'caller-mutated-schema';
            config.migrationSchema.stores[0].encryptedFields = [];
            const beforeAuthorization = await engine.inspectDatabase(physical);
            await api.unlockProtectedDatabase(config.name, 'test-key', new Uint8Array(32).fill(41), true);
            const afterAuthorization = await engine.inspectDatabase(physical);
            await api.ensureProtectedMigration(config.name);
            await api.addItem({ dbName: config.name, storeName: 'clients',
                record: { id: 1, name: 'Public', secret: 'Never persist this plaintext' } });
            const raw = (await readRows(physical))[0];
            const initial = await engine.inspectDatabase(physical);
            api.lockProtectedDatabase(config.name);
            let lockedCode;
            try { await api.findItem(config.name, 'clients', 1); }
            catch (error) { lockedCode = error.code; }
            await api.unlockProtectedDatabase(config.name, 'test-key', new Uint8Array(32).fill(41));
            await api.ensureProtectedMigration(config.name);
            const row = await api.findItem(config.name, 'clients', 1);
            const reopened = await engine.inspectDatabase(physical);
            api.releaseProtectedDatabase(config.name);
            return { beforeAuthorization, afterAuthorization, rawSecretType: typeof raw.secret,
                ciphertextBytes: raw.secret.ciphertext.byteLength, initial, reopened, lockedCode, row };
        }, { config, physical });
        assert.equal(result.beforeAuthorization, null);
        assert.equal(result.afterAuthorization, null);
        assert.equal(result.rawSecretType, 'object');
        assert.ok(result.ciphertextBytes > 0);
        assert.equal(result.initial.state.protocolVersion, 3);
        assert.equal(result.initial.state.status, 'ready');
        assert.equal(result.lockedCode, 'MAGDB_AUTHORIZATION_REQUIRED');
        assert.deepEqual(result.reopened, result.initial);
        assert.deepEqual(result.row, { id: 1, name: 'Public', secret: 'Never persist this plaintext' });
    });

    test('public rotation API resumes after release and unlocks with the new key only', async ({ page }) => {
        const physical = '__magic_protected__:public-rotation';
        const target = model();
        const config = configuration(physical, 'route', target, initial(target).url);
        const value = await page.evaluate(async config => {
            const api = await import('/engine/magicDB.js');
            const old = new Uint8Array(32).fill(41), next = new Uint8Array(32).fill(83);
            await api.createDb(config);
            await api.unlockProtectedDatabase(config.name, 'first', old, true);
            await api.ensureProtectedMigration(config.name);
            for (const id of [1, 2, 3]) await api.addItem({ dbName: config.name, storeName: 'clients',
                record: { id, name: 'Document ' + id, secret: 'Content ' + id } });
            const events = [];
            const unsubscribe = api.subscribeKeyRotationEvents(event => {
                events.push([event.phase, event.converted]);
                if (event.phase === 'converting' && event.converted === 1) api.cancelManagedEncryptionKeyRotation(config.name);
            });
            let cancelled = false;
            try { await api.rotateManagedEncryptionKey(config.name, { targetKeyId: 'next', mode: 'online', batchSize: 1,
                recoverable: true }, next); }
            catch (error) { cancelled = error.name === 'AbortError'; }
            unsubscribe();
            const partial = await api.getManagedKeyRotationStatus(config.name);
            const during = await api.findItem(config.name, 'clients', 2);
            api.releaseProtectedDatabase(config.name);
            await api.createDb(config);
            const hints = await api.getManagedEncryptionKeyHints(config.name);
            await api.authorizeManagedKeyRotation(config.name, 'first', old, 'next', next);
            const completed = await api.resumeManagedEncryptionKeyRotation(config.name, { batchSize: 1 });
            api.releaseProtectedDatabase(config.name);
            await api.createDb(config);
            const active = await api.getManagedEncryptionKeyHints(config.name);
            await api.unlockProtectedDatabase(config.name, 'next', next);
            await api.ensureProtectedMigration(config.name);
            const row = await api.findItem(config.name, 'clients', 1);
            const close = IDBDatabase.prototype.close;
            let connectionCloses = 0;
            IDBDatabase.prototype.close = function () {
                if (this.name === config.encryption.physicalName) connectionCloses++;
                return close.call(this);
            };
            let second;
            try {
                second = await api.rotateManagedEncryptionKey(config.name, {
                    targetKeyId: 'third', mode: 'read-only', recoverable: true, batchSize: 1
                }, new Uint8Array(32).fill(94));
            } finally { IDBDatabase.prototype.close = close; }
            let reused = false;
            try {
                await api.rotateManagedEncryptionKey(config.name, { targetKeyId: 'first', recoverable: true }, old);
            } catch (error) { reused = error.message.startsWith('MAGIC_ROTATION_KEY_REUSED:'); }
            const retained = await api.findItem(config.name, 'clients', 1);
            api.releaseProtectedDatabase(config.name);
            return { cancelled, partial, during, hints, completed, active, row, events, second, retained, reused, connectionCloses };
        }, config);
        assert.equal(value.cancelled, true);
        assert.equal(value.partial.converted, 1);
        assert.equal(value.partial.retirementReady, false);
        assert.equal(value.during.secret, 'Content 2');
        assert.deepEqual(value.hints, { active: true, keyId: 'first', targetKeyId: 'next' });
        assert.equal(value.completed.retirementReady, true);
        assert.equal(value.completed.converted, 3);
        assert.equal(value.completed.verified, 3);
        assert.deepEqual(value.active, { active: false, keyId: 'next' });
        assert.deepEqual(value.row, { id: 1, name: 'Document 1', secret: 'Content 1' });
        assert.deepEqual(value.events, [['converting', 0], ['converting', 1]]);
        assert.equal(value.second.retirementReady, true);
        assert.equal(value.second.targetKeyId, 'third');
        assert.equal(value.connectionCloses, 0);
        assert.deepEqual(value.retained, value.row);
        assert.equal(value.reused, true);
    });

    test('online rotation preserves independently authorized tab mutations while paused and resumed', async ({ page, context }) => {
        const physical = '__magic_protected__:rotation-shared-tabs';
        const target = model();
        const config = configuration(physical, 'route', target, initial(target).url);
        await page.evaluate(async config => {
            const api = await import('/engine/magicDB.js');
            await api.createDb(config);
            await api.unlockProtectedDatabase(config.name, 'original', new Uint8Array(32).fill(41), true);
            await api.ensureProtectedMigration(config.name);
            for (const id of [1, 2, 3]) await api.addItem({ dbName: config.name, storeName: 'clients',
                record: { id, name: 'Document ' + id, secret: 'Content ' + id } });
            const unsubscribe = api.subscribeKeyRotationEvents(event => {
                if (event.converted === 1) api.cancelManagedEncryptionKeyRotation(config.name);
            });
            try {
                await api.rotateManagedEncryptionKey(config.name, { targetKeyId: 'target', mode: 'online',
                    batchSize: 1, recoverable: true }, new Uint8Array(32).fill(55));
                throw new Error('Expected rotation interruption.');
            } catch (error) {
                if (error.name !== 'AbortError') throw error;
            } finally { unsubscribe(); }
            const paused = await api.pauseManagedEncryptionKeyRotation(config.name);
            if (!paused.paused || !paused.writesAllowed) throw new Error('The pause changed the online write policy.');
        }, config);
        const peer = await context.newPage();
        await peer.goto(page.url());
        await peer.evaluate(async config => {
            const api = await import('/engine/magicDB.js');
            await api.createDb(config);
            await api.authorizeManagedKeyRotation(config.name, 'original', new Uint8Array(32).fill(41),
                'target', new Uint8Array(32).fill(55));
            const row = await api.findItem(config.name, 'clients', 2);
            if (row.secret !== 'Content 2') throw new Error('The source generation was not readable.');
            await api.putItem({ dbName: config.name, storeName: 'clients',
                record: { id: 2, name: 'Updated in peer', secret: 'Updated content' } });
            await api.deleteItem({ dbName: config.name, storeName: 'clients', record: { id: 3 } });
            await api.addItem({ dbName: config.name, storeName: 'clients',
                record: { id: 4, name: 'Inserted in peer', secret: 'New content' } });
        }, config);
        const completed = await page.evaluate(async config => {
            const api = await import('/engine/magicDB.js');
            const status = await api.resumeManagedEncryptionKeyRotation(config.name, { batchSize: 1 });
            return { status, rows: await api.toArray(config.name, 'clients') };
        }, config);
        assert.equal(completed.status.retirementReady, true);
        assert.equal(completed.status.verified, 3);
        assert.deepEqual(completed.rows, [
            { id: 1, name: 'Document 1', secret: 'Content 1' },
            { id: 2, name: 'Updated in peer', secret: 'Updated content' },
            { id: 4, name: 'Inserted in peer', secret: 'New content' }
        ]);
        const rejected = await peer.evaluate(async config => {
            const api = await import('/engine/magicDB.js');
            try { await api.putItem({ dbName: config.name, storeName: 'clients',
                record: { id: 1, name: 'Stale write', secret: 'Must fail' } }); return false; }
            catch { return true; }
        }, config);
        assert.equal(rejected, true);
        await peer.close();
    });

    test('abrupt tab termination releases rotation ownership to a worker without replaying a committed batch', async ({ page, context }) => {
        const physical = '__magic_protected__:rotation-tab-close';
        const source = model();
        const config = configuration(physical, 'route', source, initial(source).url);
        const workerUrl = '/rotation-resume-worker.js';
        assets.set(workerUrl, { type: 'text/javascript', body: `
            import * as api from '/engine/magicDB.js';
            self.onmessage = async ({ data: config }) => {
                try {
                    await api.createDb(config);
                    const partial = await api.authorizeManagedKeyRotation(config.name, 'original', new Uint8Array(32).fill(41),
                        'target', new Uint8Array(32).fill(55));
                    const completed = await api.resumeManagedEncryptionKeyRotation(config.name, { batchSize: 1 });
                    self.postMessage({ partial, completed, rows: await api.toArray(config.name, 'clients') });
                } catch (error) { self.postMessage({ error: error.message }); }
                finally { api.releaseProtectedDatabase(config.name); }
            };
        ` });
        const readCiphertext = async physicalName => {
            const db = await new Promise((resolve, reject) => {
                const request = indexedDB.open(physicalName);
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
            try {
                return await new Promise((resolve, reject) => {
                    const request = db.transaction('clients').objectStore('clients').get(1);
                    request.onsuccess = () => resolve(request.result.secret);
                    request.onerror = () => reject(request.error);
                });
            } finally { db.close(); }
        };
        const peer = await context.newPage();
        await peer.goto(page.url());
        await page.evaluate(async config => {
            const api = await import('/engine/magicDB.js');
            await api.createDb(config);
            await api.unlockProtectedDatabase(config.name, 'original', new Uint8Array(32).fill(41), true);
            await api.ensureProtectedMigration(config.name);
            for (const id of [1, 2, 3]) await api.addItem({ dbName: config.name, storeName: 'clients',
                record: { id, name: 'Document ' + id, secret: 'Content ' + id } });
            api.subscribeKeyRotationEvents(event => {
                if (event.phase !== 'converting' || event.converted !== 1) return;
                crypto.subtle.encrypt = () => {
                    globalThis.rotationEncryptionPending = true;
                    return new Promise(() => {});
                };
            });
            void api.rotateManagedEncryptionKey(config.name, {
                targetKeyId: 'target', mode: 'online', batchSize: 1, recoverable: true
            }, new Uint8Array(32).fill(55)).catch(error => { globalThis.rotationFailure = error.message; });
        }, config);
        await page.waitForFunction(() => globalThis.rotationEncryptionPending || globalThis.rotationFailure);
        assert.equal(await page.evaluate(() => globalThis.rotationFailure ?? null), null);
        const committed = await peer.evaluate(readCiphertext, physical);
        assert.equal(typeof committed, 'object');
        await page.close();
        const result = await peer.evaluate(async ({ config, workerUrl }) => {
            const worker = new Worker(workerUrl, { type: 'module' });
            try {
                return await new Promise((resolve, reject) => {
                    worker.onmessage = event => resolve(event.data);
                    worker.onerror = event => reject(new Error(event.message));
                    worker.postMessage(config);
                });
            } finally { worker.terminate(); }
        }, { config, workerUrl });
        assert.equal(result.error, undefined);
        assert.equal(result.partial.converted, 1);
        assert.equal(result.completed.converted, 3);
        assert.equal(result.completed.verified, 3);
        assert.equal(result.completed.retirementReady, true);
        assert.deepEqual(await peer.evaluate(readCiphertext, physical), committed);
        assert.deepEqual(result.rows.map(row => row.secret), ['Content 1', 'Content 2', 'Content 3']);
        await peer.close();
    });

    if (process.env.MAGIC_ROTATION_BENCHMARK === '1') {
        for (const records of [1000, 10000, 100000]) for (const mode of ['exclusive', 'read-only', 'online']) {
            test(`rotation benchmark: ${mode}, ${records} documents`, async ({ page }) => {
                const source = model();
                const config = configuration('__magic_protected__:rotation-benchmark', 'route', source, initial(source).url);
                const result = await page.evaluate(async ({ config, records, mode }) => {
                    const api = await import('/engine/magicDB.js');
                    const route = config.name;
                    const sourceKey = new Uint8Array(32).fill(71), targetKey = new Uint8Array(32).fill(91);
                    await api.createDb(config);
                    await api.unlockProtectedDatabase(route, 'source', sourceKey, true);
                    await api.ensureProtectedMigration(route);
                    const payload = 'Text '.repeat(25);
                    for (let offset = 0; offset < records; offset += 256) {
                        const rows = Array.from({ length: Math.min(256, records - offset) }, (_, index) => ({
                            id: offset + index + 1, name: 'Example', secret: payload
                        }));
                        await api.bulkAddItem(route, 'clients', rows);
                    }
                    const usage = () => navigator.storage.estimate().then(value => value.usage ?? null).catch(() => null);
                    const startUsage = await usage();
                    const readSamples = [], writeSamples = [];
                    let observe;
                    let sampleError;
                    let verifyStart;
                    let maxTimerLag = 0;
                    let peakHeap = performance.memory?.usedJSHeapSize ?? null;
                    let previous = performance.now();
                    const read = () => api.findItem(route, 'clients', 1);
                    const sample = async (fn, values) => {
                        const start = performance.now();
                        try { await fn(); values.push(performance.now() - start); }
                        catch (error) {
                            if (!/^(MAGIC_ROTATION_ACCESS_FENCED|MAGIC_ROTATION_CONCURRENT_CHANGE|MAGIC_ENCRYPTION_POLICY_MISMATCH|MAGDB_AUTHORIZATION_REQUIRED):/.test(error.message)) throw error;
                        }
                    };
                    const baseline = [];
                    for (let i = 0; i < 12; i++) await sample(read, baseline);
                    const unsubscribe = api.subscribeKeyRotationEvents(event => {
                        if (event.phase === 'verifying') verifyStart ??= performance.now();
                    });
                    const interval = setInterval(() => {
                        const now = performance.now();
                        maxTimerLag = Math.max(maxTimerLag, now - previous - 20);
                        previous = now;
                        if (peakHeap !== null) peakHeap = Math.max(peakHeap, performance.memory.usedJSHeapSize);
                        if (observe || mode === 'exclusive') return;
                        observe = (async () => {
                            await sample(read, readSamples);
                            if (mode === 'online') await sample(() => api.putItem({ dbName: route, storeName: 'clients',
                                record: { id: records, name: 'Concurrent', secret: payload } }), writeSamples);
                        })().catch(error => { sampleError = error; }).finally(() => { observe = undefined; });
                    }, 20);
                    const started = performance.now();
                    let status;
                    let elapsed;
                    try {
                        status = await api.rotateManagedEncryptionKey(route, {
                            targetKeyId: 'target', mode, batchSize: 128, yieldBetweenBatches: true, recoverable: true
                        }, targetKey);
                        elapsed = performance.now() - started;
                    } finally {
                        clearInterval(interval);
                        unsubscribe();
                        await observe;
                    }
                    if (sampleError) throw sampleError;
                    const percentile = values => values.length ? [...values].sort((a, b) => a - b)
                        [Math.min(values.length - 1, Math.floor(values.length * 0.95))] : null;
                    const endUsage = await usage();
                    return { records, mode, elapsed, verificationElapsed: verifyStart === undefined ? null :
                        elapsed - (verifyStart - started), converted: status.converted, verified: status.verified,
                        retirementReady: status.retirementReady, baselineReadP95: percentile(baseline),
                        foregroundReadP95: percentile(readSamples), foregroundWriteP95: percentile(writeSamples),
                        foregroundReads: readSamples.length, foregroundWrites: writeSamples.length,
                        maxTimerLag, peakHeap, startUsage, endUsage };
                }, { config, records, mode });
                assert.equal(result.retirementReady, true);
                assert.equal(result.verified, records);
                console.log('ROTATION_BENCHMARK ' + JSON.stringify(result));
            });
        }
    }

    test('authenticated registration adopts a legacy vault and retires the older protected route', async ({ page }) => {
        const physical = '__magic_protected__:managed-legacy-adoption';
        const before = model();
        const after = model(['name', 'secret']);
        const migration = plan(before, after, [{ op: 'reprotect', store: 'clients' }], { mode: 'resumable', baseline: before });
        const legacy = configuration(physical, 'legacy', before, undefined);
        delete legacy.migrationManifestUrl;
        delete legacy.migrationSchema;
        const managed = configuration(physical, 'managed', after, migration.url);
        const result = await page.evaluate(async ({ legacy, managed, physical }) => {
            const api = await import('/engine/magicDB.js');
            await api.createDb(legacy);
            await api.unlockProtectedDatabase(legacy.name, 'test-key', new Uint8Array(32).fill(42), true);
            await api.addItem({ dbName: legacy.name, storeName: 'clients',
                record: { id: 1, name: 'Now private', secret: 'Already private' } });
            await api.createDb(managed);
            await api.unlockProtectedDatabase(managed.name, 'test-key', new Uint8Array(32).fill(42));
            await api.ensureProtectedMigration(managed.name);
            const row = await api.findItem(managed.name, 'clients', 1);
            const raw = (await readRows(physical))[0];
            let oldRejected = false;
            try { await api.addItem({ dbName: legacy.name, storeName: 'clients', record: { id: 2, name: 'Stale', secret: 'Stale' } }); }
            catch { oldRejected = true; }
            api.releaseProtectedDatabase(managed.name);
            return { row, oldRejected, rawNameType: typeof raw.name, rawSecretType: typeof raw.secret,
                nameCiphertextBytes: raw.name.ciphertext.byteLength, secretCiphertextBytes: raw.secret.ciphertext.byteLength };
        }, { legacy, managed, physical });
        assert.deepEqual(result.row, { id: 1, name: 'Now private', secret: 'Already private' });
        assert.equal(result.oldRejected, true);
        assert.equal(result.rawNameType, 'object');
        assert.equal(result.rawSecretType, 'object');
        assert.ok(result.nameCiphertextBytes > 0 && result.secretCiphertextBytes > 0);
        const saved = await inspect(page, physical);
        assert.equal(saved.state.revision, 2);
        assert.equal(saved.state.protocolVersion, 3);
    });

    test('locking an all-public managed vault fences a public patch started before lock', async ({ page }) => {
        const physical = '__magic_protected__:managed-public-lock';
        const target = model([]);
        const config = configuration(physical, 'route', target, initial(target).url);
        const result = await page.evaluate(async ({ config, physical }) => {
            const api = await import('/engine/magicDB.js');
            await api.createDb(config);
            await api.unlockProtectedDatabase(config.name, 'test-key', new Uint8Array(32).fill(45), true);
            await api.ensureProtectedMigration(config.name);
            await api.addItem({ dbName: config.name, storeName: 'clients',
                record: { id: 1, name: 'Original', secret: 'Public by policy' } });
            const patch = api.patchPublicRecord(config.name, 'clients', 1, { name: 'Late patch' });
            api.lockProtectedDatabase(config.name);
            let outcome;
            try { outcome = { updated: await patch }; }
            catch (error) { outcome = { message: error.message }; }
            return { outcome, rows: await readRows(physical) };
        }, { config, physical });
        assert.match(result.outcome.message ?? '', /MAGIC_ENCRYPTION_LOCKED/, JSON.stringify(result));
        assert.deepEqual(result.rows, [{ id: 1, name: 'Original', secret: 'Public by policy' }]);
    });

    test('locking an active authenticated wrapper migration revokes writes and permits authorized retry', async ({ page }) => {
        const physical = '__magic_protected__:managed-lock';
        const source = model();
        const first = configuration(physical, 'first', source, initial(source).url);
        const next = plan(source, copy(source), [{ op: 'transform', store: 'clients' }], { mode: 'resumable', module: `
            export async function transform(row) {
                if (row.id === 2 && globalThis.pauseProtectedHook) {
                    globalThis.protectedHookWaiting = true;
                    await new Promise(() => {});
                }
                return { ...row, visits: (row.visits ?? 0) + 1 };
            }` });
        const config = configuration(physical, 'next', source, next.url);
        await page.evaluate(async ({ first, config }) => {
            window.protectedApi = await import('/engine/magicDB.js');
            await protectedApi.createDb(first);
            await protectedApi.unlockProtectedDatabase(first.name, 'test-key', new Uint8Array(32).fill(43), true);
            await protectedApi.ensureProtectedMigration(first.name);
            for (const id of [1, 2]) await protectedApi.addItem({ dbName: first.name, storeName: 'clients',
                record: { id, name: 'Public ' + id, secret: 'Private ' + id, visits: 0 } });
            protectedApi.releaseProtectedDatabase(first.name);
            await protectedApi.createDb(config);
            await protectedApi.unlockProtectedDatabase(config.name, 'test-key', new Uint8Array(32).fill(43));
            window.pauseProtectedHook = true;
            window.protectedAttempt = protectedApi.ensureProtectedMigration(config.name)
                .then(() => ({ ok: true }), error => ({ code: error.code }));
        }, { first, config });
        await page.waitForFunction(() => window.protectedHookWaiting === true);
        const result = await page.evaluate(async ({ config, physical }) => {
            protectedApi.lockProtectedDatabase(config.name);
            const stopped = await protectedAttempt;
            const checkpoint = await engine.inspectDatabase(physical);
            const partial = (await readRows(physical)).map(row => ({ visits: row.visits, secretType: typeof row.secret }));
            window.pauseProtectedHook = false;
            await protectedApi.unlockProtectedDatabase(config.name, 'test-key', new Uint8Array(32).fill(43));
            await protectedApi.ensureProtectedMigration(config.name);
            const rows = await Promise.all([1, 2].map(id => protectedApi.findItem(config.name, 'clients', id)));
            protectedApi.releaseProtectedDatabase(config.name);
            return { stopped, checkpoint, partial, rows };
        }, { config, physical });
        assert.ok(['MAGDB_CANCELLED', 'MAGDB_AUTHORIZATION_LOST'].includes(result.stopped.code));
        assert.equal(result.checkpoint.state.status, 'migrating');
        assert.equal(result.checkpoint.state.active.processedRecords, 0);
        assert.deepEqual(result.partial, [{ visits: 0, secretType: 'object' }, { visits: 0, secretType: 'object' }]);
        assert.deepEqual(result.rows, [1, 2].map(id => ({ id, name: 'Public ' + id, secret: 'Private ' + id, visits: 1 })));
        assert.equal((await inspect(page, physical)).state.revision, 2);
    });

    test('explicit physical binding encrypts an existing plain database and later makes every field public', async ({ page }) => {
        const physical = 'existing-plain-records';
        const plain = model([]), protectedModel = model();
        let migration = plan(plain, protectedModel, [{ op: 'reprotect', store: 'clients' }],
            { mode: 'resumable', baseline: plain });
        function binding(route, target) {
            const configured = configuration(physical, route, target, migration.url);
            configured.name = '__magic_protected__:explicit-binding:' + route;
            configured.encryption.explicitPhysicalBinding = true;
            return configured;
        }
        const encrypted = binding('encrypted', protectedModel);
        await page.evaluate(async ({ physical, plain, encrypted }) => {
            await makeLegacy(physical, plain, [{ id: 1, name: 'Public', secret: 'Existing plaintext' }]);
            window.bindingApi = await import('/engine/magicDB.js');
            await bindingApi.createDb(encrypted);
            await bindingApi.unlockProtectedDatabase(encrypted.name, 'test-key', new Uint8Array(32).fill(44), true);
            await bindingApi.ensureProtectedMigration(encrypted.name);
        }, { physical, plain, encrypted });
        const protectedResult = await page.evaluate(async ({ physical, encrypted }) => {
            const raw = (await readRows(physical))[0];
            const logical = await bindingApi.findItem(encrypted.name, 'clients', 1);
            bindingApi.releaseProtectedDatabase(encrypted.name);
            return { logical, rawSecretType: typeof raw.secret, ciphertextBytes: raw.secret.ciphertext.byteLength };
        }, { physical, encrypted });
        assert.equal(protectedResult.logical.secret, 'Existing plaintext');
        assert.equal(protectedResult.rawSecretType, 'object');
        assert.ok(protectedResult.ciphertextBytes > 0);

        migration = appendRevision(migration, plain, [{ op: 'reprotect', store: 'clients', allowProtectionLoss: true }], 'resumable');
        const document = JSON.parse(assets.get(migration.lastMigrationPath).body);
        document.requiredCapabilities = ['authenticatedMigrationsV1'];
        migration.manifest.migrations.at(-1).sha256 = jsonAsset(migration.lastMigrationPath, document).sha256;
        jsonAsset(migration.base + '/manifest.json', migration.manifest);
        const declassified = binding('public', plain);
        const final = await page.evaluate(async ({ physical, declassified }) => {
            await bindingApi.createDb(declassified);
            await bindingApi.unlockProtectedDatabase(declassified.name, 'test-key', new Uint8Array(32).fill(44));
            await bindingApi.ensureProtectedMigration(declassified.name);
            const raw = (await readRows(physical))[0];
            const logical = await bindingApi.findItem(declassified.name, 'clients', 1);
            const snapshot = await engine.inspectDatabase(physical);
            bindingApi.releaseProtectedDatabase(declassified.name);
            return { raw, logical, snapshot };
        }, { physical, declassified });
        assert.deepEqual(final.raw, { id: 1, name: 'Public', secret: 'Existing plaintext' });
        assert.deepEqual(final.logical, final.raw);
        assert.equal(final.snapshot.state.revision, 3);
        assert.equal(final.snapshot.state.protocolVersion, 3);
        assert.equal(final.snapshot.state.status, 'ready');
    });
}
