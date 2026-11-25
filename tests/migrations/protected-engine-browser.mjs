import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';

export function registerProtectedEngineTests({ test, schema, bundle, copy, inspect, read, loadPage,
    recoveryPackage, cliFixture, getOrigin }) {
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
            assert.equal((await run(page, plan)).ok, false);
            assert.deepEqual(await inspect(page), before);
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

    test('authenticated recovery repairs source and target encryption partitions with the approved package', async ({ page }) => {
        const source = model({ encryptedFields: ['name'] }), target = model({ exposedFields: ['id'] });
        await seed(page, source, rows.map(row => ({ ...row, value: 1 })));
        const plan = planFor(source, target, [{ op: 'transform', store: 'clients' }], { module: `
            export function transform(row) { if (row.id === 2) throw new Error(row.name); return { ...row, value: 11 }; }` });
        const approved = recoveryPackage(plan);
        const interrupted = await run(page, plan);
        assert.equal(interrupted.error?.code, 'MAGDB_HOOK_FAILED');
        assert.ok(!JSON.stringify(interrupted).includes('Private'));
        assert.equal((await inspect(page)).state.active.processedRecords, 1);
        const result = await page.evaluate(options => tryEnsure({ ...options, protection: protectionAdapter }, true),
            { name: 'test', schema: target, manifestUrl: plan.url, batchSize: 1, recovery: approved });
        assert.equal(result.value?.outcome, 'recovered', JSON.stringify(result));
        assert.deepEqual(await logical(page, target), rows.map(row => ({ ...row, value: 10, repairs: 1 })));
        assert.ok(!JSON.stringify(await read(page)).includes('Private'));
        const completed = (await inspect(page)).state;
        assert.equal(completed.protocolVersion, 3);
        assert.equal(completed.history[0].outcome, 'recovered');
        assert.equal((await run(page, plan)).ok, true);
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
