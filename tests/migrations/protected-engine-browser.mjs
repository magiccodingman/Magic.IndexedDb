import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';

export function registerProtectedEngineTests({ test, schema, bundle, copy, inspect, read, loadPage,
    recoveryPackage, appendRevision, jsonAsset, assets, cliFixture, getOrigin }) {
    const capability = 'authenticatedMigrationsV1';
    const rows = [1, 2, 3].map(id => ({ id, name: `Private ${id}`, visits: 0 }));
    const model = policy => { const result = schema(); result.requiredCapabilities = [capability]; Object.assign(result.stores[0], policy); return result; };
    const planFor = (before, after, operations, options = {}) => bundle(before, after, operations, { ...options,
        mode: options.mode ?? 'resumable', changeMigration: doc => { doc.requiredCapabilities = [capability]; },
        changeManifest: manifest => { manifest.requiredCapabilities = [capability]; } });
    async function authorize(page, keyByte = 73, account = 'protected-test') {
        await page.evaluate(async ({ keyByte, account }) => {
            const { createProtectedMigrationAdapter } = await import('/engine/migrations/protectedMigrationAdapter.js');
            window.authorization = new AbortController();
            window.protectionAdapter = await createProtectedMigrationAdapter({ name: 'test', account, keyId: 'test-key',
                key: new Uint8Array(32).fill(keyByte), signal: authorization.signal });
        }, { keyByte, account });
    }
    async function seed(page, source, values = rows) {
        await authorize(page);
        await page.evaluate(async ({ source, values }) => {
            await engine.ensureDatabase({ name: 'test', schema: source, protection: protectionAdapter, initialize: true });
            const encoded = [];
            for (const row of values) encoded.push(await protectionAdapter.sealRecord(row, source, 'clients', row.id));
            await writeRows('test', encoded);
        }, { source, values });
    }
    const run = (page, plan, extra = {}) => page.evaluate(options => tryEnsure({ ...options, protection: protectionAdapter }),
        { name: 'test', schema: plan.after, manifestUrl: plan.url, batchSize: 1, ...extra });
    const logical = (page, target) => page.evaluate(async target => {
        const result = [];
        for (const row of await readRows('test')) result.push(await protectionAdapter.openRecord(row, target, 'clients', row.id));
        return result;
    }, target);
    const representations = page => page.evaluate(async () => (await readRows('test')).map(row =>
        row.__magic_record?.ciphertext instanceof ArrayBuffer ? 'record' :
            row.name?.ciphertext instanceof ArrayBuffer ? 'field' : 'public'));

    test('managed rotation admits only recoverable distinct keys and fences the older migration path', async ({ page }) => {
        const source = model({ encryptedFields: ['name'] });
        await seed(page, source);
        const result = await page.evaluate(async schema => {
            const { admitRotation, rotationCoordinator, enterRotationVerification } = await import('/engine/utilities/rotationLifecycle.js');
            const { importEncryptionKey } = await import('/engine/utilities/encryptionEnvelope.js');
            const ready = await engine.ensureDatabase({ name: 'test', schema, protection: protectionAdapter });
            const db = await openRaw('test');
            const vault = { database: db.name, account: 'protected-test' };
            const key = byte => importEncryptionKey(new Uint8Array(32).fill(byte));
            const keys = new Map([['test-key', await key(73)], ['target', await key(76)]]);
            const make = patch => ({ db, vault, ready, schema, authorization: protectionAdapter, keys,
                targetKeyId: 'target', mode: 'online', recoverable: true, priorKeyIds: new Set(), ...patch });
            const rejected = async options => {
                try { await admitRotation(options); return false; } catch { return true; }
            };
            const ephemeral = await rejected(make({ recoverable: false }));
            const sameKey = await rejected(make({ keys: new Map(keys).set('target', await key(73)) }));
            const wrongSource = await rejected(make({ keys: new Map(keys).set('test-key', await key(75)) }));
            await engine.assertDatabaseReady(db, ready, protectionAdapter);
            const admitted = await rotationCoordinator(db, undefined, () => admitRotation(make()));
            let oldOpenFenced = false;
            try { await engine.assertDatabaseReady(db, ready, protectionAdapter); }
            catch { oldOpenFenced = true; }
            let prematureVerification = false;
            try {
                await enterRotationVerification({ ...admitted, db, vault, schema, keys,
                    assertCurrent: protectionAdapter.assertCurrent });
            } catch { prematureVerification = true; }
            db.close();
            return { ephemeral, sameKey, wrongSource, oldOpenFenced, prematureVerification,
                writeGeneration: admitted.document.state.writeKeyId };
        }, source);
        assert.deepEqual(result, { ephemeral: true, sameKey: true, wrongSource: true, oldOpenFenced: true,
            prematureVerification: true, writeGeneration: 'target' });
    });

    for (const mode of ['exclusive', 'read-only', 'online']) for (const [layout, policy] of [
        ['field', { encryptedFields: ['name'] }],
        ['whole-record', { exposedFields: ['id', 'visits'] }]
    ]) {
        test(`rotation storage enforces ${mode} access for ${layout} protection and restores a target-only vault`, async ({ page }) => {
            const source = model(policy);
            await seed(page, source);
            const observed = await page.evaluate(async ({ schema, mode, policy }) => {
                const { Dexie } = await import('/engine/dexie/dexie.js');
                const { createProtectedStorage } = await import('/engine/utilities/protectedStorage.js');
                const { admitRotation, enterRotationVerification, finishRotation } = await import('/engine/utilities/rotationLifecycle.js');
                const { advanceRotationPass } = await import('/engine/utilities/rotationPass.js');
                const { importEncryptionKey } = await import('/engine/utilities/encryptionEnvelope.js');
                const { createProtectedMigrationAdapter } = await import('/engine/migrations/protectedMigrationAdapter.js');
                const { portableProtectionPolicy } = await import('/engine/utilities/protectionPolicy.js');
                const ready = await engine.ensureDatabase({ name: 'test', schema, protection: protectionAdapter });
                const dexie = new Dexie('test');
                await dexie.open();
                const db = dexie.backendDB();
                const vault = { database: db.name, account: 'protected-test' };
                const storage = createProtectedStorage(dexie, vault.account, [{ tableName: 'clients',
                    columnNamesInCompoundKey: ['id'], primaryKeyAuto: false, indexes: [], uniqueIndexes: [],
                    columnNamesInCompoundIndex: [], ...policy }], null, { managedSchema: schema });
                const keys = new Map([['test-key', await importEncryptionKey(new Uint8Array(32).fill(73))],
                    ['target', await importEncryptionKey(new Uint8Array(32).fill(76))]]);
                storage.setManagedReadiness(ready);
                await storage.unlock('test-key', keys.get('test-key'), false);
                const table = storage.forTable('clients');
                const common = { db, vault, ready, schema, keys, assertCurrent: protectionAdapter.assertCurrent };
                let active = await admitRotation({ ...common, authorization: protectionAdapter, targetKeyId: 'target',
                    mode, recoverable: true });
                const attach = () => storage.setRotationAccess(active.document, active.fence, keys, common.assertCurrent);
                await attach();
                const attempt = async action => {
                    try { return { allowed: true, value: await action() }; }
                    catch (err) { return { allowed: false, fenced: err.message.startsWith('MAGIC_ROTATION_ACCESS_FENCED:') }; }
                };
                const read = await attempt(() => table.read(1));
                const write = await attempt(() => table.write([{ id: 4, name: 'New generation', visits: 0 }],
                    (store, sealed) => store.put(sealed[0])));
                const policies = portableProtectionPolicy(schema).policies;
                const run = async () => {
                    for (let index = 0; index < 12; index++) {
                        const step = await advanceRotationPass({ ...common, document: active.document, policies, batchSize: 1,
                            expectations: [...active.checks, { store: '__magic_encryption', key: 'policy', value: active.fence }] });
                        if (step.outcome === 'pass-complete') return;
                        if (step.outcome !== 'advanced') throw Error('The rotation step did not advance.');
                        active.document = step.document;
                    }
                    throw Error('The bounded pass did not complete.');
                };
                await run();
                active = await enterRotationVerification({ ...common, ...active });
                await attach();
                const verificationWrite = await attempt(() => table.patch(1, { visits: 10 }));
                await run();
                const finalized = await finishRotation({ ...common, ...active });
                dexie.close();
                const target = await createProtectedMigrationAdapter({ name: 'test', account: vault.account, keyId: 'target', key: keys.get('target') });
                const reopened = await engine.ensureDatabase({ name: 'test', schema, protection: target });
                const raw = await readRows('test');
                const logical = [];
                for (const row of raw) logical.push(await target.openRecord(row, schema, 'clients', row.id));
                return { read, write, verificationWrite, lineage: target.keyLineage.generations,
                    retirement: finalized.lineage.completed, revision: reopened.revision, logical,
                    publicRows: raw.map(row => ({ id: row.id, visits: row.visits })),
                    deployed: raw.map(row => (row.__magic_record ?? row.name).keyId) };
            }, { schema: source, mode, policy });
            assert.equal(observed.read.allowed, mode !== 'exclusive');
            if (!observed.read.allowed) assert.equal(observed.read.fenced, true);
            assert.equal(observed.write.allowed, mode === 'online');
            if (!observed.write.allowed) assert.equal(observed.write.fenced, true);
            assert.deepEqual(observed.verificationWrite, { allowed: false, fenced: true });
            assert.deepEqual(observed.lineage, ['test-key', 'target']);
            assert.equal(observed.retirement.converted, 3);
            assert.equal(observed.retirement.verified, mode === 'online' ? 4 : 3);
            assert.equal(observed.revision, 1);
            assert.deepEqual(observed.logical, mode === 'online' ? [...rows, { id: 4, name: 'New generation', visits: 0 }] : rows);
            assert.deepEqual(observed.publicRows, observed.logical.map(({ id, visits }) => ({ id, visits })));
            assert.ok(observed.deployed.every(generation => generation === 'target'));
        });
    }

    for (const [label, before, after, loss] of [
        ['plaintext to field encryption', {}, { encryptedFields: ['name'] }, false],
        ['field encryption to plaintext', { encryptedFields: ['name'] }, {}, true],
        ['field to whole-record encryption', { encryptedFields: ['name'] }, { exposedFields: ['id'] }, false],
        ['whole-record to field encryption', { exposedFields: ['id'] }, { encryptedFields: ['name'] }, true],
        ['whole-record public allowlist grows', { exposedFields: ['id'] }, { exposedFields: ['id', 'name'] }, true],
        ['whole-record public allowlist shrinks', { exposedFields: ['id', 'name'] }, { exposedFields: ['id'] }, false]
    ]) test(`authenticated migration: ${label}`, async ({ page }) => {
        const source = model(before), target = model(after);
        await seed(page, source);
        const plan = planFor(source, target, [{ op: 'reprotect', store: 'clients', ...(loss ? { allowProtectionLoss: true } : {}) }]);
        const result = await run(page, plan);
        assert.equal(result.ok, true, JSON.stringify(result));
        assert.deepEqual(await logical(page, target), rows);
        const stored = await read(page);
        const namePublic = after.exposedFields ? after.exposedFields.includes('name') : !after.encryptedFields?.includes('name');
        assert.equal(JSON.stringify(stored).includes('Private 1'), namePublic);
        if (after.exposedFields) assert.ok(stored.every(row => !Object.hasOwn(row, 'visits') && Object.hasOwn(row, '__magic_record')));
        const ready = await inspect(page);
        assert.equal(ready.state.protocolVersion, 3);
        assert.equal(ready.state.status, 'ready');
        assert.equal(ready.state.revision, 2);
        assert.equal((await run(page, plan)).ok, true);
    });

    test('authenticated downgrade requires an explicit decision before any journal or record changes', async ({ page }) => {
        const source = model({ encryptedFields: ['name'] }), target = model({});
        await seed(page, source);
        const before = await inspect(page), stored = await read(page);
        const plan = planFor(source, target, [{ op: 'reprotect', store: 'clients' }]);
        assert.equal((await run(page, plan)).error?.code, 'MAGDB_PROTECTION_LOSS_REQUIRES_APPROVAL');
        assert.deepEqual(await inspect(page), before);
        assert.deepEqual(await read(page), stored);
    });

    for (const [label, keyByte, account] of [['wrong key', 74, 'protected-test'], ['wrong account', 73, 'another-account']])
        test(`authenticated migration rejects ${label} without mutation`, async ({ page }) => {
            const source = model({ encryptedFields: ['name'] }); await seed(page, source);
            const before = await inspect(page), stored = await read(page);
            const plan = planFor(source, copy(source), [{ op: 'setMissing', store: 'clients', path: 'changed', value: true }]);
            await authorize(page, keyByte, account);
            assert.equal((await run(page, plan)).error?.code, 'MAGDB_AUTHORIZATION_REJECTED');
            assert.deepEqual(await inspect(page), before);
            assert.deepEqual(await read(page), stored);
        });

    test('a v3 journal rejects a restored legacy policy even when the policy shape matches', async ({ page }) => {
        const source = model({ encryptedFields: ['name'] });
        await seed(page, source);
        const plan = planFor(source, copy(source), [{ op: 'setMissing', store: 'clients', path: 'changed', value: true }]);
        await page.evaluate(async () => {
            const { importEncryptionKey, encryptValue } = await import('/engine/utilities/encryptionEnvelope.js');
            const { createProtectionPolicy } = await import('/engine/utilities/protectionPolicy.js');
            const policy = createProtectionPolicy([{ tableName: 'clients', columnNamesInCompoundKey: ['id'],
                primaryKeyAuto: false, indexes: [], uniqueIndexes: [], columnNamesInCompoundIndex: [], encryptedFields: ['name'] }]);
            const key = await importEncryptionKey(new Uint8Array(32).fill(73));
            const envelope = await encryptValue(JSON.stringify(['Magic.IndexedDb/policy', 1, policy.policyFingerprint]),
                key, 'test-key', { database: 'test', account: 'protected-test', store: '__magic_encryption', primaryKey: 'policy', member: null });
            const db = await openRaw('test');
            try {
                const tx = db.transaction('__magic_encryption', 'readwrite'), done = complete(tx);
                tx.objectStore('__magic_encryption').put(envelope, 'policy');
                await done;
            } finally { db.close(); }
        });
        const before = await inspect(page), stored = await read(page);
        const result = await run(page, plan);
        assert.equal(result.error?.code, 'MAGDB_PROTECTION_STATE_INVALID', JSON.stringify(result));
        assert.deepEqual(await inspect(page), before);
        assert.deepEqual(await read(page), stored);
    });

    test('an authenticated structural migration rejects numeric-policy damage during preflight', async ({ page }) => {
        const source = model({ encryptedFields: ['name'] });
        source.numericFormat = 'lossless-v1';
        await seed(page, source);
        const target = copy(source);
        const added = { name: 'byId', keyPath: 'id', unique: false, multiEntry: false };
        target.stores[0].indexes.push(added);
        const plan = planFor(source, target, [{ op: 'createIndex', store: 'clients', index: added }], { mode: 'atomic' });
        const before = await inspect(page), stored = await read(page);
        await page.route(plan.url.replace('manifest.json', plan.manifest.migrations[0].path), async route => {
            await page.evaluate(async () => {
                const db = await openRaw('test');
                try {
                    const tx = db.transaction('__magic_numeric_policy', 'readwrite'), done = complete(tx);
                    tx.objectStore('__magic_numeric_policy').delete('policy');
                    await done;
                } finally { db.close(); }
            });
            await route.continue();
        });
        const result = await run(page, plan);
        assert.equal(result.error?.code, 'MAGDB_CODEC_POLICY_MISMATCH', JSON.stringify(result));
        assert.deepEqual(await inspect(page), { ...before, numericPolicy: null });
        assert.deepEqual(await read(page), stored);
    });

    test('authenticated preflight rejects a replaced journal for a different committed edge', async ({ page }) => {
        const source = model({ encryptedFields: ['name'] });
        await seed(page, source);
        await page.evaluate(async () => {
            const db = await openRaw('test');
            try {
                const tx = db.transaction(['__magic_migrations__', '__magic_encryption']);
                window.priorAuthenticatedEdge = await Promise.all([
                    request(tx.objectStore('__magic_migrations__').get('state')),
                    request(tx.objectStore('__magic_encryption').get('migration-state'))]);
            } finally { db.close(); }
        });
        let plan = planFor(source, copy(source), [{ op: 'setMissing', store: 'clients', path: 'afterFirst', value: true }]);
        assert.equal((await run(page, plan)).value?.revision, 2);
        plan = appendRevision(plan, source, [{ op: 'setMissing', store: 'clients', path: 'afterSecond', value: true }], 'resumable');
        const later = JSON.parse(assets.get(plan.lastMigrationPath).body);
        later.requiredCapabilities = [capability];
        plan.manifest.migrations.at(-1).sha256 = jsonAsset(plan.lastMigrationPath, later).sha256;
        jsonAsset(plan.base + '/manifest.json', plan.manifest);
        const before = await inspect(page), stored = await read(page);
        await page.route(getOrigin() + plan.lastMigrationPath, async route => {
            await page.evaluate(async () => {
                const db = await openRaw('test');
                try {
                    const tx = db.transaction(['__magic_migrations__', '__magic_encryption'], 'readwrite'), done = complete(tx);
                    tx.objectStore('__magic_migrations__').put(priorAuthenticatedEdge[0], 'state');
                    tx.objectStore('__magic_encryption').put(priorAuthenticatedEdge[1], 'migration-state');
                    await done;
                } finally { db.close(); }
            });
            await route.continue();
        });
        assert.equal((await run(page, plan)).error?.code, 'MAGDB_CONCURRENT_CHANGE');
        const after = await inspect(page);
        assert.equal(after.nativeVersion, before.nativeVersion);
        assert.equal(after.state.revision, 1);
        assert.deepEqual(await read(page), stored);
    });

    test('authenticated preflight rejects replacement with an older checkpoint of the same edge', async ({ page }) => {
        const source = model({ encryptedFields: ['name'] });
        await seed(page, source);
        const plan = planFor(source, source, [{ op: 'transform', store: 'clients' }], {
            module: 'export function transform(row) { return { ...row, visits: row.visits + 1 }; }' });
        for (const count of [1, 2]) {
            const result = await page.evaluate(async ({ source, url, count }) => {
                const controller = new AbortController();
                try {
                    await engine.ensureDatabase({ name: 'test', schema: source, manifestUrl: url, protection: protectionAdapter,
                        batchSize: 1, signal: controller.signal, onEvent: event => {
                            if (event.phase === 'checkpoint' && event.processedRecords === count) controller.abort();
                        } });
                } catch (error) { return error.code; }
                return null;
            }, { source, url: plan.url, count });
            assert.equal(result, 'MAGDB_CANCELLED');
            if (count === 1) await page.evaluate(async () => {
                const db = await openRaw('test');
                try {
                    const tx = db.transaction(['__magic_migrations__', '__magic_encryption']);
                    window.earlierProgress = await Promise.all([
                        request(tx.objectStore('__magic_migrations__').get('state')),
                        request(tx.objectStore('__magic_encryption').get('migration-state'))]);
                } finally { db.close(); }
            });
        }
        assert.equal((await inspect(page)).state.active.processedRecords, 2);
        const stored = await read(page);
        await page.route(plan.url.replace('manifest.json', plan.manifest.migrations[0].path), async route => {
            await page.evaluate(async () => {
                const db = await openRaw('test');
                try {
                    const tx = db.transaction(['__magic_migrations__', '__magic_encryption'], 'readwrite'), done = complete(tx);
                    tx.objectStore('__magic_migrations__').put(earlierProgress[0], 'state');
                    tx.objectStore('__magic_encryption').put(earlierProgress[1], 'migration-state');
                    await done;
                } finally { db.close(); }
            });
            await route.continue();
        });
        assert.equal((await run(page, plan)).error?.code, 'MAGDB_CONCURRENT_CHANGE');
        assert.deepEqual(await read(page), stored);
    });

    test('authenticated migration checkpoints retain ciphertext and reject journal tampering', async ({ page }) => {
        const source = model({ encryptedFields: ['name'] }); await seed(page, source);
        const plan = planFor(source, copy(source), [{ op: 'transform', store: 'clients' }], {
            module: 'export function transform(row) { return { ...row, visits: row.visits + 1 }; }' });
        const cancelled = await page.evaluate(async options => {
            const controller = new AbortController();
            try { await engine.ensureDatabase({ ...options, protection: protectionAdapter, signal: controller.signal,
                onEvent: event => { if (event.phase === 'checkpoint' && event.processedRecords === 1) controller.abort(); } }); }
            catch (error) { return error.code; }
        }, { name: 'test', schema: source, manifestUrl: plan.url, batchSize: 1 });
        assert.equal(cancelled, 'MAGDB_CANCELLED');
        assert.equal((await inspect(page)).state.active.processedRecords, 1);
        assert.ok(!(JSON.stringify(await read(page))).includes('Private'));
        const before = await read(page);
        await page.evaluate(async () => {
            const db = await openRaw('test'); const tx = db.transaction('__magic_migrations__', 'readwrite'); const done = complete(tx);
            const table = tx.objectStore('__magic_migrations__'); const get = table.get('state');
            get.onsuccess = () => { const state = get.result; state.active.processedRecords = 2; table.put(state, 'state'); };
            await done; db.close();
        });
        assert.equal((await run(page, plan)).error?.code, 'MAGDB_PROTECTION_STATE_INVALID');
        assert.deepEqual(await read(page), before);
    });

    test('authenticated metadata cannot be bypassed by deleting its policy and requesting plaintext adoption', async ({ page }) => {
        const source = model({}); await seed(page, source);
        await page.evaluate(async () => {
            const db = await openRaw('test'); const tx = db.transaction(['__magic_migrations__', '__magic_encryption'], 'readwrite'); const done = complete(tx);
            const table = tx.objectStore('__magic_migrations__'); const get = table.get('state');
            get.onsuccess = () => { const state = get.result; state.protocolVersion = 1; table.put(state, 'state'); };
            tx.objectStore('__magic_encryption').clear(); await done; db.close();
        });
        const before = await read(page);
        const plain = schema();
        const result = await page.evaluate(schema => tryEnsure({ name: 'test', schema }), plain);
        assert.equal(result.ok, false);
        const retry = await page.evaluate(schema => tryEnsure({ name: 'test', schema, protection: protectionAdapter, initialize: true }), source);
        assert.equal(retry.error?.code, 'MAGDB_PROTECTION_STATE_INVALID');
        assert.deepEqual(await read(page), before);
    });

    for (const removeJournal of [false, true]) for (const removeProof of [false, true])
        test(`authenticated identity rejects ${removeJournal ? 'deleted' : 'downgraded'} journal with ${removeProof ? 'deleted' : 'retained'} proof`, async ({ page }) => {
            const source = model({ encryptedFields: ['name'] });
            await seed(page, source);
            const plan = planFor(source, copy(source), [{ op: 'setMissing', store: 'clients', path: 'replayed', value: true }],
                { baseline: source });
            await page.evaluate(async ({ removeJournal, removeProof }) => {
                const db = await openRaw('test');
                try {
                    const tx = db.transaction(['__magic_migrations__', '__magic_encryption'], 'readwrite');
                    const done = complete(tx), journal = tx.objectStore('__magic_migrations__');
                    if (removeJournal) journal.delete('state');
                    else {
                        const get = journal.get('state');
                        get.onsuccess = () => journal.put({ ...get.result, protocolVersion: 1 }, 'state');
                    }
                    if (removeProof) tx.objectStore('__magic_encryption').delete('migration-state');
                    await done;
                } finally { db.close(); }
            }, { removeJournal, removeProof });
            const before = await inspect(page), stored = await read(page);
            assert.equal((await run(page, plan, { initialize: true })).error?.code, 'MAGDB_PROTECTION_STATE_INVALID');
            assert.deepEqual(await inspect(page), before);
            assert.deepEqual(await read(page), stored);
        });

    test('abrupt authenticated migration termination resumes encrypted batches exactly once', async ({ page, context }) => {
        const source = model({ encryptedFields: ['name'] }); await seed(page, source);
        const plan = planFor(source, copy(source), [{ op: 'transform', store: 'clients' }], { module: `
            export async function transform(row) {
                if (row.id === 2 && globalThis.holdAuthenticated) { globalThis.authHookPending = true; await new Promise(() => {}); }
                return { ...row, visits: row.visits + 1 };
            }` });
        await page.evaluate(options => { window.holdAuthenticated = true; window.pendingAuthenticated = tryEnsure({ ...options, protection: protectionAdapter }); },
            { name: 'test', schema: source, manifestUrl: plan.url, batchSize: 1 });
        await page.waitForFunction(() => window.authHookPending === true);
        const reader = await loadPage(context);
        assert.equal((await inspect(reader)).state.active.processedRecords, 1);
        assert.ok(!JSON.stringify(await read(reader)).includes('Private'));
        await page.close();
        await authorize(reader);
        assert.equal((await run(reader, plan)).ok, true);
        assert.deepEqual(await logical(reader, source), rows.map(row => ({ ...row, visits: 1 })));
        assert.equal((await inspect(reader)).state.status, 'ready');
    });

    test('authenticated static deployment checks CSP and cached historical assets before writing', async ({ page, context }) => {
        const source = model({ encryptedFields: ['name'] });
        await seed(page, source);
        const plan = planFor(source, copy(source), [{ op: 'transform', store: 'clients' }], {
            module: 'export function transform(row) { return { ...row, visits: row.visits + 1 }; }' });
        const before = await inspect(page), stored = await read(page);
        let allowBlob = false;
        await context.route(getOrigin() + '/', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html>',
            headers: { 'Content-Security-Policy': "default-src 'none'; connect-src 'self'; worker-src 'self'; script-src 'self'" +
                (allowBlob ? ' blob:' : '') } }));
        const strict = await loadPage(context);
        await authorize(strict);
        const blocked = await run(strict, plan);
        assert.equal(blocked.error?.code, 'MAGDB_MIGRATION_FAILED');
        assert.deepEqual(await inspect(strict), before);
        assert.deepEqual(await read(strict), stored);

        allowBlob = true;
        const cached = await loadPage(context);
        await authorize(cached);
        const workerPath = '/protected-offline-fixture.js';
        assets.set(workerPath, { type: 'text/javascript', body: `
            self.addEventListener('install', () => self.skipWaiting());
            self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
            self.addEventListener('fetch', event => {
                if (new URL(event.request.url).pathname.startsWith('/fixtures/'))
                    event.respondWith(caches.match(event.request).then(hit => hit ?? new Response(null, { status: 504 })));
            });` });
        const hookPath = plan.base + '/migrations/transform.js';
        await cached.evaluate(async ({ workerPath, paths }) => {
            const cache = await caches.open('authenticated-assets');
            await cache.addAll(paths);
            await navigator.serviceWorker.register(workerPath);
            if (!navigator.serviceWorker.controller) await new Promise(resolve =>
                navigator.serviceWorker.addEventListener('controllerchange', resolve, { once: true }));
        }, { workerPath, paths: [new URL(plan.url).pathname, ...plan.manifest.schemas.map(asset => plan.base + '/' + asset.path),
            plan.base + '/' + plan.manifest.migrations[0].path] });
        assert.equal((await run(cached, plan)).error?.code, 'MAGDB_ASSET_UNAVAILABLE');
        assert.deepEqual(await inspect(cached), before);
        assert.deepEqual(await read(cached), stored);
        await cached.evaluate(({ hookPath, body }) => caches.open('authenticated-assets').then(cache =>
            cache.put(hookPath, new Response(body))), { hookPath, body: assets.get(hookPath).body });
        assert.equal((await run(cached, plan)).value?.revision, 2);
        assert.deepEqual(await logical(cached, source), rows.map(row => ({ ...row, visits: 1 })));
        assert.deepEqual(await representations(cached), ['field', 'field', 'field']);
    });

    test('two tabs and a worker converge on one authenticated migration of 1024 records', async ({ page, context }) => {
        const source = model({ encryptedFields: ['name'] });
        const records = Array.from({ length: 1024 }, (_, index) => ({ id: index + 1, name: 'Private ' + index, visits: 0 }));
        await seed(page, source, records);
        const plan = planFor(source, copy(source), [{ op: 'transform', store: 'clients' }], {
            module: 'export function transform(row) { return { ...row, visits: row.visits + 1 }; }' });
        const tab = await loadPage(context);
        await authorize(tab);
        const workerPath = plan.base + '/protected-worker.mjs';
        assets.set(workerPath, { type: 'text/javascript', body: `
            import { ensureDatabase } from '/engine/migrations/migrationEngine.js';
            import { createProtectedMigrationAdapter } from '/engine/migrations/protectedMigrationAdapter.js';
            self.onmessage = async ({ data }) => {
                try {
                    const protection = await createProtectedMigrationAdapter({ name: 'test', account: 'protected-test',
                        keyId: 'test-key', key: new Uint8Array(32).fill(73) });
                    const result = await ensureDatabase({ ...data, protection });
                    self.postMessage({ revision: result.revision });
                } catch (error) { self.postMessage({ code: error.code }); }
            };` });
        const worker = page.evaluate(({ workerPath, options }) => new Promise((resolve, reject) => {
            const worker = new Worker(workerPath, { type: 'module' });
            worker.onmessage = event => { worker.terminate(); resolve(event.data); };
            worker.onerror = error => { worker.terminate(); reject(new Error(error.message)); };
            worker.postMessage(options);
        }), { workerPath, options: { name: 'test', schema: source, manifestUrl: plan.url, batchSize: 32 } });
        const [one, two, three] = await Promise.all([run(page, plan, { batchSize: 32 }), run(tab, plan, { batchSize: 32 }), worker]);
        assert.deepEqual([one.value?.revision, two.value?.revision, three.revision], [2, 2, 2], JSON.stringify([one, two, three]));
        assert.deepEqual(await logical(tab, source), records.map(row => ({ ...row, visits: 1 })));
        assert.ok((await representations(tab)).every(kind => kind === 'field'));
        assert.equal((await inspect(tab)).state.history.length, 1);
    });

    test('authenticated recovery resumes both checkpoints across policy partitions and advances a later revision', async ({ page, context }) => {
        const source = model({ encryptedFields: ['name'] }), target = model({ exposedFields: ['id'] });
        await seed(page, source, rows.map(row => ({ ...row, value: 1 })));
        let plan = planFor(source, target, [{ op: 'transform', store: 'clients' }], { module: `
            export function transform(row) { if (row.id === 2) throw new Error(row.name); return { ...row, value: 11 }; }` });
        const approved = recoveryPackage(plan);
        const interrupted = await run(page, plan);
        assert.equal(interrupted.error?.code, 'MAGDB_HOOK_FAILED');
        assert.ok(!JSON.stringify(interrupted).includes('Private'));
        assert.equal((await inspect(page)).state.active.processedRecords, 1);
        assert.deepEqual(await representations(page), ['record', 'field', 'field']);

        plan = appendRevision(plan, target, [{ op: 'setMissing', store: 'clients', path: 'normalized', value: true }], 'resumable');
        const later = JSON.parse(assets.get(plan.lastMigrationPath).body);
        later.requiredCapabilities = [capability];
        plan.manifest.migrations.at(-1).sha256 = jsonAsset(plan.lastMigrationPath, later).sha256;
        jsonAsset(plan.base + '/manifest.json', plan.manifest);
        const options = { name: 'test', schema: target, manifestUrl: plan.url, batchSize: 1, recovery: approved };
        const pauseAt = (tab, phase) => tab.evaluate(async ({ options, phase }) => {
            const observable = [];
            try {
                await engine.recoverDatabase({ ...options, protection: protectionAdapter, onEvent: event => {
                    observable.push(event);
                    if (event.phase === 'recoveryCheckpoint' && (phase === 'repair'
                        ? event.processedRecords === 1 : event.verifiedRecords === 1)) authorization.abort();
                } });
            } catch (error) { return { code: error.code, observable }; }
        }, { options, phase });

        const repairPause = await pauseAt(page, 'repair');
        assert.equal(repairPause.code, 'MAGDB_AUTHORIZATION_LOST');
        assert.equal((await inspect(page)).state.recovery.repaired, 1);
        const checkpoint = await inspect(page);
        assert.deepEqual(await representations(page), ['record', 'field', 'field']);
        await page.close();
        const reopened = await loadPage(context);
        await authorize(reopened);
        assert.deepEqual(await inspect(reopened), checkpoint);
        assert.equal((await run(reopened, plan)).error?.code, 'MAGDB_RECOVERY_REQUIRED');

        const verifyPause = await pauseAt(reopened, 'verify');
        assert.equal(verifyPause.code, 'MAGDB_AUTHORIZATION_LOST');
        await authorize(reopened);
        assert.equal((await inspect(reopened)).state.recovery.verified, 1);
        assert.equal((await run(reopened, plan)).error?.code, 'MAGDB_RECOVERY_REQUIRED');
        assert.ok(!(JSON.stringify([repairPause, verifyPause, await inspect(reopened)])).includes('Private'));
        const result = await reopened.evaluate(options => tryEnsure({ ...options, protection: protectionAdapter }, true), options);
        assert.equal(result.value?.outcome, 'recovered', JSON.stringify(result));
        assert.equal(result.value.applicationTargetRevision, 3);
        const expected = rows.map(row => ({ ...row, value: 10, repairs: 1 }));
        assert.deepEqual(await logical(reopened, target), expected);
        assert.ok(!JSON.stringify(await read(reopened)).includes('Private'));
        const completed = (await inspect(reopened)).state;
        assert.equal(completed.protocolVersion, 3);
        assert.equal(completed.history[0].outcome, 'recovered');
        assert.ok(await reopened.evaluate(async id => {
            const db = await openRaw('test');
            try {
                const audit = await request(db.transaction('__magic_migrations__').objectStore('__magic_migrations__').get('recovery:' + id));
                return audit?.ciphertext instanceof ArrayBuffer && audit.ciphertext.byteLength > 0 &&
                    !Object.hasOwn(audit, 'original') && !Object.hasOwn(audit, 'recovery');
            } finally { db.close(); }
        }, approved.id));
        assert.equal((await run(reopened, plan)).value?.revision, 3);
        const advanced = (await inspect(reopened)).state;
        assert.equal(advanced.protocolVersion, 3);
        assert.deepEqual(advanced.history[0], completed.history[0]);
        assert.deepEqual(await logical(reopened, target), expected.map(row => ({ ...row, normalized: true })));
    });

    test('rotation resumes retained authenticated recovery evidence and verifies it with only the target key', async ({ page, context }) => {
        const source = model({ encryptedFields: ['name'] });
        await seed(page, source, rows.map(row => ({ ...row, value: 1 })));
        const plan = planFor(source, source, [{ op: 'transform', store: 'clients' }], { module: `
            export function transform(row) { if (row.id === 2) throw new Error(row.name); return { ...row, value: 11 }; }` });
        const recovery = recoveryPackage(plan);
        const failed = await run(page, plan);
        assert.equal(failed.error?.code, 'MAGDB_HOOK_FAILED');
        assert.equal((await inspect(page)).state.active.processedRecords, 1);
        const options = { name: 'test', schema: source, manifestUrl: plan.url, batchSize: 1 };
        const recovered = await page.evaluate(async ({ options, recovery }) =>
            tryEnsure({ ...options, recovery, protection: protectionAdapter }, true), { options, recovery });
        assert.equal(recovered.value?.outcome, 'recovered', JSON.stringify(recovered));

        const partial = await page.evaluate(async options => {
            const api = await import('/engine/magicDB.js');
            const { importEncryptionKey, encryptValue } = await import('/engine/utilities/encryptionEnvelope.js');
            const ready = await engine.ensureDatabase({ ...options, protection: protectionAdapter });
            const entry = ready.history[0];
            const old = new Uint8Array(32).fill(73), target = new Uint8Array(32).fill(76);
            const key = 'recovery:' + entry.recovery.id;
            const db = await openRaw('test');
            const store = mode => db.transaction('__magic_migrations__', mode).objectStore('__magic_migrations__');
            const evidence = await request(store('readonly').get(key));
            const plaintext = await protectionAdapter.verifyRecoveryEvidence(evidence, entry);
            // Recreate the exact v3.0 envelope binding for the retained evidence
            // produced by the real recovery above.
            const legacyEvidence = await encryptValue(plaintext, await importEncryptionKey(old), 'test-key',
                { database: 'test', account: 'protected-test', store: '__magic_encryption', primaryKey: 'migration-state', member: null });
            await request(store('readwrite').put(legacyEvidence, key));
            db.close();
            const config = { name: '__magic_protected__:recovery-rotation', migrationSchema: options.schema,
                migrationManifestUrl: options.manifestUrl,
                encryption: { physicalName: 'test', account: 'protected-test', explicitPhysicalBinding: true },
                storeSchemas: [{ tableName: 'clients', columnNamesInCompoundKey: ['id'], primaryKeyAuto: false,
                    indexes: [], uniqueIndexes: [], columnNamesInCompoundIndex: [], encryptedFields: ['name'] }] };
            await api.createDb(config);
            await api.unlockProtectedDatabase(config.name, 'test-key', old);
            await api.ensureProtectedMigration(config.name);
            const { rotationJournalKey } = await import('/engine/utilities/rotationJournal.js');
            // Prevent the next crypto preparation from completing after the
            // metadata batch commits. The native checkpoint is the barrier.
            const decrypt = crypto.subtle.decrypt.bind(crypto.subtle);
            crypto.subtle.decrypt = async (...args) => {
                const opened = await openRaw('test');
                let progress;
                try { progress = await request(opened.transaction('__magic_encryption').objectStore('__magic_encryption').get(rotationJournalKey)); }
                finally { opened.close(); }
                if (progress?.state.afterKey === key) {
                    globalThis.evidenceCheckpointReached = true;
                    await new Promise(() => {});
                }
                return decrypt(...args);
            };
            void api.rotateManagedEncryptionKey(config.name, { targetKeyId: 'next', mode: 'online',
                batchSize: 1, recoverable: true }, target).catch(error => { globalThis.rotationFailure = error.message; });
            return { config, plaintext, key };
        }, options);
        await page.waitForFunction(() => globalThis.evidenceCheckpointReached || globalThis.rotationFailure);
        assert.equal(await page.evaluate(() => globalThis.rotationFailure ?? null), null);
        await page.close();

        const resumed = await loadPage(context);
        const result = await resumed.evaluate(async ({ config, key, plaintext }) => {
            const api = await import('/engine/magicDB.js');
            const { createProtectedMigrationAdapter } = await import('/engine/migrations/protectedMigrationAdapter.js');
            const { rotationJournalKey } = await import('/engine/utilities/rotationJournal.js');
            const source = new Uint8Array(32).fill(73), target = new Uint8Array(32).fill(76);
            const readMetadata = async (name, id) => {
                const db = await openRaw('test');
                try { return await request(db.transaction(name).objectStore(name).get(id)); }
                finally { db.close(); }
            };
            const progress = await readMetadata('__magic_encryption', rotationJournalKey);
            const converted = await readMetadata('__magic_migrations__', key);
            await api.createDb(config);
            const partialStatus = await api.authorizeManagedKeyRotation(config.name, 'test-key', source, 'next', target);
            const changeEvidence = async (action, ...args) => {
                const db = await openRaw('test');
                try { await request(db.transaction('__magic_migrations__', 'readwrite').objectStore('__magic_migrations__')[action](...args)); }
                finally { db.close(); }
            };
            await changeEvidence('delete', key);
            let missingEvidenceRejected = false;
            try { await api.resumeManagedEncryptionKeyRotation(config.name, { batchSize: 1 }); }
            catch (failure) { missingEvidenceRejected = failure.code === 'MAGDB_PROTECTION_STATE_INVALID'; }
            const blocked = await api.getManagedKeyRotationStatus(config.name);
            await changeEvidence('put', converted, key);
            const completed = await api.resumeManagedEncryptionKeyRotation(config.name, { batchSize: 1 });
            api.releaseProtectedDatabase(config.name);
            await api.createDb(config);
            await api.unlockProtectedDatabase(config.name, 'next', target);
            const ready = await api.ensureProtectedMigration(config.name);
            const entry = ready.history[0];
            const evidence = await readMetadata('__magic_migrations__', key);
            const adapter = keyId => createProtectedMigrationAdapter({ name: 'test', account: config.encryption.account,
                keyId, key: keyId === 'next' ? target : source });
            const intact = await (await adapter('next')).verifyRecoveryEvidence(evidence, entry) === plaintext;
            let oldKeyRejected = false;
            try { await (await adapter('test-key')).openRetainedRecoveryEvidence(evidence, entry); }
            catch { oldKeyRejected = true; }
            return { progress: progress.state.afterKey, convertedKey: converted.keyId, partialStatus, blocked, missingEvidenceRejected, completed, intact,
                oldKeyRejected, history: ready.history, ciphertextUnchanged: [...new Uint8Array(converted.ciphertext)].join() ===
                    [...new Uint8Array(evidence.ciphertext)].join(), rows: await api.toArray(config.name, 'clients') };
        }, partial);
        assert.equal(result.progress, partial.key);
        assert.equal(result.convertedKey, 'next');
        assert.equal(result.partialStatus.retirementReady, false);
        assert.equal(result.missingEvidenceRejected, true);
        assert.equal(result.blocked.phase, 'verifying');
        assert.equal(result.blocked.retirementReady, false);
        assert.equal(result.completed.retirementReady, true);
        assert.equal(result.intact, true);
        assert.equal(result.oldKeyRejected, true);
        assert.equal(result.ciphertextUnchanged, true);
        assert.equal(result.history[0].recovery.id, recovery.id);
        assert.deepEqual(result.rows, rows.map(row => ({ ...row, value: 10, repairs: 1 })));
    });

    test('authenticated enrollment seals evidence retained by a prior plaintext managed recovery', async ({ page }) => {
        const source = schema();
        const name = 'test';
        await page.evaluate(async ({ source, rows }) => {
            await engine.ensureDatabase({ name: 'test', schema: source });
            await writeRows('test', rows);
        }, { source, rows: rows.map(row => ({ ...row, value: 1 })) });
        const plan = bundle(source, source, [{ op: 'transform', store: 'clients' }], { mode: 'resumable', module: `
            export function transform(row) { if (row.id === 2) throw new Error('Fixture failure'); return { ...row, value: 11 }; }` });
        const recovery = recoveryPackage(plan);
        const options = { name, schema: source, manifestUrl: plan.url, batchSize: 1 };
        const failed = await page.evaluate(options => tryEnsure(options), options);
        assert.equal(failed.error?.code, 'MAGDB_HOOK_FAILED');
        const recovered = await page.evaluate(options => tryEnsure(options, true), { ...options, recovery });
        assert.equal(recovered.value?.outcome, 'recovered', JSON.stringify(recovered));
        assert.equal((await inspect(page)).state.protocolVersion, 2);
        const plaintextAudit = await page.evaluate(async id => {
            const db = await openRaw('test');
            try { return await request(db.transaction('__magic_migrations__').objectStore('__magic_migrations__').get('recovery:' + id)); }
            finally { db.close(); }
        }, recovery.id);
        plan.manifest.requiredCapabilities.push(capability);
        jsonAsset(plan.base + '/manifest.json', plan.manifest);

        await authorize(page);
        const enrolled = await page.evaluate(async ({ options, plaintextAudit }) => {
            const ready = await engine.ensureDatabase({ ...options, initialize: true, protection: protectionAdapter });
            const entry = ready.history[0];
            const db = await openRaw('test');
            try {
                const key = 'recovery:' + entry.recovery.id;
                const meta = mode => db.transaction('__magic_migrations__', mode).objectStore('__magic_migrations__');
                const audit = await request(meta('readonly').get(key));
                const plaintext = await protectionAdapter.verifyRecoveryEvidence(audit, entry);
                // Older v3 enrollment could have left the v2 audit unsealed.
                await request(meta('readwrite').put(plaintextAudit, key));
                const { admitRotation } = await import('/engine/utilities/rotationLifecycle.js');
                const { importEncryptionKey } = await import('/engine/utilities/encryptionEnvelope.js');
                const from = await importEncryptionKey(new Uint8Array(32).fill(73));
                const to = await importEncryptionKey(new Uint8Array(32).fill(76));
                let rejectedBeforeAdmission = false;
                try {
                    await admitRotation({ db, ready, schema: options.schema, authorization: protectionAdapter,
                        vault: { database: db.name, account: 'protected-test' }, keys: new Map([['test-key', from], ['next', to]]),
                        targetKeyId: 'next', mode: 'exclusive', recoverable: true });
                } catch (failure) { rejectedBeforeAdmission = failure.code === 'MAGDB_PROTECTION_STATE_INVALID'; }
                await engine.assertDatabaseReady(db, ready, protectionAdapter);
                await request(meta('readwrite').put(audit, key));
                return { protocol: ready.protocolVersion, sealed: audit.ciphertext instanceof ArrayBuffer &&
                    audit.keyId === 'test-key' && !Object.hasOwn(audit, 'original'), payloadPreserved: plaintext.includes('processedRecords'),
                    rejectedBeforeAdmission };
            } finally { db.close(); }
        }, { options, plaintextAudit });
        assert.deepEqual(enrolled, { protocol: 3, sealed: true, payloadPreserved: true, rejectedBeforeAdmission: true });
        assert.deepEqual(await read(page), rows.map(row => ({ ...row, value: 10, repairs: 1 })));
    });

    if (cliFixture) test('installed CLI protected history executes all encryption directions and later structural revision', async ({ page }) => {
        const directory = path.join(path.dirname(cliFixture), 'protected-transition');
        const manifest = JSON.parse(await readFile(path.join(directory, 'manifest.json'), 'utf8'));
        const models = await Promise.all(manifest.schemas.map(async entry => JSON.parse(await readFile(path.join(directory, entry.path), 'utf8'))));
        const values = rows.map(row => ({ id: row.id, name: row.name, notes: 'Secret note ' + row.id }));
        await seed(page, models[0], values);
        const plan = { after: models.at(-1), url: getOrigin() + '/cli/protected-transition/manifest.json' };
        const result = await run(page, plan);
        assert.equal(result.ok, true, JSON.stringify(result));
        assert.equal(result.value.revision, 6);
        assert.deepEqual(await logical(page, models.at(-1)), values);
        const saved = await inspect(page);
        assert.equal(saved.state.protocolVersion, 3);
        assert.equal(saved.state.history.length, 5);
        assert.equal(saved.stores[0].indexes[0].name, 'byName');
    });
}
