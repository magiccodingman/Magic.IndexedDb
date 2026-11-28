// Run the public JavaScript migration protocol in real browser storage. No .NET
// runtime, DOM framework, fake IndexedDB implementation, or backend is involved.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { access, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { registerProtectedEngineTests } from './protected-engine-browser.mjs';
import { registerProtectedRegistrationTests } from './protected-registration-browser.mjs';

const root = path.resolve(import.meta.dirname, '../..');
const listOnly = process.argv[2] === '--list';
const browserName = listOnly ? 'chromium' : (process.argv[2] ?? 'chromium');
const cliFixture = process.env.MAGIC_CLI_FIXTURE === undefined ? null : path.resolve(process.env.MAGIC_CLI_FIXTURE);
let cliManifest;
if (cliFixture !== null) {
    try {
        cliManifest = JSON.parse(await readFile(cliFixture, 'utf8'));
        assert.equal(cliManifest.targetRevision, 2);
        assert.ok([1, 2].every(revision => cliManifest.schemas.some(schema => schema.revision === revision)));
    } catch (cause) {
        throw new Error('MAGIC_CLI_FIXTURE must point to a readable revision-2 CLI fixture manifest.', { cause });
    }
}
if (!['chromium', 'firefox', 'webkit'].includes(browserName)) throw new Error('Choose chromium, firefox, or webkit.');
const playwrightEntry = path.resolve(process.argv[3] ?? 'E2eTest/bin/Release/net10.0/.playwright/package/index.mjs');
const output = path.resolve(process.argv[4] ?? `artifacts/migration-browser/${browserName}`);
const assets = new Map();
const requests = [];
const tests = [];
let fixtureNumber = 0;
let origin;

function canonical(value) {
    if (Array.isArray(value)) return '[' + value.map(canonical).join(',') + ']';
    if (value && typeof value === 'object') return '{' + Object.keys(value).sort()
        .map(key => JSON.stringify(key) + ':' + canonical(value[key])).join(',') + '}';
    return JSON.stringify(value);
}
const digest = text => 'sha256:' + createHash('sha256').update(text).digest('hex');
const fingerprint = value => digest(canonical(value));
const copy = value => structuredClone(value);
const schema = () => ({
    protocolVersion: 1, schemaId: 'clients', numericFormat: 'legacy',
    stores: [{ name: 'clients', keyPath: 'id', autoIncrement: false, indexes: [], fields: [
        { name: 'id', type: 'integer32', nullable: false, codec: 'json' },
        { name: 'name', type: 'string', nullable: false, codec: 'json' }
    ] }], requiredCapabilities: []
});
const field = name => ({ name, type: 'string', nullable: true, codec: 'json' });
const index = (name, keyPath = name, unique = false) => ({ name, keyPath, unique, multiEntry: false });
const records = [{ id: 1, name: 'Ada' }, { id: 2, name: 'Grace' }];
const registrationStore = () => ({ tableName: 'clients', columnNamesInCompoundKey: ['id'],
    primaryKeyAuto: false, indexes: [], uniqueIndexes: [], columnNamesInCompoundIndex: [] });

function jsonAsset(url, value) {
    const body = JSON.stringify(value, null, 2) + '\n';
    assets.set(url, { body, type: 'application/json' });
    return { path: url, sha256: digest(body) };
}

function bundle(before, after, operations, options = {}) {
    const base = `/fixtures/${++fixtureNumber}`;
    const first = jsonAsset(`${base}/schemas/0001.json`, before);
    const second = jsonAsset(`${base}/schemas/0002.json`, after);
    const migration = {
        protocolVersion: 1, schemaId: before.schemaId, id: '0002_Change',
        fromRevision: 1, toRevision: 2, fromSchemaHash: fingerprint(before),
        toSchemaHash: fingerprint(after), mode: options.mode ?? 'atomic',
        requiredCapabilities: [], operations: copy(operations)
    };
    if (options.module) {
        assets.set(`${base}/migrations/transform.js`, { body: options.module, type: 'text/javascript' });
        for (const operation of migration.operations.filter(op => op.op === 'transform')) {
            operation.module ??= 'transform.js';
            operation.exportName ??= 'transform';
            operation.sha256 ??= digest(options.module);
        }
    }
    options.changeMigration?.(migration);
    const step = jsonAsset(`${base}/migrations/0002_Change.json`, migration);
    const manifest = {
        protocolVersion: 1, schemaId: before.schemaId, targetRevision: 2,
        requiredCapabilities: [],
        schemas: [
            { revision: 1, path: 'schemas/0001.json', sha256: first.sha256 },
            { revision: 2, path: 'schemas/0002.json', sha256: second.sha256 }
        ],
        migrations: [{ id: migration.id, fromRevision: 1, toRevision: 2,
            path: 'migrations/0002_Change.json', sha256: step.sha256 }],
        baselines: []
    };
    if (options.baseline) {
        const legacy = jsonAsset(`${base}/schemas/legacy.json`, options.baseline);
        manifest.baselines.push({ id: 'legacy-2x', revision: 1,
            schema: { path: 'schemas/legacy.json', sha256: legacy.sha256 } });
    }
    options.changeManifest?.(manifest);
    jsonAsset(`${base}/manifest.json`, manifest);
    return { url: origin + base + '/manifest.json', base, manifest, migration, before, after };
}

function appendRevision(plan, after, operations, mode = 'atomic') {
    const revision = plan.manifest.targetRevision + 1;
    const schemaPath = `schemas/${String(revision).padStart(4, '0')}.json`;
    const schemaAsset = jsonAsset(`${plan.base}/${schemaPath}`, after);
    const id = `${String(revision).padStart(4, '0')}_Next`;
    const migrationPath = `migrations/${id}.json`;
    const document = { protocolVersion: 1, schemaId: after.schemaId, id,
        fromRevision: revision - 1, toRevision: revision, fromSchemaHash: fingerprint(plan.after),
        toSchemaHash: fingerprint(after), mode, requiredCapabilities: [], operations };
    const migrationAsset = jsonAsset(`${plan.base}/${migrationPath}`, document);
    const manifest = copy(plan.manifest);
    manifest.targetRevision = revision;
    manifest.schemas.push({ revision, path: schemaPath, sha256: schemaAsset.sha256 });
    manifest.migrations.push({ id, fromRevision: revision - 1, toRevision: revision,
        path: migrationPath, sha256: migrationAsset.sha256 });
    jsonAsset(plan.base + '/manifest.json', manifest);
    return { ...plan, manifest, after, lastMigrationPath: plan.base + '/' + migrationPath };
}

function test(name, run) { tests.push({ name, run }); }

async function loadPage(context) {
    const page = await context.newPage();
    await page.goto(origin + '/', { waitUntil: 'domcontentloaded' });
    await page.evaluate(async () => {
        window.engine = await import('/engine/migrations/migrationEngine.js');
        window.events = [];
        window.request = request => new Promise((resolve, reject) => {
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        window.complete = transaction => new Promise((resolve, reject) => {
            transaction.oncomplete = () => resolve();
            transaction.onabort = () => reject(transaction.error ?? new Error('Transaction aborted.'));
            transaction.onerror = () => {};
        });
        window.openRaw = name => request(indexedDB.open(name));
        window.readRows = async name => {
            const db = await openRaw(name);
            try { return await request(db.transaction('clients').objectStore('clients').getAll()); }
            finally { db.close(); }
        };
        window.writeRows = async (name, rows) => {
            const db = await openRaw(name);
            try {
                const tx = db.transaction('clients', 'readwrite');
                const done = complete(tx);
                for (const row of rows) tx.objectStore('clients').put(row);
                await done;
            } finally { db.close(); }
        };
        window.tryEnsure = async (options, recovering = false) => {
            try {
                const value = await (recovering ? engine.recoverDatabase : engine.ensureDatabase)({ ...options, onEvent: event => events.push(event) });
                return { ok: true, value };
            } catch (error) {
                return { ok: false, error: { code: error.code, message: error.message,
                    retryable: error.retryable, details: error.details } };
            }
        };
        window.makeLegacy = async (name, model, rows, version = 10) => {
            const opening = indexedDB.open(name, version);
            opening.onupgradeneeded = () => {
                for (const definition of model.stores) {
                    const options = { autoIncrement: definition.autoIncrement };
                    if (definition.keyPath !== null) options.keyPath = definition.keyPath;
                    const store = opening.result.createObjectStore(definition.name, options);
                    for (const i of definition.indexes) store.createIndex(i.name, i.keyPath,
                        { unique: i.unique, multiEntry: i.multiEntry });
                }
            };
            const db = await request(opening);
            db.close();
            if (rows?.length) await writeRows(name, rows);
        };
    });
    return page;
}

async function seed(page, model = schema(), rows = records, name = 'test') {
    const result = await page.evaluate(async ({ model, rows, name }) => {
        const ready = await engine.ensureDatabase({ name, schema: model });
        await writeRows(name, rows);
        return ready;
    }, { model, rows, name });
    assert.equal(result.revision, 1);
    return result;
}
const runPlan = (page, plan, extra = {}) => page.evaluate(options => tryEnsure(options), {
    name: 'test', schema: plan.after, manifestUrl: plan.url, ...extra
});
const read = (page, name = 'test') => page.evaluate(name => readRows(name), name);
const inspect = (page, name = 'test') => page.evaluate(name => engine.inspectDatabase(name), name);
const expectFailure = result => {
    assert.equal(result.ok, false, 'Unsafe plan unexpectedly succeeded.');
    assert.match(result.error.code ?? '', /^MAG(?:DB|IC)_/, 'Failure needs a stable diagnostic code.');
};
async function unchanged(page, previous, expectedRows = records) {
    assert.equal((await inspect(page)).nativeVersion, previous.nativeVersion, 'Failed preflight/atomic migration changed native version.');
    assert.deepEqual(await read(page), expectedRows, 'Failed operation changed records.');
}

test('inspection does not create a nonexistent database', async ({ page }) => {
    assert.equal(await inspect(page, 'missing'), null);
    const names = await page.evaluate(async () => (await indexedDB.databases()).map(x => x.name));
    assert.ok(!names.includes('missing'));
});

test('fresh inline schema creates stores and native indexes', async ({ page }) => {
    const model = schema(); model.stores[0].indexes.push(index('byName', 'name'));
    await seed(page, model);
    const state = await inspect(page);
    const actual = state.stores.find(s => s.name === 'clients');
    assert.equal(actual.keyPath, 'id');
    assert.ok(actual.indexes.some(i => i.name === 'byName' && i.keyPath === 'name'));
    assert.deepEqual(await read(page), records);
});

test('fresh target bootstrap skips historical migration downloads and transforms', async ({ page }) => {
    const model = schema();
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        module: 'export function transform() { throw new Error("History must not run on bootstrap"); }'
    });
    assets.delete(plan.base + '/migrations/0002_Change.json');
    const beforeRequests = requests.length;
    const result = await runPlan(page, plan);
    assert.equal(result.ok, true, JSON.stringify(result));
    assert.equal(result.value.revision, 2);
    assert.equal((await read(page)).length, 0);
    assert.ok(!requests.slice(beforeRequests).includes(plan.base + '/migrations/0002_Change.json'));
});

for (const [name, change, expectedCode] of [
    ['missing history edge', manifest => { manifest.migrations = []; }, 'MAGDB_HISTORY_INVALID'],
    ['missing historical schema', manifest => { manifest.schemas = manifest.schemas.filter(entry => entry.revision !== 1); }, 'MAGDB_HISTORY_INVALID'],
    ['schema beyond the target', manifest => { manifest.schemas.push({ ...manifest.schemas[1], revision: 3 }); }, 'MAGDB_MANIFEST_INVALID']
]) test(`fresh bootstrap rejects ${name} without creating storage`, async ({ page }) => {
    const model = schema();
    const plan = bundle(model, model, [], { changeManifest: change });
    const result = await runPlan(page, plan);
    expectFailure(result); assert.equal(result.error.code, expectedCode);
    assert.equal(await inspect(page), null);
});

test('independent physical names share a schema without sharing data', async ({ page }) => {
    await seed(page, schema(), records, 'organization-a');
    await seed(page, schema(), [{ id: 7, name: 'Different' }], 'organization-b');
    assert.deepEqual(await read(page, 'organization-a'), records);
    assert.deepEqual(await read(page, 'organization-b'), [{ id: 7, name: 'Different' }]);
});

test('repeated ready opens do not change revision or native version', async ({ page }) => {
    const ready = await seed(page);
    const second = await page.evaluate(model => engine.ensureDatabase({ name: 'test', schema: model }), schema());
    assert.equal(second.revision, ready.revision);
    assert.equal(second.nativeVersion, ready.nativeVersion);
    assert.deepEqual(await read(page), records);
});

test('schema input is pinned before waiting for migration ownership', async ({ page }) => {
    const model = schema();
    const result = await page.evaluate(async input => {
        let release;
        let acquired;
        const acquiredPromise = new Promise(resolve => { acquired = resolve; });
        const blocker = navigator.locks.request('magic-indexeddb:migrate:test', async () => {
            acquired(); await new Promise(resolve => { release = resolve; });
        });
        await acquiredPromise;
        const pending = engine.ensureDatabase({ name: 'test', schema: input });
        input.stores.push({ name: 'unexpected', keyPath: 'id', autoIncrement: false, indexes: [], fields: [] });
        release(); await blocker;
        return await pending;
    }, model);
    assert.equal(result.schemaHash, fingerprint(model));
    assert.ok(!(await inspect(page)).stores.some(s => s.name === 'unexpected'));
});

test('unknown protocol fails before database creation', async ({ page }) => {
    const model = schema(); model.protocolVersion = 999;
    expectFailure(await page.evaluate(model => tryEnsure({ name: 'test', schema: model }), model));
    assert.equal(await inspect(page), null);
});

test('unknown required capability fails before database creation', async ({ page }) => {
    const model = schema(); model.requiredCapabilities = ['unsupported-future-operation'];
    expectFailure(await page.evaluate(model => tryEnsure({ name: 'test', schema: model }), model));
    assert.equal(await inspect(page), null);
});

test('protected schemas are rejected by the unauthenticated plain runner', async ({ page }) => {
    const model = schema(); model.stores[0].encryptedFields = ['name'];
    expectFailure(await page.evaluate(model => tryEnsure({ name: 'test', schema: model }), model));
    assert.equal(await inspect(page), null);
});

for (const [name, protection] of [
    ['field protection', { encryptedFields: ['name'] }],
    ['whole-record protection with no public fields', { exposedFields: [] }]
]) test(`a plain target cannot bypass historical ${name}`, async ({ page }) => {
    const plain = schema(); const historical = copy(plain);
    Object.assign(historical.stores[0], protection);
    const ready = await seed(page, plain);
    // Simulate an existing managed installation whose recorded model included
    // protection. The ordinary target has the same native layout; only the
    // supported storage adapter can decide how to interpret those records.
    await page.evaluate(async schemaHash => {
        const db = await openRaw('test');
        try {
            const tx = db.transaction('__magic_migrations__', 'readwrite'); const done = complete(tx);
            const store = tx.objectStore('__magic_migrations__'); const get = store.get('state');
            get.onsuccess = () => store.put({ ...get.result, schemaHash }, 'state');
            await done;
        } finally { db.close(); }
    }, fingerprint(historical));
    const plan = bundle(historical, plain, [{ op: 'setMissing', store: 'clients', path: 'unsafeRawUpdate', value: true }]);
    const result = await runPlan(page, plan);
    expectFailure(result); assert.equal(result.error.code, 'MAGDB_PROTECTED_ADAPTER_REQUIRED');
    await unchanged(page, ready);
    assert.equal((await inspect(page)).state.schemaHash, fingerprint(historical));
});

test('a protected intermediate model fails before earlier plain revisions write', async ({ page }) => {
    const plain = schema(); const intermediate = copy(plain); intermediate.stores[0].exposedFields = [];
    const ready = await seed(page, plain);
    const plan = appendRevision(bundle(plain, intermediate,
        [{ op: 'setMissing', store: 'clients', path: 'unsafeRawUpdate', value: true }]), plain, []);
    const result = await runPlan(page, plan);
    expectFailure(result); assert.equal(result.error.code, 'MAGDB_PROTECTED_ADAPTER_REQUIRED');
    await unchanged(page, ready);
});

test('a protected adoption baseline is rejected before stamping an unmanaged database', async ({ page }) => {
    const plain = schema(); const historical = copy(plain); historical.stores[0].encryptedFields = ['name'];
    await page.evaluate(({ model, rows }) => makeLegacy('test', model, rows), { model: plain, rows: records });
    const previous = await inspect(page);
    const plan = bundle(historical, plain,
        [{ op: 'setMissing', store: 'clients', path: 'unsafeRawUpdate', value: true }], { baseline: historical });
    const result = await runPlan(page, plan);
    expectFailure(result); assert.equal(result.error.code, 'MAGDB_PROTECTED_ADAPTER_REQUIRED');
    await unchanged(page, previous);
    assert.equal((await inspect(page)).state, null);
});

test('a transient protected store cannot bypass preflight by being removed from the target', async ({ page }) => {
    const model = schema(); const ready = await seed(page, model);
    const definition = { ...copy(model.stores[0]), name: 'temporary', fields: [], exposedFields: [] };
    const plan = bundle(model, model, [
        { op: 'setMissing', store: 'clients', path: 'unsafeRawUpdate', value: true },
        { op: 'createStore', definition },
        { op: 'deleteStore', store: 'temporary', allowDataLoss: true }
    ]);
    const result = await runPlan(page, plan);
    expectFailure(result); assert.equal(result.error.code, 'MAGDB_PROTECTED_ADAPTER_REQUIRED');
    await unchanged(page, ready);
    assert.ok(!(await inspect(page)).stores.some(store => store.name === 'temporary'));
});

test('protected registration defers its manifest and storage until authorization', async ({ page }) => {
    const physical = '__magic_protected__:new-vault';
    const configuration = { name: physical + ':account', encryption: { account: 'fixture', physicalName: physical },
        storeSchemas: [{ ...registrationStore(), encryptedFields: ['secret'] }],
        migrationManifestUrl: origin + '/unsupported-protected/manifest.json' };
    const beforeRequests = requests.length;
    const result = await page.evaluate(async configuration => {
        const api = await import('/engine/magicDB.js');
        await api.createDb(configuration);
        try { await api.ensureProtectedMigration(configuration.name); return { ok: true }; }
        catch (error) { return { ok: false, error: { code: error.code } }; }
    }, configuration);
    expectFailure(result);
    assert.equal(result.error.code, 'MAGDB_AUTHORIZATION_REQUIRED');
    assert.equal(await inspect(page, physical), null);
    assert.ok(!requests.slice(beforeRequests).includes('/unsupported-protected/manifest.json'));
});

test('conflicting protected registration preserves authenticated CRUD and ciphertext', async ({ page }) => {
    const physical = '__magic_protected__:existing-vault';
    const configuration = { name: physical + ':account', encryption: { account: 'fixture', physicalName: physical },
        storeSchemas: [{ ...registrationStore(), encryptedFields: ['secret'] }] };
    const result = await page.evaluate(async ({ configuration, physical }) => {
        const api = await import('/engine/magicDB.js');
        await api.createDb(configuration);
        await api.unlockProtectedDatabase(configuration.name, 'fixture-key', new Uint8Array(32).fill(17), true);
        await api.addItem({ dbName: configuration.name, storeName: 'clients',
            record: { id: 1, name: 'Public fixture', secret: 'Encrypted fixture' } });
        async function rawSnapshot() {
            const db = await openRaw(physical);
            try {
                const row = await request(db.transaction('clients').objectStore('clients').get(1));
                return { nativeVersion: db.version, stores: [...db.objectStoreNames],
                    ciphertext: Array.from(new Uint8Array(row.secret.ciphertext)) };
            } finally { db.close(); }
        }
        const before = await rawSnapshot();
        let code;
        try { await api.createDb({ ...configuration, migrationManifestUrl: '/unsupported-protected/manifest.json' }); }
        catch (error) { code = error.code; }
        const after = await rawSnapshot();
        const row = await api.findItem(configuration.name, 'clients', 1);
        api.releaseProtectedDatabase(configuration.name);
        return { code, before, after, decrypted: row.secret };
    }, { configuration, physical });
    assert.equal(result.code, 'MAGDB_CONFIGURATION_CONFLICT');
    assert.deepEqual(result.after, result.before);
    assert.equal(result.decrypted, 'Encrypted fixture');
    assert.ok(!result.after.stores.includes('__magic_migrations__'));
});

test('managed numeric registration detects durable policy tampering before writes', async ({ page }) => {
    const configuration = { name: '__magic_numeric_v1__:policy-fixture', numericLegacyName: 'policy-fixture',
        numericStorage: { version: 1, mode: 'lossless' }, storeSchemas: [registrationStore()] };
    const result = await page.evaluate(async configuration => {
        const api = await import('/engine/magicDB.js');
        await api.createDb(configuration);
        await api.addItem({ dbName: configuration.name, storeName: 'clients', record: { id: 1, name: 'Retained' } });
        const db = await openRaw(configuration.name);
        try {
            const tx = db.transaction('__magic_numeric_policy', 'readwrite'); const done = complete(tx);
            const table = tx.objectStore('__magic_numeric_policy'); const saved = table.get('policy');
            saved.onsuccess = () => table.put({ ...saved.result, version: 99 }, 'policy');
            await done;
        } finally { db.close(); }
        const failures = [];
        for (const operation of [
            () => api.createDb(configuration),
            () => api.openDb(configuration.name),
            () => api.addItem({ dbName: configuration.name, storeName: 'clients', record: { id: 2, name: 'Must not be written' } })
        ]) {
            try { await operation(); failures.push(null); }
            catch (error) { failures.push(error.message); }
        }
        const rows = await readRows(configuration.name);
        await api.closeDb(configuration.name);
        return { failures, rows };
    }, configuration);
    assert.ok(result.failures.every(error => error?.includes('NUMERIC_POLICY_MISMATCH')), JSON.stringify(result.failures));
    assert.deepEqual(result.rows, [{ id: 1, name: 'Retained' }]);
});

test('managed numeric fresh provisioning and reopen preserve reserved metadata and records', async ({ page }) => {
    const configuration = { name: '__magic_numeric_v1__:reopen-fixture', numericLegacyName: 'reopen-fixture',
        numericStorage: { version: 1, mode: 'lossless' }, storeSchemas: [registrationStore()] };
    const result = await page.evaluate(async configuration => {
        const api = await import('/engine/magicDB.js');
        await api.createDb(configuration);
        await api.addItem({ dbName: configuration.name, storeName: 'clients', record: { id: 1, name: 'Retained' } });
        await api.closeDb(configuration.name); await api.openDb(configuration.name); await api.createDb(configuration);
        let metadataRejected = false;
        try { await api.toArray(configuration.name, '__magic_numeric_policy'); }
        catch { metadataRejected = true; }
        const raw = await api.openDb(configuration.name);
        const row = await api.findItem(configuration.name, 'clients', 1);
        await api.closeDb(configuration.name);
        return { row, metadataRejected, rawHandleHidden: raw === undefined };
    }, configuration);
    assert.equal(result.metadataRejected, true); assert.equal(result.rawHandleHidden, true);
    assert.deepEqual(result.row, { id: 1, name: 'Retained' });
});

test('atomic backfill and index upgrade preserves unrelated record data', async ({ page }) => {
    const before = schema(); const after = copy(before);
    after.stores[0].fields.push(field('status')); after.stores[0].indexes.push(index('status'));
    const original = [{ id: 1, name: 'Ada', status: null, arbitrary: { retained: true } }, { id: 2, name: 'Grace' }];
    await seed(page, before, original);
    const plan = bundle(before, after, [
        { op: 'setMissing', store: 'clients', path: 'status', value: 'active' },
        { op: 'createIndex', store: 'clients', index: index('status') }
    ]);
    const result = await runPlan(page, plan);
    assert.equal(result.ok, true, JSON.stringify(result));
    assert.equal(result.value.revision, 2);
    assert.deepEqual(await read(page), [original[0], { ...original[1], status: 'active' }]);
    const count = await page.evaluate(async () => {
        const db = await openRaw('test');
        try { return await request(db.transaction('clients').objectStore('clients').index('status').count('active')); }
        finally { db.close(); }
    });
    assert.equal(count, 1);
});

test('a client skipping releases follows the ordered immutable migration chain', async ({ page }) => {
    const before = schema(); const middle = copy(before); middle.stores[0].fields.push(field('status'));
    const after = copy(before); after.stores[0].fields.push(field('lifecycle'));
    await seed(page, before);
    const plan = appendRevision(bundle(before, middle,
        [{ op: 'setMissing', store: 'clients', path: 'status', value: 'active' }]), after,
        [{ op: 'renameField', store: 'clients', from: 'status', to: 'lifecycle' }]);
    const result = await runPlan(page, plan);
    assert.equal(result.ok, true, JSON.stringify(result)); assert.equal(result.value.revision, 3);
    assert.deepEqual(await read(page), records.map(row => ({ ...row, lifecycle: 'active' })));
    assert.equal((await inspect(page)).state.history.length, 2);
});

test('a missing later release asset prevents earlier migrations from starting', async ({ page }) => {
    const before = schema(); const middle = copy(before); middle.stores[0].fields.push(field('status'));
    const after = copy(before); after.stores[0].fields.push(field('lifecycle'));
    const ready = await seed(page, before);
    const plan = appendRevision(bundle(before, middle,
        [{ op: 'setMissing', store: 'clients', path: 'status', value: 'active' }]), after,
        [{ op: 'renameField', store: 'clients', from: 'status', to: 'lifecycle' }]);
    assets.delete(plan.lastMigrationPath);
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('retry after a later transaction fails does not replay a committed earlier release', async ({ page }) => {
    const before = schema(); const after = copy(before); after.stores[0].indexes.push(index('uniqueName', 'name', true));
    const original = records.map(row => ({ ...row, name: 'Duplicate', visits: 0 }));
    await seed(page, before, original);
    const plan = appendRevision(bundle(before, before, [{ op: 'transform', store: 'clients' }], {
        module: 'export function transform(row) { return {...row, visits:row.visits+1}; }'
    }), after, [{ op: 'createIndex', store: 'clients', index: index('uniqueName', 'name', true) }]);
    expectFailure(await runPlan(page, plan));
    assert.equal((await inspect(page)).state.revision, 2);
    assert.ok((await read(page)).every(row => row.visits === 1));
    await page.evaluate(() => writeRows('test', [{ id: 2, name: 'Unique', visits: 1 }]));
    const resumed = await runPlan(page, plan);
    assert.equal(resumed.ok, true, JSON.stringify(resumed)); assert.equal(resumed.value.revision, 3);
    assert.ok((await read(page)).every(row => row.visits === 1));
});

test('moving completed migration references to different revisions rejects on ready reopen', async ({ page }) => {
    const model = schema(); await seed(page, model);
    const plan = appendRevision(bundle(model, model, []), model, []);
    const completed = await runPlan(page, plan);
    assert.equal(completed.ok, true, JSON.stringify(completed));
    const manifest = copy(plan.manifest);
    Object.assign(manifest.migrations[0], { fromRevision: 2, toRevision: 3 });
    Object.assign(manifest.migrations[1], { fromRevision: 1, toRevision: 2 });
    jsonAsset(plan.base + '/manifest.json', manifest);
    const rejected = await runPlan(page, plan);
    expectFailure(rejected); assert.equal(rejected.error.code, 'MAGDB_HISTORY_REWRITTEN');
    await unchanged(page, completed.value);
});

for (const invalidHistory of ['duplicate', 'too-long']) test(`a ${invalidHistory} completed journal is rejected without applying work`, async ({ page }) => {
    const model = schema(); await seed(page, model);
    let plan = bundle(model, model, []);
    if (invalidHistory === 'duplicate') plan = appendRevision(plan, model, []);
    const completed = await runPlan(page, plan);
    assert.equal(completed.ok, true, JSON.stringify(completed));
    await page.evaluate(async kind => {
        const db = await openRaw('test');
        try {
            const tx = db.transaction('__magic_migrations__', 'readwrite'); const done = complete(tx);
            const store = tx.objectStore('__magic_migrations__'); const get = store.get('state');
            get.onsuccess = () => {
                const state = get.result;
                if (kind === 'duplicate') state.history[1] = structuredClone(state.history[0]);
                else state.history.push({ id: 'impossible-extra-entry', sha256: 'sha256:' + '0'.repeat(64) });
                store.put(state, 'state');
            };
            await done;
        } finally { db.close(); }
    }, invalidHistory);
    const rejected = await runPlan(page, plan);
    expectFailure(rejected); assert.equal(rejected.error.code, 'MAGDB_STATE_INVALID');
    await unchanged(page, completed.value);
});

for (const [name, change] of [
    ['unknown operation', migration => migration.operations.push({ op: 'future-op' })],
    ['unresolved manual decision', migration => migration.operations.push({ op: 'manual', reason: 'Choose a backfill.' })],
    ['wrong source fingerprint', migration => migration.fromSchemaHash = 'sha256:' + '0'.repeat(64)],
    ['wrong target fingerprint', migration => migration.toSchemaHash = 'sha256:' + '0'.repeat(64)],
    ['unknown migration protocol', migration => migration.protocolVersion = 999],
    ['unknown migration capability', migration => migration.requiredCapabilities = ['unsupported']]
]) test(`${name} fails before mutation`, async ({ page }) => {
    const ready = await seed(page); const model = schema();
    const plan = bundle(model, model, [], { changeMigration: change });
    expectFailure(await runPlan(page, plan));
    await unchanged(page, ready);
});

test('missing migration asset fails before mutation', async ({ page }) => {
    const ready = await seed(page); const model = schema(); const plan = bundle(model, model, []);
    assets.delete(plan.base + '/migrations/0002_Change.json');
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('modified migration bytes fail integrity before mutation', async ({ page }) => {
    const ready = await seed(page); const model = schema(); const plan = bundle(model, model, []);
    assets.get(plan.base + '/migrations/0002_Change.json').body += ' ';
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('modified target schema bytes fail integrity before mutation', async ({ page }) => {
    const ready = await seed(page); const model = schema(); const plan = bundle(model, model, []);
    assets.get(plan.base + '/schemas/0002.json').body += ' ';
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('runtime model and release target mismatch fails before mutation', async ({ page }) => {
    const ready = await seed(page); const before = schema(); const after = copy(before);
    after.stores[0].indexes.push(index('byName', 'name'));
    const plan = bundle(before, after, [{ op: 'createIndex', store: 'clients', index: index('byName', 'name') }]);
    expectFailure(await runPlan(page, plan, { schema: before })); await unchanged(page, ready);
});

test('foreign schema identity is rejected for an existing instance', async ({ page }) => {
    const ready = await seed(page); const other = schema(); other.schemaId = 'another-history';
    expectFailure(await page.evaluate(model => tryEnsure({ name: 'test', schema: model }), other));
    await unchanged(page, ready);
});

test('duplicate migration edges are rejected', async ({ page }) => {
    const ready = await seed(page); const model = schema();
    const plan = bundle(model, model, [], { changeManifest: manifest => manifest.migrations.push(copy(manifest.migrations[0])) });
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('missing migration path is rejected', async ({ page }) => {
    const ready = await seed(page); const model = schema();
    const plan = bundle(model, model, [], { changeManifest: manifest => manifest.migrations = [] });
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('unique index conflict rolls back structural and data changes', async ({ page }) => {
    const before = schema(); const after = copy(before);
    after.stores[0].fields.push(field('status'));
    after.stores[0].indexes.push(index('uniqueName', 'name', true));
    const original = [{ id: 1, name: 'duplicate' }, { id: 2, name: 'duplicate' }];
    const ready = await seed(page, before, original);
    const plan = bundle(before, after, [
        { op: 'setMissing', store: 'clients', path: 'status', value: 'should-roll-back' },
        { op: 'createIndex', store: 'clients', index: index('uniqueName', 'name', true) }
    ]);
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready, original);
    assert.ok(!(await inspect(page)).stores.find(s => s.name === 'clients').indexes.some(i => i.name === 'uniqueName'));
});

test('throwing atomic transform rolls back earlier updated records', async ({ page }) => {
    const ready = await seed(page); const model = schema();
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        module: 'export function transform(row) { if (row.id === 2) throw new Error("bad fixture"); return {...row, name:"changed"}; }'
    });
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('asynchronous atomic transform is rejected without partial writes', async ({ page }) => {
    const ready = await seed(page); const model = schema();
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        module: 'export async function transform(row) { return {...row, name:"changed"}; }'
    });
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('rejected asynchronous atomic hook is observed without unhandled rejection', async ({ page }) => {
    const ready = await seed(page); const model = schema();
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        module: 'export async function transform() { throw new Error("async hook rejected"); }'
    });
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('resumable transform cannot replace an out-of-line object with null', async ({ page }) => {
    const model = schema(); model.stores[0].keyPath = null;
    await page.evaluate(async ({ model, rows }) => {
        await engine.ensureDatabase({ name: 'test', schema: model });
        const db = await openRaw('test');
        try {
            const tx = db.transaction('clients', 'readwrite'); const done = complete(tx);
            for (const row of rows) tx.objectStore('clients').put(row, row.id);
            await done;
        } finally { db.close(); }
    }, { model, rows: records });
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export function transform() { return null; }'
    });
    expectFailure(await runPlan(page, plan));
    assert.deepEqual(await read(page), records);
});

test('modified custom hook bytes are rejected before mutation', async ({ page }) => {
    const ready = await seed(page); const model = schema();
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        module: 'export function transform(row) { return row; }'
    });
    assets.get(plan.base + '/migrations/transform.js').body += '\n// unexpected change';
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('custom hook transforms historical records without a .NET runtime', async ({ page }) => {
    const model = schema(); await seed(page);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        module: 'export function transform(row) { return {...row, name: row.name.toUpperCase()}; }'
    });
    const result = await runPlan(page, plan);
    assert.equal(result.ok, true, JSON.stringify(result));
    assert.deepEqual(await read(page), records.map(row => ({ ...row, name: row.name.toUpperCase() })));
});

test('field rename preserves data and updates the declared model', async ({ page }) => {
    const before = schema(); const after = copy(before);
    after.stores[0].fields.find(f => f.name === 'name').name = 'displayName';
    await seed(page, before);
    const plan = bundle(before, after, [{ op: 'renameField', store: 'clients', from: 'name', to: 'displayName' }]);
    const result = await runPlan(page, plan);
    assert.equal(result.ok, true, JSON.stringify(result));
    assert.deepEqual(await read(page), records.map(({ id, name }) => ({ id, displayName: name })));
});

test('field rename collision rejects without overwriting a destination value', async ({ page }) => {
    const before = schema(); const after = copy(before);
    after.stores[0].fields.find(f => f.name === 'name').name = 'displayName';
    const original = [{ id: 1, name: 'Ada', displayName: 'Already occupied' }];
    const ready = await seed(page, before, original);
    const plan = bundle(before, after, [{ op: 'renameField', store: 'clients', from: 'name', to: 'displayName' }]);
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready, original);
});

test('store removal requires an explicit allowDataLoss declaration', async ({ page }) => {
    const ready = await seed(page); const before = schema(); const after = copy(before); after.stores = [];
    const plan = bundle(before, after, [{ op: 'deleteStore', store: 'clients' }]);
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
});

test('explicit store removal succeeds and finalizes only the declared target', async ({ page }) => {
    await seed(page); const before = schema(); const after = copy(before); after.stores = [];
    const plan = bundle(before, after, [{ op: 'deleteStore', store: 'clients', allowDataLoss: true }]);
    const result = await runPlan(page, plan);
    assert.equal(result.ok, true, JSON.stringify(result));
    assert.equal(result.value.revision, 2);
    assert.ok(!(await inspect(page)).stores.some(s => s.name === 'clients'));
});

test('unsafe transform field names are rejected before writes', async ({ page }) => {
    const ready = await seed(page); const model = schema();
    const plan = bundle(model, model, [{ op: 'setMissing', store: 'clients', path: '__proto__', value: { compromised: true } }]);
    expectFailure(await runPlan(page, plan)); await unchanged(page, ready);
    assert.equal(await page.evaluate(() => ({}).compromised), undefined);
});

test('unexpected physical indexes fail readiness without schema repair', async ({ page }) => {
    await seed(page); const previous = await inspect(page);
    await page.evaluate(async version => {
        const opening = indexedDB.open('test', version + 1);
        opening.onupgradeneeded = () => opening.transaction.objectStore('clients').createIndex('unexpected', 'name');
        const db = await request(opening); db.close();
    }, previous.nativeVersion);
    expectFailure(await page.evaluate(model => tryEnsure({ name: 'test', schema: model }), schema()));
    assert.deepEqual(await read(page), records);
    assert.ok((await inspect(page)).stores.find(s => s.name === 'clients').indexes.some(i => i.name === 'unexpected'));
});

test('unknown legacy database is never silently stamped or rebuilt', async ({ page }) => {
    const model = schema();
    await page.evaluate(({ model, rows }) => makeLegacy('test', model, rows), { model, rows: records });
    const previous = await inspect(page);
    expectFailure(await page.evaluate(model => tryEnsure({ name: 'test', schema: model }), model));
    await unchanged(page, previous);
});

test('declared legacy baseline adopts native version ten and preserves rows', async ({ page }) => {
    const model = schema();
    await page.evaluate(({ model, rows }) => makeLegacy('test', model, rows), { model, rows: records });
    const plan = bundle(model, model, [], { baseline: model });
    const result = await runPlan(page, plan);
    assert.equal(result.ok, true, JSON.stringify(result));
    assert.equal(result.value.revision, 2);
    assert.ok(result.value.nativeVersion >= 10);
    assert.deepEqual(await read(page), records);
});

test('legacy baseline with unrecognized extra stores fails without dropping them', async ({ page }) => {
    const model = schema(); const legacy = copy(model);
    legacy.stores.push({ name: 'unexpected', keyPath: 'id', autoIncrement: false, indexes: [], fields: [] });
    await page.evaluate(({ model, rows }) => makeLegacy('test', model, rows), { model: legacy, rows: records });
    const previous = await inspect(page); const plan = bundle(model, model, [], { baseline: model });
    expectFailure(await runPlan(page, plan)); await unchanged(page, previous);
    assert.ok((await inspect(page)).stores.some(s => s.name === 'unexpected'));
});

test('legacy baseline with a missing required index is rejected', async ({ page }) => {
    const actual = schema(); const expected = copy(actual); expected.stores[0].indexes.push(index('byName', 'name'));
    await page.evaluate(({ model, rows }) => makeLegacy('test', model, rows), { model: actual, rows: records });
    const previous = await inspect(page); const plan = bundle(expected, expected, [], { baseline: expected });
    expectFailure(await runPlan(page, plan)); await unchanged(page, previous);
});

test('resumable retry does not replay already committed batches', async ({ page }) => {
    const model = schema(); const original = Array.from({ length: 9 }, (_, i) => ({ id: i + 1, name: `Client ${i + 1}`, visits: 0 }));
    await seed(page, model, original);
    await page.evaluate(() => { window.injectFailure = true; });
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export function transform(row) { if (row.id === 5 && globalThis.injectFailure) throw new Error("injected fault"); return {...row, visits:row.visits+1}; }'
    });
    expectFailure(await runPlan(page, plan, { batchSize: 2 }));
    const partial = await read(page);
    assert.deepEqual(partial.map(r => r.visits), [1, 1, 1, 1, 0, 0, 0, 0, 0]);
    await page.evaluate(() => { window.injectFailure = false; });
    const resumed = await runPlan(page, plan, { batchSize: 2 });
    assert.equal(resumed.ok, true, JSON.stringify(resumed));
    assert.equal(resumed.value.revision, 2);
    assert.ok((await read(page)).every(r => r.visits === 1));
});

test('resumable hooks can reuse an output object without corrupting earlier batch results', async ({ page }) => {
    const model = schema();
    const original = records.map(row => ({ ...row, visits: 0 }));
    await seed(page, model, original);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable',
        module: 'const scratch = {}; export function transform(row) { Object.assign(scratch, row, {visits: row.visits + 1}); return scratch; }'
    });
    const result = await runPlan(page, plan, { batchSize: 2 });
    assert.equal(result.ok, true, JSON.stringify(result));
    assert.deepEqual(await read(page), original.map(row => ({ ...row, visits: 1 })));
    assert.equal((await inspect(page)).state.status, 'ready');
});

test('public recovery diagnostics redact the durable record-key checkpoint', async ({ page }) => {
    const model = schema(); model.stores[0].fields[0].type = 'string';
    const privateKey = 'PRIVATE_ACCOUNT_IDENTIFIER_28a';
    const original = [{ id: privateKey + '-1', name: 'First' }, { id: privateKey + '-2', name: 'Second' }];
    await seed(page, model, original);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export function transform(row) { if (row.name === "Second") throw new Error("stop after one committed batch"); return {...row, visited:true}; }'
    });
    expectFailure(await runPlan(page, plan, { batchSize: 1 }));
    const diagnostics = await inspect(page);
    assert.ok(!JSON.stringify(diagnostics).includes(privateKey));
    assert.equal(diagnostics.state.active.hasCheckpoint, true);
    assert.equal(Object.hasOwn(diagnostics.state.active, 'lastKey'), false);
    const readinessCode = await page.evaluate(async state => {
        const db = await openRaw('test');
        try {
            await engine.assertDatabaseReady(db, { ...state.state, nativeVersion: state.nativeVersion });
        } catch (error) { return error.code; }
        finally { db.close(); }
    }, diagnostics);
    assert.equal(readinessCode, 'MAGDB_DATABASE_INCOMPATIBLE');
    const partial = [{ ...original[0], visited: true }, original[1]];
    assert.deepEqual(await read(page), partial);
    const retry = await runPlan(page, plan, { batchSize: 1 });
    assert.equal(retry.error?.code, 'MAGDB_HOOK_FAILED');
    assert.equal(retry.error.retryable, false);
    const failureEvent = await page.evaluate(() => events.findLast(event => event.phase === 'failed'));
    assert.equal(failureEvent.code, 'MAGDB_HOOK_FAILED');
    assert.equal(failureEvent.retryable, false);
    assert.ok(!JSON.stringify({ retry, failureEvent }).includes('stop after one committed batch'));
    const replacement = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export function transform(row) { return {...row, fixed: true}; }'
    });
    assert.equal((await runPlan(page, replacement, { batchSize: 1 })).error?.code, 'MAGDB_RESUME_PACKAGE_REQUIRED');
    assert.deepEqual(await inspect(page), diagnostics);
    assert.deepEqual(await read(page), partial);
});

test('resumable key-changing hook is rejected without skipping records', async ({ page }) => {
    const ready = await seed(page); const model = schema();
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export function transform(row) { return {...row, id:row.id+100}; }'
    });
    expectFailure(await runPlan(page, plan, { batchSize: 1 }));
    assert.deepEqual(await read(page), records);
    assert.ok((await inspect(page)).nativeVersion >= ready.nativeVersion);
});

test('mutating a Date context key cannot bypass resumable primary-key protection', async ({ page }) => {
    const model = schema(); model.stores[0].fields[0].type = 'date';
    await page.evaluate(async model => {
        await engine.ensureDatabase({ name: 'test', schema: model });
        await writeRows('test', [{ id: new Date('2024-01-01T00:00:00Z'), name: 'Original' }]);
    }, model);
    const before = await read(page);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export function transform(row, context) { context.primaryKey.setUTCFullYear(2025); row.id = context.primaryKey; return row; }'
    });
    expectFailure(await runPlan(page, plan, { batchSize: 1 }));
    assert.deepEqual(await read(page), before);
});

test('page termination releases ownership and resumes from a durable checkpoint', async ({ page, context }) => {
    const model = schema(); const original = Array.from({ length: 6 }, (_, i) => ({ id: i + 1, name: `Client ${i + 1}`, visits: 0 }));
    await seed(page, model, original);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export async function transform(row) { if (row.id === 3 && globalThis.holdMigration) { globalThis.waitingForTermination=true; await new Promise(()=>{}); } return {...row, visits:row.visits+1}; }'
    });
    await page.evaluate(options => {
        window.holdMigration = true;
        window.running = tryEnsure(options);
    }, { name: 'test', schema: model, manifestUrl: plan.url, batchSize: 2 });
    await page.waitForFunction(() => window.waitingForTermination === true);
    const reader = await loadPage(context);
    assert.deepEqual((await read(reader)).map(r => r.visits), [1, 1, 0, 0, 0, 0]);
    await page.close();
    const resumed = await runPlan(reader, plan, { batchSize: 2 });
    assert.equal(resumed.ok, true, JSON.stringify(resumed));
    assert.ok((await read(reader)).every(r => r.visits === 1));
    await reader.close();
});

test('two tabs racing the same migration execute the transformation once', async ({ page, context }) => {
    const model = schema(); await seed(page, model, records.map(row => ({ ...row, visits: 0 })));
    const secondPage = await loadPage(context);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        module: 'export function transform(row) { return {...row, visits:row.visits+1}; }'
    });
    const results = await Promise.all([runPlan(page, plan), runPlan(secondPage, plan)]);
    for (const result of results) assert.equal(result.ok, true, JSON.stringify(result));
    assert.ok((await read(page)).every(r => r.visits === 1));
    await secondPage.close();
});

test('older application revision cannot reopen a migrated database', async ({ page }) => {
    const model = schema(); await seed(page); const plan = bundle(model, model, []);
    assert.equal((await runPlan(page, plan)).ok, true);
    expectFailure(await page.evaluate(model => tryEnsure({ name: 'test', schema: model }), model));
    assert.deepEqual(await read(page), records);
});

test('blocked native upgrade emits metadata and succeeds after the owner closes', async ({ page, context }) => {
    const before = schema(); const after = copy(before); after.stores[0].indexes.push(index('byName', 'name'));
    await seed(page, before); const blocker = await loadPage(context);
    await blocker.evaluate(async () => { window.heldDatabase = await openRaw('test'); });
    const plan = bundle(before, after, [{ op: 'createIndex', store: 'clients', index: index('byName', 'name') }]);
    await page.evaluate(options => { window.running = tryEnsure(options); }, { name: 'test', schema: after, manifestUrl: plan.url });
    await page.waitForFunction(() => events.some(event => event.phase === 'blocked'));
    await blocker.evaluate(() => heldDatabase.close());
    const result = await page.evaluate(() => window.running);
    assert.equal(result.ok, true, JSON.stringify(result));
    assert.deepEqual(await read(page), records);
    await blocker.close();
});

test('cancelling a blocked upgrade settles promptly and aborts a late opening', async ({ page, context }) => {
    const before = schema(); const after = copy(before); after.stores[0].indexes.push(index('byName', 'name'));
    const ready = await seed(page, before); const blocker = await loadPage(context);
    await blocker.evaluate(async () => { window.heldDatabase = await openRaw('test'); });
    const plan = bundle(before, after, [{ op: 'createIndex', store: 'clients', index: index('byName', 'name') }]);
    const result = await page.evaluate(async options => {
        const controller = new AbortController();
        try {
            await engine.ensureDatabase({ ...options, signal: controller.signal,
                onEvent(event) { if (event.phase === 'blocked') controller.abort(); } });
            return { ok: true };
        } catch (error) { return { ok: false, error: { code: error.code } }; }
    }, { name: 'test', schema: after, manifestUrl: plan.url });
    expectFailure(result);
    await blocker.evaluate(() => heldDatabase.close());
    // This open queues after the abandoned request and verifies its late upgrade
    // was aborted rather than applied after cancellation had been reported.
    await unchanged(page, ready);
    await blocker.close();
});

test('a retry queued behind a cancelled native upgrade remains cancellable', async ({ page, context }) => {
    const model = schema(); const ready = await seed(page, model);
    const plan = bundle(model, model, []);
    const blocker = await loadPage(context);
    await blocker.evaluate(async () => { window.heldDatabase = await openRaw('test'); });
    const options = { name: 'test', schema: model, manifestUrl: plan.url };
    const first = await page.evaluate(async options => {
        const controller = new AbortController();
        try {
            await engine.ensureDatabase({ ...options, signal: controller.signal,
                onEvent(event) { if (event.phase === 'blocked') controller.abort(); } });
        } catch (error) { return error.code; }
    }, options);
    assert.equal(first, 'MAGDB_CANCELLED');
    await page.evaluate(options => {
        window.retryController = new AbortController();
        window.retryOpening = false;
        const open = indexedDB.open;
        indexedDB.open = function (...args) {
            window.retryOpening = true;
            return open.apply(this, args);
        };
        window.retryPending = tryEnsure({ ...options, signal: retryController.signal })
            .finally(() => { indexedDB.open = open; });
    }, options);
    await page.waitForFunction(() => window.retryOpening);
    const retry = await page.evaluate(() => {
        retryController.abort();
        return new Promise(resolve => {
            const timeout = setTimeout(() => resolve(null), 2_000);
            retryPending.then(result => { clearTimeout(timeout); resolve(result); });
        });
    });
    await blocker.evaluate(() => heldDatabase.close());
    assert.equal(retry?.error?.code, 'MAGDB_CANCELLED', 'Cancellation waited for a different tab to release its connection.');
    await unchanged(page, ready);
    assert.equal((await runPlan(page, plan)).ok, true);
});

test('observer exceptions cannot fail a committed migration', async ({ page }) => {
    const result = await page.evaluate(async model => {
        const remove = engine.subscribeMigrationEvents(() => { throw new Error('Observer fault'); });
        try {
            return await engine.ensureDatabase({ name: 'test', schema: model,
                onEvent() { throw new Error('Per-call observer fault'); } });
        } finally { remove(); }
    }, schema());
    assert.equal(result.revision, 1);
});

test('events exclude record values and custom exception contents', async ({ page }) => {
    const secret = 'PRIVATE_RECORD_CONTENT_7f1'; const model = schema();
    await seed(page, model, [{ id: 1, name: secret }]);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        module: 'export function transform(row) { throw new Error(row.name); }'
    });
    expectFailure(await runPlan(page, plan));
    const eventText = await page.evaluate(() => JSON.stringify(events));
    assert.ok(!eventText.includes(secret), 'Telemetry leaked record contents through a transform error.');
    const phases = await page.evaluate(() => events.map(e => e.phase));
    assert.ok(phases.includes('failed'));
});

test('pre-aborted request does not create a database', async ({ page }) => {
    const result = await page.evaluate(async model => {
        const controller = new AbortController(); controller.abort();
        try { await engine.ensureDatabase({ name: 'test', schema: model, signal: controller.signal }); return { ok: true }; }
        catch { return { ok: false }; }
    }, schema());
    assert.equal(result.ok, false); assert.equal(await inspect(page), null);
});

test('cancellation after a committed checkpoint leaves resumable state', async ({ page }) => {
    const model = schema(); const original = Array.from({ length: 6 }, (_, i) => ({ id: i + 1, name: 'Client', visits: 0 }));
    await seed(page, model, original);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export function transform(row) { return {...row, visits:row.visits+1}; }'
    });
    for (const expected of [[1, 1, 0, 0, 0, 0], [1, 1, 1, 1, 0, 0]]) {
        const interrupted = await page.evaluate(async options => {
            const controller = new AbortController();
            try {
                await engine.ensureDatabase({ ...options, signal: controller.signal,
                    onEvent(event) { if (event.phase === 'checkpoint') controller.abort(); } });
                return { ok: true };
            } catch { return { ok: false }; }
        }, { name: 'test', schema: model, manifestUrl: plan.url, batchSize: 2 });
        assert.equal(interrupted.ok, false);
        assert.deepEqual((await read(page)).map(row => row.visits), expected);
    }
    const resumed = await runPlan(page, plan, { batchSize: 2 });
    assert.equal(resumed.ok, true, JSON.stringify(resumed));
    assert.ok((await read(page)).every(r => r.visits === 1));
});

test('cancelling a pending asynchronous hook releases ownership without a late commit', async ({ page }) => {
    const model = schema(); await seed(page, model);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export async function transform(row, context) { if (globalThis.holdCustomHook) { globalThis.hookSignal = context.signal; await new Promise(resolve => { globalThis.releaseCustomHook = resolve; }); } return {...row, visits:(row.visits ?? 0)+1}; }'
    });
    await page.evaluate(options => {
        window.holdCustomHook = true; window.controller = new AbortController();
        window.running = tryEnsure({ ...options, signal: controller.signal });
    }, { name: 'test', schema: model, manifestUrl: plan.url, batchSize: 1 });
    await page.waitForFunction(() => typeof window.releaseCustomHook === 'function');
    const aborted = await page.evaluate(async () => {
        controller.abort(); const result = await running;
        return { result, hookSignalAborted: hookSignal.aborted };
    });
    expectFailure(aborted.result);
    assert.equal(aborted.hookSignalAborted, true);
    assert.deepEqual(await read(page), records);
    // A new attempt gets ownership before the original hook has settled.
    await page.evaluate(() => { window.holdCustomHook = false; });
    assert.equal((await runPlan(page, plan, { batchSize: 1 })).ok, true);
    await page.evaluate(() => { releaseCustomHook(); });
    assert.ok((await read(page)).every(row => row.visits === 1));
});

test('cancelling pending custom module initialization leaves the old database intact', async ({ page }) => {
    const ready = await seed(page); const model = schema();
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        module: 'globalThis.initializingCustomModule=true; await new Promise(()=>{}); export function transform(row) { return row; }'
    });
    await page.evaluate(options => {
        window.controller = new AbortController(); window.running = tryEnsure({ ...options, signal: controller.signal });
    }, { name: 'test', schema: model, manifestUrl: plan.url });
    await page.waitForFunction(() => window.initializingCustomModule === true);
    const result = await page.evaluate(async () => { controller.abort(); return await running; });
    expectFailure(result); await unchanged(page, ready);
});

test('dedicated workers and tabs share migration ownership', async ({ page }) => {
    const model = schema(); await seed(page, model);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable',
        module: 'export function transform(row) { return {...row, visits:(row.visits ?? 0)+1}; }'
    });
    const workerUrl = plan.base + '/worker.js';
    assets.set(workerUrl, { type: 'text/javascript', body: `
        import { ensureDatabase } from '/engine/migrations/migrationEngine.js';
        self.onmessage = async ({ data }) => {
            try { self.postMessage({ ok: true, value: await ensureDatabase(data) }); }
            catch (error) { self.postMessage({ ok: false, code: error.code, message: error.message }); }
        };
    ` });
    const result = await page.evaluate(async ({ workerUrl, options }) => {
        const workers = [new Worker(workerUrl, { type: 'module' }), new Worker(workerUrl, { type: 'module' })];
        try {
            return await Promise.all([tryEnsure(options), ...workers.map(worker => new Promise((resolve, reject) => {
                worker.onmessage = event => resolve(event.data);
                worker.onerror = event => reject(new Error(event.message));
                worker.postMessage(options);
            }))]);
        } finally { workers.forEach(worker => worker.terminate()); }
    }, { workerUrl, options: { name: 'test', schema: model, manifestUrl: plan.url, batchSize: 1 } });
    assert.ok(result.every(item => item.ok), JSON.stringify(result));
    assert.deepEqual((await read(page)).map(row => row.visits), [1, 1]);
});

test('service worker cache must supply every needed asset and matching bytes', async ({ page }) => {
    const model = schema(); const ready = await seed(page, model);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        module: 'export function transform(row) { return {...row, visits:(row.visits ?? 0)+1}; }'
    });
    const manifest = plan.base + '/manifest.json';
    const hook = plan.base + '/migrations/transform.js';
    const serviceWorker = '/migration-offline-fixture.js';
    assets.set(serviceWorker, { type: 'text/javascript', body: `
        self.addEventListener('install', () => self.skipWaiting());
        self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
        self.addEventListener('fetch', event => {
            if (new URL(event.request.url).pathname.startsWith('/fixtures/')) {
                event.respondWith(caches.match(event.request).then(hit => hit ?? new Response(null, { status: 504 })));
            }
        });
    ` });
    await page.evaluate(async ({ serviceWorker, paths }) => {
        const cache = await caches.open('immutable-migration-fixture');
        await cache.addAll(paths);
        await navigator.serviceWorker.register(serviceWorker);
        if (!navigator.serviceWorker.controller) await new Promise(resolve =>
            navigator.serviceWorker.addEventListener('controllerchange', resolve, { once: true }));
    }, { serviceWorker, paths: [manifest, ...plan.manifest.schemas.map(asset => plan.base + '/' + asset.path),
        plan.base + '/' + plan.manifest.migrations[0].path] });
    const offline = await runPlan(page, plan);
    assert.equal(offline.error?.code, 'MAGDB_ASSET_UNAVAILABLE');
    assert.equal(offline.error.retryable, true);
    await unchanged(page, ready);
    await page.evaluate(hook => caches.open('immutable-migration-fixture').then(cache =>
        cache.put(hook, new Response('export function transform(row) { return row; }'))), hook);
    assert.equal((await runPlan(page, plan)).error?.code, 'MAGDB_ASSET_HASH_MISMATCH');
    await unchanged(page, ready);
    await page.evaluate(({ hook, bytes }) => caches.open('immutable-migration-fixture').then(cache =>
        cache.put(hook, new Response(bytes))), { hook, bytes: assets.get(hook).body });
    assert.equal((await runPlan(page, plan)).ok, true);
    assert.deepEqual((await read(page)).map(row => row.visits), [1, 1]);
});

test('a network failure is reported as an unavailable deployment asset', async ({ page }) => {
    const model = schema(); const ready = await seed(page, model);
    const plan = bundle(model, model, []);
    const schemaUrl = plan.base + '/' + plan.manifest.schemas[1].path;
    await page.route(url => url.pathname === schemaUrl, route => route.abort('failed'));
    const result = await runPlan(page, plan);
    assert.equal(result.error?.code, 'MAGDB_ASSET_UNAVAILABLE');
    assert.equal(result.error.retryable, true);
    await unchanged(page, ready);
});

test('invalid manifest JSON rejects before creating or mutating a database', async ({ page }) => {
    const model = schema(); const ready = await seed(page, model);
    const plan = bundle(model, model, []);
    assets.set(new URL(plan.url).pathname, { type: 'application/json', body: '<truncated manifest' });
    const result = await runPlan(page, plan);
    assert.equal(result.error?.code, 'MAGDB_ASSET_INVALID');
    assert.equal(result.error.retryable, false);
    await unchanged(page, ready);
});

test('quota error aborts a resumable batch and preserves its previous checkpoint', async ({ page }) => {
    const model = schema(); const original = [1, 2, 3, 4].map(id => ({ id, name: 'Customer', visits: 0 }));
    await seed(page, model, original);
    const plan = bundle(model, model, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export function transform(row) { return {...row, visits:row.visits+1}; }'
    });
    const result = await page.evaluate(async options => {
        const put = IDBObjectStore.prototype.put;
        IDBObjectStore.prototype.put = function (row, ...keys) {
            if (this.name === 'clients' && row.id === 4) throw new DOMException('Synthetic quota fault', 'QuotaExceededError');
            return put.call(this, row, ...keys);
        };
        try { return await tryEnsure(options); }
        finally { IDBObjectStore.prototype.put = put; }
    }, { name: 'test', schema: model, manifestUrl: plan.url, batchSize: 2 });
    assert.equal(result.error?.code, 'MAGDB_QUOTA_EXCEEDED');
    assert.equal(result.error.retryable, true);
    assert.equal((await inspect(page)).state.active.processedRecords, 2);
    assert.deepEqual((await read(page)).map(row => row.visits), [1, 1, 0, 0]);
    assert.equal((await runPlan(page, plan, { batchSize: 2 })).ok, true);
    assert.deepEqual((await read(page)).map(row => row.visits), [1, 1, 1, 1]);
});

function recoveryPackage(plan, body = repairModule, edit = () => {}) {
    const edge = plan.manifest.migrations[0];
    const document = {
        protocolVersion: 2, id: 'fix-original', schemaId: plan.before.schemaId,
        source: { revision: edge.fromRevision, schemaHash: plan.migration.fromSchemaHash,
            migrationId: edge.id, migrationHash: edge.sha256 },
        target: { revision: edge.toRevision, schemaHash: plan.migration.toSchemaHash },
        operation: { index: 0, store: 'clients' },
        module: { path: 'repair.mjs', sha256: digest(body) }
    };
    edit(document);
    const location = plan.base + '/recoveries/recovery.json';
    assets.set(plan.base + '/recoveries/repair.mjs', { body, type: 'text/javascript' });
    const bytes = canonical(document);
    assets.set(location, { body: bytes, type: 'application/json' });
    const asset = { sha256: digest(bytes) };
    plan.manifest.protocolVersion = 2;
    plan.manifest.requiredCapabilities = [...new Set([...plan.manifest.requiredCapabilities, 'resumableRepairV1'])];
    plan.manifest.recoveries = [{ id: document.id, fromRevision: 1, path: 'recoveries/recovery.json', sha256: asset.sha256 }];
    jsonAsset(plan.base + '/manifest.json', plan.manifest);
    return { id: document.id, sha256: asset.sha256 };
}

const repairModule = `
export function repairProcessed(row) {
    if (row.value !== 11) throw new Error(row.name);
    return { ...row, value: row.value - 1, repairs: (row.repairs ?? 0) + 1 };
}
export function repairUnprocessed(row) {
    if (row.value !== 1) throw new Error(row.name);
    return { ...row, value: row.value + 9, repairs: (row.repairs ?? 0) + 1 };
}
export function verify(row) { return row.value === 10 && row.repairs === 1; }
`;
const recoveryRows = [1, 2, 3].map(id => ({ id, name: 'PRIVATE_RECOVERY_FIXTURE', value: 1 }));
const makeInterrupted = (model, failId = 2) => bundle(model, model, [{ op: 'transform', store: 'clients' }], {
    mode: 'resumable', module: `export function transform(row) {
        if (row.id === ${failId} && !globalThis.allowOriginal) throw new Error(row.name);
        return {...row, value: 11};
    }`
});
const recoveryOptions = (plan, recovery, extra = {}) =>
    ({ name: 'test', schema: plan.after, manifestUrl: plan.url, recovery, batchSize: 1, ...extra });
const repair = (page, plan, recovery, extra) => page.evaluate(options => tryEnsure(options, true), recoveryOptions(plan, recovery, extra));

for (const [label, failId, processed] of [['empty', 1, 0], ['intermediate', 2, 1], ['final', 0, 3]]) {
    test(`recovery repairs the ${label} original boundary and preserves v2 lineage`, async ({ page }) => {
        const model = schema(); await seed(page, model, recoveryRows);
        let plan = makeInterrupted(model, failId);
        const first = failId === 0 ? await page.evaluate(async options => {
            const controller = new AbortController();
            let checkpoints = 0;
            try {
                await engine.ensureDatabase({ ...options, signal: controller.signal,
                    onEvent: event => { if (event.phase === 'checkpoint' && ++checkpoints === 4) controller.abort(); } });
            } catch (error) { return { ok: false, error: { code: error.code } }; }
        }, recoveryOptions(plan, null)) : await runPlan(page, plan, { batchSize: 1 });
        expectFailure(first);
        assert.equal(first.error.code, failId === 0 ? 'MAGDB_CANCELLED' : 'MAGDB_HOOK_FAILED');
        const original = await inspect(page);
        assert.equal(original.state.active.processedRecords, processed);
        assert.deepEqual((await read(page)).map(row => row.value), recoveryRows.map((_, index) => index < processed ? 11 : 1));
        const approved = recoveryPackage(plan);
        const originalDigest = plan.manifest.migrations[0].sha256;
        plan = appendRevision(plan, model, [{ op: 'setMissing', store: 'clients', path: 'afterRecovery', value: true }],
            label === 'final' ? 'resumable' : 'atomic');
        const result = await repair(page, plan, approved);
        assert.deepEqual(result.value, { outcome: 'recovered', recoveryId: approved.id, revision: 2, applicationTargetRevision: 3 });
        assert.equal((await inspect(page)).nativeVersion, original.nativeVersion + 1);
        assert.deepEqual(await read(page), recoveryRows.map(row => ({ ...row, value: 10, repairs: 1 })));
        const recovered = { id: plan.migration.id, sha256: originalDigest, outcome: 'recovered', recovery: approved };
        assert.deepEqual((await inspect(page)).state.history, [recovered]);
        assert.equal((await repair(page, plan, approved)).value?.outcome, 'alreadyRecovered');
        assert.equal((await runPlan(page, plan)).value?.revision, 3);
        const advanced = await inspect(page);
        assert.equal(advanced.state.protocolVersion, 2);
        assert.deepEqual(advanced.state.history[0], recovered);
        assert.deepEqual(await read(page), recoveryRows.map(row => ({ ...row, value: 10, repairs: 1, afterRecovery: true })));
        const withoutBinding = copy(plan.manifest);
        withoutBinding.protocolVersion = 1;
        withoutBinding.requiredCapabilities = [];
        delete withoutBinding.recoveries;
        withoutBinding.schemas = withoutBinding.schemas.map(asset => ({ ...asset, path: plan.base + '/' + asset.path }));
        const failure = await runPlan(page, plan, { manifestUrl: undefined, manifest: withoutBinding });
        assert.equal(failure.error?.code, 'MAGDB_RECOVERY_BINDING_REQUIRED');
        if (process.env.MAGIC_V1_ENGINE) {
            assert.equal(await page.evaluate(async options => {
                const legacy = await import('/v1/migrationEngine.js');
                try { await legacy.ensureDatabase(options); }
                catch (error) { return error.code; }
            }, { name: 'test', schema: model, manifest: withoutBinding }),
            'MAGDB_STATE_INVALID');
        }
    });
}

test('compound keys and lossless policy survive recovery without rewriting the original edge', async ({ page }) => {
    const model = schema();
    model.numericFormat = 'lossless-v1';
    model.stores[0].keyPath = ['region', 'id'];
    const rows = recoveryRows.map((row, i) => ({ ...row, region: i < 2 ? 'a' : 'b', exact: '~ml1:09232379236109516801' }));
    await seed(page, model, rows);
    const plan = makeInterrupted(model);
    assert.equal((await runPlan(page, plan, { batchSize: 1 })).error?.code, 'MAGDB_HOOK_FAILED');
    const previous = await inspect(page);
    const approved = recoveryPackage(plan);
    assert.equal((await repair(page, plan, approved)).value?.outcome, 'recovered');
    const final = await inspect(page);
    assert.deepEqual(final.stores, previous.stores);
    assert.deepEqual(final.numericPolicy, previous.numericPolicy);
    assert.deepEqual(await read(page), rows.map(row => ({ ...row, value: 10, repairs: 1 })));
});

test('out-of-line date and binary compound keys survive native recovery checkpoints', async ({ page }) => {
    const model = schema();
    model.stores[0].keyPath = null;
    const preserveKeys = await page.evaluate(async ({ model, rows }) => {
        await engine.ensureDatabase({ name: 'test', schema: model });
        const db = await openRaw('test');
        try {
            const tx = db.transaction('clients', 'readwrite');
            const finished = complete(tx);
            rows.forEach((row, index) => tx.objectStore('clients').add(row, [new Date(10), Uint8Array.of(index)]));
            await finished;
            return true;
        } finally { db.close(); }
    }, { model, rows: recoveryRows });
    assert.equal(preserveKeys, true);
    const plan = makeInterrupted(model);
    assert.equal((await runPlan(page, plan, { batchSize: 1 })).error?.code, 'MAGDB_HOOK_FAILED');
    assert.equal((await repair(page, plan, recoveryPackage(plan))).value?.outcome, 'recovered');
    const keysMatch = await page.evaluate(async () => {
        const db = await openRaw('test');
        try {
            const keys = await request(db.transaction('clients').objectStore('clients').getAllKeys());
            return keys.length === 3 && keys.every((key, index) => indexedDB.cmp(key, [new Date(10), Uint8Array.of(index)]) === 0);
        } finally { db.close(); }
    });
    assert.equal(keysMatch, true);
    assert.deepEqual(await read(page), recoveryRows.map(row => ({ ...row, value: 10, repairs: 1 })));
});

test('recovery rejects a multi-operation edge and prevents primary key edits', async ({ page }) => {
    const model = schema();
    await seed(page, model, recoveryRows, 'multi');
    const module = 'export function transform(row) { if (row.id === 2) throw Error(); return {...row,value:11}; }';
    const multi = bundle(model, model, [{ op: 'transform', store: 'clients' },
        { op: 'setMissing', store: 'clients', path: 'later', value: true }], { mode: 'resumable', module });
    expectFailure(await runPlan(page, multi, { name: 'multi', batchSize: 1 }));
    const before = await inspect(page, 'multi');
    assert.equal((await repair(page, multi, recoveryPackage(multi), { name: 'multi' })).error?.code, 'MAGDB_RECOVERY_UNSUPPORTED');
    assert.deepEqual(await inspect(page, 'multi'), before);

    await seed(page, model, recoveryRows);
    const single = makeInterrupted(model);
    expectFailure(await runPlan(page, single, { batchSize: 1 }));
    const partial = await read(page);
    const approval = recoveryPackage(single, repairModule.replace('value: row.value - 1', 'id: 900, value: row.value - 1'));
    assert.equal((await repair(page, single, approval)).error?.code, 'MAGDB_KEY_CHANGE_UNSUPPORTED');
    assert.equal((await inspect(page)).state.recovery.repaired, 0);
    assert.deepEqual(await read(page), partial);
});

test('explicit recovery never reruns a genuinely completed original edge or claims bootstrap did', async ({ page }) => {
    const model = schema(); await seed(page, model, recoveryRows);
    let plan = makeInterrupted(model);
    await page.evaluate(() => { globalThis.allowOriginal = true; });
    assert.equal((await runPlan(page, plan)).value?.revision, 2);
    const previous = await inspect(page);
    const approved = recoveryPackage(plan, 'throw new Error("A no-op must not import application code");');
    assert.equal((await repair(page, plan, approved)).value?.outcome, 'alreadyCompletedOriginal');
    assert.deepEqual(await inspect(page), previous);
    plan = appendRevision(plan, model, [{ op: 'setMissing', store: 'clients', path: 'afterOriginal', value: true }]);
    assert.equal((await runPlan(page, plan)).value?.revision, 3);
    assert.equal((await inspect(page)).state.protocolVersion, 1);
    assert.deepEqual(await read(page), recoveryRows.map(row => ({ ...row, value: 11, afterOriginal: true })));
    assert.equal((await runPlan(page, plan, { name: 'bootstrap' })).value?.revision, 3);
    assert.equal((await repair(page, plan, approved, { name: 'bootstrap' })).error?.code, 'MAGDB_RECOVERY_NOT_APPLICABLE');
    const pending = appendRevision(plan, model,
        [{ op: 'setMissing', store: 'clients', path: 'unrelatedStep', value: true }], 'resumable');
    const interrupted = await page.evaluate(async options => {
        const controller = new AbortController();
        try {
            await engine.ensureDatabase({ ...options, signal: controller.signal,
                onEvent: event => { if (event.phase === 'checkpoint') controller.abort(); } });
        } catch (error) { return error.code; }
    }, recoveryOptions(pending, approved));
    assert.equal(interrupted, 'MAGDB_CANCELLED');
    const later = await inspect(page);
    assert.equal(later.state.status, 'migrating');
    assert.equal((await repair(page, pending, approved)).value?.outcome, 'alreadyCompletedOriginal');
    assert.deepEqual(await inspect(page), later);
});

test('cancel during recovery before writes and after checkpoints without replay', async ({ page, context }) => {
    const model = schema(); await seed(page, model, recoveryRows);
    const plan = makeInterrupted(model);
    expectFailure(await runPlan(page, plan, { batchSize: 1 }));
    const approved = recoveryPackage(plan, repairModule.replace('export function repairProcessed(row) {',
        'export async function repairProcessed(row) { if (globalThis.pauseRepair) await new Promise(resolve => { globalThis.resumeHook = resolve; });'));
    const options = recoveryOptions(plan, approved);
    await page.evaluate(options => {
        window.pauseRepair = true;
        window.cancel = new AbortController();
        window.pending = tryEnsure({ ...options, signal: cancel.signal }, true);
    }, options);
    await page.waitForFunction(() => typeof window.resumeHook === 'function');
    assert.equal((await inspect(page)).state.recovery.repaired, 0);
    const cancelled = await page.evaluate(async () => { cancel.abort(); const result = await pending; resumeHook(); return result; });
    assert.equal(cancelled.error?.code, 'MAGDB_CANCELLED');
    assert.equal((await runPlan(page, plan)).error?.code, 'MAGDB_RECOVERY_REQUIRED');
    const originalAssets = new Map([...assets].filter(([url]) => url.startsWith(plan.base)));
    const changed = recoveryPackage(plan, repairModule + '\n');
    assert.equal((await repair(page, plan, changed)).error?.code, 'MAGDB_RECOVERY_PACKAGE_REQUIRED');
    for (const [url, asset] of originalAssets) assets.set(url, asset);
    const reopened = await loadPage(context);
    const checkpoint = await reopened.evaluate(async options => {
        const controller = new AbortController();
        try {
            await engine.recoverDatabase({ ...options, signal: controller.signal,
                onEvent: event => { if (event.phase === 'recoveryCheckpoint' && event.processedRecords === 1) controller.abort(); } });
        } catch (error) { return error.code; }
    }, options);
    assert.equal(checkpoint, 'MAGDB_CANCELLED');
    assert.equal((await inspect(reopened)).state.recovery.repaired, 1);
    assert.deepEqual((await read(reopened)).map(row => row.repairs ?? 0), [1, 0, 0]);
    await reopened.close();
    assert.equal((await repair(page, plan, approved)).value?.outcome, 'recovered');
    assert.deepEqual((await read(page)).map(row => row.repairs), [1, 1, 1]);
});

for (const phase of ['repair', 'verify']) {
    test(`abrupt page termination during recovery ${phase} preserves committed progress`, async ({ page, context }) => {
        const model = schema();
        await seed(page, model, recoveryRows);
        let plan = makeInterrupted(model);
        assert.equal((await runPlan(page, plan, { batchSize: 1 })).error?.code, 'MAGDB_HOOK_FAILED');
        const pause = `if (globalThis.holdRecovery === '${phase}' && row.id === 2) {
            globalThis.recoveryHookPending = true;
            await new Promise(() => {});
        }`;
        const approved = recoveryPackage(plan, repairModule
            .replace('export function repairUnprocessed(row) {', `export async function repairUnprocessed(row) {
                ${phase === 'repair' ? pause : ''}`)
            .replace('export function verify(row) {', `export async function verify(row) {
                (globalThis.verificationCalls ??= []).push(row.id);
                ${phase === 'verify' ? pause : ''}`));
        plan = appendRevision(plan, model, [{ op: 'setMissing', store: 'clients', path: 'afterRecovery', value: true }]);
        await page.evaluate(({ options, phase }) => {
            window.holdRecovery = phase;
            window.pending = tryEnsure(options, true);
        }, { options: recoveryOptions(plan, approved), phase });
        await page.waitForFunction(() => window.recoveryHookPending === true);

        const reader = await loadPage(context);
        const checkpoint = await inspect(reader);
        const partial = await read(reader);
        assert.equal(checkpoint.state.status, 'recovering');
        assert.equal(checkpoint.state.protocolVersion, 2);
        assert.equal(checkpoint.state.revision, 1);
        assert.equal(checkpoint.state.active.processedRecords, 1);
        assert.deepEqual(checkpoint.state.history, []);
        assert.deepEqual(checkpoint.state.recovery, { ...approved, phase, repaired: phase === 'repair' ? 1 : 3,
            verified: phase === 'repair' ? 0 : 1, hasCheckpoint: true });
        assert.deepEqual(partial.map(row => row.repairs ?? 0), phase === 'repair' ? [1, 0, 0] : [1, 1, 1]);
        await page.close();

        assert.deepEqual(await inspect(reader), checkpoint);
        assert.deepEqual(await read(reader), partial);
        assert.equal((await runPlan(reader, plan)).error?.code, 'MAGDB_RECOVERY_REQUIRED');
        const resumed = await repair(reader, plan, approved);
        assert.deepEqual(resumed.value, { outcome: 'recovered', recoveryId: approved.id, revision: 2, applicationTargetRevision: 3 });
        assert.deepEqual(await reader.evaluate(() => verificationCalls), phase === 'repair' ? [1, 2, 3] : [2, 3]);
        const repaired = recoveryRows.map(row => ({ ...row, value: 10, repairs: 1 }));
        assert.deepEqual(await read(reader), repaired);
        const original = plan.manifest.migrations[0];
        const recovered = { id: original.id, sha256: original.sha256, outcome: 'recovered', recovery: approved };
        assert.deepEqual((await inspect(reader)).state.history, [recovered]);

        assert.equal((await runPlan(reader, plan)).value?.revision, 3);
        const final = (await inspect(reader)).state;
        assert.equal(final.status, 'ready');
        assert.equal(final.protocolVersion, 2);
        const later = plan.manifest.migrations[1];
        assert.deepEqual(final.history, [recovered, { id: later.id, sha256: later.sha256 }]);
        assert.deepEqual(await read(reader), repaired.map(row => ({ ...row, afterRecovery: true })));
    });
}

test('recovery rejects a changed record codec identity before admission', async ({ page }) => {
    const before = schema(), after = schema();
    before.stores[0].recordCodec = 'fixture-record-v1';
    after.stores[0].recordCodec = 'fixture-record-v2';
    await seed(page, before, recoveryRows);
    const plan = bundle(before, after, [{ op: 'transform', store: 'clients' }], {
        mode: 'resumable', module: 'export function transform(row) { if (row.id === 2) throw new Error("Synthetic fault"); return { ...row, value: 11 }; }'
    });
    assert.equal((await runPlan(page, plan, { batchSize: 1 })).error?.code, 'MAGDB_HOOK_FAILED');
    const checkpoint = await inspect(page), partial = await read(page);
    assert.equal(checkpoint.state.active.processedRecords, 1);
    const result = await repair(page, plan, recoveryPackage(plan));
    assert.equal(result.error?.code, 'MAGDB_RECOVERY_UNSUPPORTED');
    assert.match(result.error.message, /Recovery cannot change the store codec/);
    assert.deepEqual(await inspect(page), checkpoint);
    assert.deepEqual(await read(page), partial);
});

test('recovery quota and verifier errors retain checkpoints and redact private data', async ({ page }) => {
    const model = schema(); await seed(page, model, recoveryRows);
    const plan = makeInterrupted(model); expectFailure(await runPlan(page, plan, { batchSize: 1 }));
    const approved = recoveryPackage(plan, repairModule.replace('return row.value === 10',
        'if (row.id === 2 && !globalThis.verifyNow) throw new Error(row.name); return row.value === 10'));
    const quota = await page.evaluate(async options => {
        const put = IDBObjectStore.prototype.put;
        IDBObjectStore.prototype.put = function (row, ...keys) {
            if (this.name === 'clients' && row.id === 2) throw new DOMException('Injected quota', 'QuotaExceededError');
            return put.call(this, row, ...keys);
        };
        try { return await tryEnsure(options, true); } finally { IDBObjectStore.prototype.put = put; }
    }, recoveryOptions(plan, approved));
    assert.equal(quota.error?.code, 'MAGDB_QUOTA_EXCEEDED');
    assert.equal(quota.error.retryable, true);
    assert.equal((await inspect(page)).state.recovery.repaired, 1);
    assert.deepEqual((await read(page)).map(row => row.repairs ?? 0), [1, 0, 0]);
    const rejected = await page.evaluate(async options => {
        try { await engine.recoverDatabase(options); return { ok: true }; }
        catch (error) {
            return { error: { code: error.code, message: error.message, retryable: error.retryable, cause: error.cause } };
        }
    }, recoveryOptions(plan, approved));
    assert.equal(rejected.error?.code, 'MAGDB_RECOVERY_VERIFICATION_FAILED');
    assert.equal(rejected.error.cause, undefined);
    assert.equal(rejected.error.retryable, false);
    const snapshot = await inspect(page);
    assert.equal(snapshot.state.status, 'recovering');
    assert.equal(snapshot.state.recovery.verified, 1);
    assert.ok(!Object.hasOwn(snapshot.state.recovery, 'lastKey'));
    assert.ok(!JSON.stringify([snapshot, rejected, await page.evaluate(() => events)]).includes('PRIVATE_RECOVERY_FIXTURE'));
    assert.equal((await runPlan(page, plan)).error?.code, 'MAGDB_RECOVERY_REQUIRED');
    await page.evaluate(() => { globalThis.verifyNow = true; });
    assert.equal((await repair(page, plan, approved)).value?.outcome, 'recovered');
    assert.deepEqual((await read(page)).map(row => row.repairs), [1, 1, 1]);
});

test('wrong recovery bindings, tampered assets and unsupported shapes reject before mutation', async ({ page }) => {
    const model = schema(); await seed(page, model, recoveryRows);
    const plan = makeInterrupted(model); expectFailure(await runPlan(page, plan, { batchSize: 1 }));
    const before = await inspect(page), partial = await read(page);
    let approved = recoveryPackage(plan);
    for (const wrong of [{ ...approved, id: 'wrong' }, { ...approved, sha256: digest('wrong') }])
        assert.equal((await repair(page, plan, wrong)).error?.code, 'MAGDB_RECOVERY_MISMATCH');
    for (const change of [doc => doc.schemaId = 'wrong', doc => doc.source.migrationHash = digest('wrong'),
        doc => doc.source.migrationId = 'wrong', doc => doc.target.schemaHash = digest('wrong'),
        doc => doc.operation.index = 1])
        assert.equal((await repair(page, plan, recoveryPackage(plan, repairModule, change))).error?.code, 'MAGDB_RECOVERY_MISMATCH');
    approved = recoveryPackage(plan, repairModule, doc => doc.operation.store = 'other');
    assert.equal((await repair(page, plan, approved)).error?.code, 'MAGDB_RECOVERY_UNSUPPORTED');
    approved = recoveryPackage(plan);
    const modulePath = plan.base + '/recoveries/repair.mjs';
    const module = assets.get(modulePath);
    assets.delete(modulePath);
    assert.equal((await repair(page, plan, approved)).error?.code, 'MAGDB_ASSET_UNAVAILABLE');
    assets.set(modulePath, { ...module, body: module.body + ' ' });
    assert.equal((await repair(page, plan, approved)).error?.code, 'MAGDB_ASSET_HASH_MISMATCH');
    assert.deepEqual(await inspect(page), before);
    await unchanged(page, before, partial);
});

test('tabs and a worker serialize explicit recovery and preserve the original attempt', async ({ page, context }) => {
    const model = schema(); await seed(page, model, recoveryRows);
    const plan = makeInterrupted(model); expectFailure(await runPlan(page, plan, { batchSize: 1 }));
    const approved = recoveryPackage(plan);
    const tab = await loadPage(context);
    const url = plan.base + '/recover-worker.js';
    assets.set(url, { type: 'text/javascript', body: `
        import {recoverDatabase} from '/engine/migrations/migrationEngine.js';
        self.onmessage = async event => {
            try { self.postMessage(await recoverDatabase(event.data)); }
            catch (error) { self.postMessage({ code: error.code }); }
        };` });
    const worker = page.evaluate(({ url, options }) => new Promise((resolve, reject) => {
        const worker = new Worker(url, { type: 'module' });
        worker.onmessage = event => { worker.terminate(); resolve(event.data); };
        worker.onerror = error => { worker.terminate(); reject(new Error(error.message)); };
        worker.postMessage(options);
    }), { url, options: recoveryOptions(plan, approved) });
    const results = await Promise.all([repair(page, plan, approved), repair(tab, plan, approved), worker]);
    assert.deepEqual(results.map(result => result.value?.outcome ?? result.outcome).sort(),
        ['alreadyRecovered', 'alreadyRecovered', 'recovered']);
    assert.deepEqual((await read(page)).map(row => row.repairs), [1, 1, 1]);
});

if (cliFixture) test('installed CLI output boots and upgrades through the browser protocol', async ({ page }) => {
    const prefix = '/cli/';
    const manifestFile = cliFixture;
    const manifest = cliManifest;
    const first = JSON.parse(await readFile(path.resolve(path.dirname(manifestFile), manifest.schemas.find(s => s.revision === 1).path), 'utf8'));
    const target = JSON.parse(await readFile(path.resolve(path.dirname(manifestFile), manifest.schemas.find(s => s.revision === 2).path), 'utf8'));
    const ready = await page.evaluate(model => engine.ensureDatabase({ name: 'cli-package', schema: model }), first);
    assert.equal(ready.revision, 1);
    const result = await page.evaluate(options => engine.ensureDatabase(options), {
        name: 'cli-package', schema: target, manifestUrl: origin + prefix + path.basename(manifestFile)
    });
    assert.equal(result.revision, 2);
});

if (cliFixture) for (const [codecCase, sourceHasCodec] of [['absent-to-null', false], ['null-to-absent', true]]) {
    test(`installed CLI output recovers optional recordCodec ${codecCase}`, async ({ page }) => {
        const relative = `recovery-codecs/${codecCase}`;
        const directory = path.join(path.dirname(cliFixture), relative);
        const manifest = JSON.parse(await readFile(path.join(directory, 'manifest.json'), 'utf8'));
        const models = await Promise.all(manifest.schemas.map(async model =>
            JSON.parse(await readFile(path.join(directory, model.path), 'utf8'))));
        const [before, after] = models;
        assert.equal(Object.hasOwn(before.stores[0], 'recordCodec'), sourceHasCodec);
        assert.equal(Object.hasOwn(after.stores[0], 'recordCodec'), !sourceHasCodec);
        assert.equal(models[sourceHasCodec ? 0 : 1].stores[0].recordCodec, null);
        await seed(page, before, recoveryRows);
        const plan = { after, url: origin + '/cli/' + relative + '/manifest.json' };
        assert.equal((await runPlan(page, plan, { batchSize: 1 })).error?.code, 'MAGDB_HOOK_FAILED');
        assert.equal((await inspect(page)).state.active.processedRecords, 1);
        assert.deepEqual((await read(page)).map(row => row.value), [11, 1, 1]);
        const { id, sha256 } = manifest.recoveries[0];
        const result = await repair(page, plan, { id, sha256 });
        assert.equal(result.value?.outcome, 'recovered', JSON.stringify(result));
        assert.deepEqual(await read(page), recoveryRows.map(row => ({ ...row, value: 10, repairs: 1 })));
        const ready = (await inspect(page)).state;
        assert.equal(ready.protocolVersion, 2);
        assert.deepEqual(ready.history, [{ id: manifest.migrations[0].id, sha256: manifest.migrations[0].sha256,
            outcome: 'recovered', recovery: { id, sha256 } }]);
        assert.equal((await runPlan(page, plan)).value?.revision, 2);
    });
}

test('rotation batch preserves a concurrent write and commits conversion with its checkpoint', async ({ page }) => {
    await page.goto(origin + '/');
    const result = await page.evaluate(async () => {
        const { readRotationBatch, commitRotationBatch } = await import('/engine/utilities/rotationBatch.js');
        const open = indexedDB.open('rotation-transaction-fixture');
        const request = operation => new Promise((resolve, reject) => {
            operation.onsuccess = () => resolve(operation.result);
            operation.onerror = () => reject(operation.error);
        });
        open.onupgradeneeded = () => {
            open.result.createObjectStore('documents', { keyPath: ['group', 'id'] });
            open.result.createObjectStore('metadata');
        };
        const db = await request(open);
        const put = async (store, value, key) => {
            const tx = db.transaction(store, 'readwrite');
            return await request(key === undefined ? tx.objectStore(store).put(value) : tx.objectStore(store).put(value, key));
        };
        const key = ['catalog', 1];
        const original = { group: 'catalog', id: 1, label: 'original', ciphertext: Uint8Array.of(1) };
        const patch = { ...original, label: 'concurrent' };
        await put('documents', original);
        await put('metadata', { phase: 0 }, 'progress');
        const [snapshot] = await readRotationBatch(db, 'documents', null, 1);
        await put('documents', patch);
        const progress = { store: 'metadata', key: 'progress', value: { phase: 0 } };
        const next = { ...progress, value: { phase: 1 } };
        const expect = { store: 'documents', key, value: snapshot.record };
        const write = { store: 'documents', key, value: { ...patch, ciphertext: Uint8Array.of(2) } };
        const stale = await commitRotationBatch(db, [expect, progress], [write, next], () => {});
        const afterStale = await request(db.transaction('metadata').objectStore('metadata').get('progress'));
        const fresh = await commitRotationBatch(db, [{ ...expect, value: patch }, progress], [write, next], () => {});
        const final = await request(db.transaction('metadata').objectStore('metadata').get('progress'));
        const [row] = await readRotationBatch(db, 'documents', null, 1);
        const remainder = await readRotationBatch(db, 'documents', key, 1);
        const unsupported = { ...row.record, foreign: new Map([['example', 'content']]) };
        await put('documents', unsupported);
        const [foreign] = await readRotationBatch(db, 'documents', null, 1);
        let unsupportedCode = null;
        try {
            await commitRotationBatch(db, [{ store: 'documents', key, value: foreign.record },
                { ...progress, value: { phase: 1 } }], [write, { ...next, value: { phase: 2 } }], () => {});
        } catch (error) { unsupportedCode = error.message.split(':')[0]; }
        const protectedProgress = await request(db.transaction('metadata').objectStore('metadata').get('progress'));
        const [retained] = await readRotationBatch(db, 'documents', null, 1);
        db.close();
        return { stale, fresh, afterStale, final, label: row.record.label, bytes: [...row.record.ciphertext], remainder,
            unsupportedCode, protectedProgress, retained: [...retained.record.foreign] };
    });
    assert.deepEqual(result, { stale: false, fresh: true, afterStale: { phase: 0 }, final: { phase: 1 },
        label: 'concurrent', bytes: [2], remainder: [], unsupportedCode: 'MAGIC_ROTATION_RECORD_UNSUPPORTED',
        protectedProgress: { phase: 1 }, retained: [['example', 'content']] });
});

test('rotation checkpoints preserve compound keys across protected and public stores', async ({ page }) => {
    await page.goto(origin + '/');
    const result = await page.evaluate(async () => {
        const { advanceRotationPass } = await import('/engine/utilities/rotationPass.js');
        const { sealRotationJournal, rotationJournalKey } = await import('/engine/utilities/rotationJournal.js');
        const { encryptionMetadataStore } = await import('/engine/utilities/encryptionPolicy.js');
        const { sealRecord, openRecord } = await import('/engine/utilities/encryptionRecord.js');
        const { importEncryptionKey } = await import('/engine/utilities/encryptionEnvelope.js');
        const generate = () => importEncryptionKey(crypto.getRandomValues(new Uint8Array(32)));
        const keys = new Map([['old', await generate()], ['new', await generate()]]);
        const open = indexedDB.open('rotation-checkpoint-fixture');
        const request = operation => new Promise((resolve, reject) => {
            operation.onsuccess = () => resolve(operation.result);
            operation.onerror = () => reject(operation.error);
        });
        open.onupgradeneeded = () => {
            open.result.createObjectStore('documents', { keyPath: ['group', 'id'] }).createIndex('byGroup', 'group');
            open.result.createObjectStore('archive', { keyPath: 'id' });
            open.result.createObjectStore('settings', { keyPath: 'id' });
            open.result.createObjectStore(encryptionMetadataStore);
        };
        const db = await request(open);
        const vault = { database: db.name, account: 'catalog-account' };
        const policy = { fields: new Set(['content']), exposed: null };
        const context = primaryKey => ({ ...vault, store: 'documents', primaryKey });
        const put = async (storeName, value, key) => {
            const store = db.transaction(storeName, 'readwrite').objectStore(storeName);
            await request(key === undefined ? store.put(value) : store.put(value, key));
        };
        const get = (storeName, key) => request(db.transaction(storeName).objectStore(storeName).get(key));
        const group = 'catalog';
        const documentKeys = ['9007199254740993', '9007199254740994'].map(id => [group, id]);
        const contents = documentKeys.map(([, id]) => ({ nested: [{ exact: id, decimal: '1.00000000000000000001' }] }));
        for (const [index, key] of documentKeys.entries())
            await put('documents', await sealRecord({ group, id: key[1], content: contents[index] }, policy, keys.get('old'), 'old', context(key)));
        await put('settings', { id: 1, label: 'Public configuration' });
        const assertCurrent = () => {};
        const document = await sealRotationJournal({
            version: 1, rotationId: 'rotation', sourceKeyId: 'old', targetKeyId: 'new', mode: 'exclusive',
            modelHash: 'model', policyFingerprint: 'policy', writeKeyId: 'new', priorKeyIds: ['old'], sequence: 0,
            phase: 'converting', storeIndex: 0, afterKey: null, converted: 0, verified: 0, paused: false
        }, keys, vault, assertCurrent);
        await put(encryptionMetadataStore, document, rotationJournalKey);
        const install = async state => put(encryptionMetadataStore, await sealRotationJournal(state, keys, vault, assertCurrent), rotationJournalKey);
        const step = async () => advanceRotationPass({ db, vault, keys, document: await get(encryptionMetadataStore, rotationJournalKey),
            policies: new Map([['archive', policy], ['documents', policy],
                ['settings', { fields: new Set(), exposed: null }]]), expectations: [], batchSize: 1, assertCurrent });
        await step(); // Skip the empty protected store.
        await step(); // Commit one record and its compound checkpoint.
        const mixed = await Promise.all(documentKeys.map(async key => (await get('documents', key)).content.keyId));
        const finishPass = async () => {
            for (let i = 0; i < 6; i++) if ((await step()).outcome === 'pass-complete') return;
            throw new Error('The bounded rotation pass did not finish.');
        };
        await finishPass();
        const converted = (await get(encryptionMetadataStore, rotationJournalKey)).state;
        // Exercise the verification scan against a persisted phase. The full
        // lifecycle coordinator installs its finalization fence at this boundary.
        await install({ ...converted, phase: 'verifying', storeIndex: 0 });
        await finishPass();
        const final = (await get(encryptionMetadataStore, rotationJournalKey)).state;
        const rows = [];
        for (const key of documentKeys)
            rows.push(await openRecord(await get('documents', key), policy, keys.get('new'), 'new', context(key)));
        let oldKeyRejected = false;
        try {
            const key = documentKeys[0];
            await openRecord(await get('documents', key), policy, keys.get('old'), 'new', context(key));
        } catch { oldKeyRejected = true; }
        const indexedKeys = await request(db.transaction('documents').objectStore('documents').index('byGroup').getAllKeys(group));
        const publicRow = await get('settings', 1);
        db.close();
        return { mixed, converted: final.converted, verified: final.verified, rows, indexedKeys, publicRow, oldKeyRejected };
    });
    const ids = ['9007199254740993', '9007199254740994'];
    assert.deepEqual(result, { mixed: ['new', 'old'], converted: 2, verified: 2,
        rows: ids.map(id => ({ group: 'catalog', id, content: { nested: [{ exact: id, decimal: '1.00000000000000000001' }] } })),
        indexedKeys: ids.map(id => ['catalog', id]), publicRow: { id: 1, label: 'Public configuration' }, oldKeyRejected: true });
});

registerProtectedEngineTests({ test, schema, bundle, copy, inspect, read, loadPage, recoveryPackage,
    appendRevision, jsonAsset, assets, cliFixture, getOrigin: () => origin });
registerProtectedRegistrationTests({ test, schema, bundle, jsonAsset, copy, registrationStore, inspect, appendRevision, assets });
const expectedScenarioCount = (cliFixture ? 142 : 138) + (process.env.MAGIC_ROTATION_BENCHMARK === "1" ? 9 : 0);
assert.equal(tests.length, expectedScenarioCount, 'Unexpected migration scenario count.');

if (listOnly) {
    console.log(JSON.stringify(tests.map(scenario => scenario.name)));
    process.exit(0);
}
await access(playwrightEntry);
if (process.env.MAGIC_V1_ENGINE)
    await access(path.join(process.env.MAGIC_V1_ENGINE, 'migrationEngine.js'));
await mkdir(output, { recursive: true });
const playwright = await import(pathToFileURL(playwrightEntry).href);

const mime = file => file.endsWith('.json') ? 'application/json' : file.endsWith('.html') ? 'text/html' : 'text/javascript';
const server = createServer(async (request, response) => {
    const pathname = new URL(request.url, 'http://127.0.0.1').pathname;
    requests.push(pathname);
    response.setHeader('Cache-Control', 'no-store');
    try {
        if (pathname === '/') {
            response.setHeader('Content-Type', 'text/html');
            response.end('<!doctype html><meta charset="utf-8"><title>Magic migration contract tests</title>');
            return;
        }
        const fixture = assets.get(pathname);
        if (fixture) {
            response.setHeader('Content-Type', fixture.type);
            response.end(fixture.body); return;
        }
        let directory; let relative;
        if (pathname.startsWith('/engine/')) {
            directory = path.join(root, 'Magic.IndexedDb/wwwroot'); relative = pathname.slice('/engine/'.length);
        } else if (pathname.startsWith('/v1/') && process.env.MAGIC_V1_ENGINE) {
            directory = path.resolve(process.env.MAGIC_V1_ENGINE); relative = pathname.slice('/v1/'.length);
        } else if (pathname.startsWith('/cli/') && cliFixture) {
            directory = path.dirname(cliFixture); relative = pathname.slice('/cli/'.length);
        } else { response.writeHead(404); response.end('Fixture not found'); return; }
        const file = path.resolve(directory, relative);
        if (!file.startsWith(directory + path.sep)) { response.writeHead(403); response.end(); return; }
        response.setHeader('Content-Type', mime(file)); response.end(await readFile(file));
    } catch { response.writeHead(404); response.end('File not found'); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
origin = `http://127.0.0.1:${server.address().port}`;
let browser;
const report = { browser: browserName, v1Compatibility: Boolean(process.env.MAGIC_V1_ENGINE), startedAt: new Date().toISOString(), scenarios: [] };
try {
    browser = await playwright[browserName].launch({ headless: true,
        ...(process.env.MAGIC_BROWSER_EXECUTABLE ? { executablePath: process.env.MAGIC_BROWSER_EXECUTABLE } : {}) });
    report.browserVersion = browser.version();
    for (const scenario of tests) {
        const context = await browser.newContext();
        const consoleLog = []; const pageErrors = [];
        context.on('page', page => {
            page.on('console', message => consoleLog.push(`${message.type()}: ${message.text()}`));
            page.on('pageerror', error => pageErrors.push(error.stack ?? String(error)));
        });
        const started = performance.now(); let page;
        const slug = scenario.name.replace(/[^a-z0-9]+/gi, '-').toLowerCase();
        try {
            page = await loadPage(context);
            // A hard per-scenario deadline means a blocked upgrade cannot hang CI.
            let timer;
            try {
                await Promise.race([scenario.run({ page, context }), new Promise((_, reject) => {
                    const timeout = scenario.name.startsWith('rotation benchmark:') ? 900_000 : 35_000;
                    timer = setTimeout(() => reject(new Error(`Scenario exceeded ${timeout / 1000} seconds.`)), timeout);
                })]);
            } finally { clearTimeout(timer); }
            if (pageErrors.length) throw new Error('Unhandled browser error:\n' + pageErrors.join('\n'));
            report.scenarios.push({ name: scenario.name, state: 'passed', milliseconds: Math.round(performance.now() - started) });
            console.log(`PASS ${scenario.name}`);
        } catch (error) {
            report.scenarios.push({ name: scenario.name, state: 'failed', milliseconds: Math.round(performance.now() - started), error: error.stack ?? String(error) });
            console.error(`FAIL ${scenario.name}\n${error.stack ?? error}`);
            await writeFile(path.join(output, slug + '.log'), [error.stack ?? error, ...consoleLog, ...pageErrors].join('\n'));
            if (page && !page.isClosed()) await page.screenshot({ path: path.join(output, slug + '.png') }).catch(() => {});
        } finally { await context.close(); }
    }
} catch (error) {
    report.infrastructureError = error.stack ?? String(error);
    console.error(report.infrastructureError);
} finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
    report.finishedAt = new Date().toISOString();
    report.expected = expectedScenarioCount;
    report.executed = report.scenarios.length;
    report.skipped = 0;
    report.passed = report.scenarios.filter(s => s.state === 'passed').length;
    report.failed = report.scenarios.filter(s => s.state === 'failed').length;
    await writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n');
    const escaped = text => String(text).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
    const cases = report.scenarios.map(s => `<testcase name="${escaped(s.name)}" classname="Magic.Migrations.${browserName}" time="${s.milliseconds / 1000}">` +
        (s.state === 'failed' ? `<failure message="Migration scenario failed">${escaped(s.error)}</failure>` : '') + '</testcase>').join('\n');
    await writeFile(path.join(output, 'results.xml'), `<?xml version="1.0" encoding="UTF-8"?>\n<testsuite name="Magic migration protocol" tests="${report.scenarios.length}" failures="${report.failed}" errors="${report.infrastructureError ? 1 : 0}">\n${cases}\n</testsuite>\n`);
    if (report.infrastructureError || report.failed || report.passed !== tests.length || tests.length < 40) process.exitCode = 1;
    console.log(`Migration browser results: ${report.passed}/${tests.length} passed (${browserName}).`);
}
