// Run the public JavaScript migration protocol in real browser storage. No .NET
// runtime, DOM framework, fake IndexedDB implementation, or backend is involved.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { access, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

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

function appendRevision(plan, after, operations) {
    const revision = plan.manifest.targetRevision + 1;
    const schemaPath = `schemas/${String(revision).padStart(4, '0')}.json`;
    const schemaAsset = jsonAsset(`${plan.base}/${schemaPath}`, after);
    const id = `${String(revision).padStart(4, '0')}_Next`;
    const migrationPath = `migrations/${id}.json`;
    const document = { protocolVersion: 1, schemaId: after.schemaId, id,
        fromRevision: revision - 1, toRevision: revision, fromSchemaHash: fingerprint(plan.after),
        toSchemaHash: fingerprint(after), mode: 'atomic', requiredCapabilities: [], operations };
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
        window.tryEnsure = async options => {
            try {
                const value = await engine.ensureDatabase({ ...options, onEvent: event => events.push(event) });
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

test('protected registration rejects a migration manifest before provisioning storage', async ({ page }) => {
    const physical = '__magic_protected__:new-vault';
    const configuration = { name: physical + ':account', encryption: { account: 'fixture', physicalName: physical },
        storeSchemas: [{ ...registrationStore(), encryptedFields: ['secret'] }],
        migrationManifestUrl: origin + '/unsupported-protected/manifest.json' };
    const beforeRequests = requests.length;
    const result = await page.evaluate(async configuration => {
        const api = await import('/engine/magicDB.js');
        try { await api.createDb(configuration); return { ok: true }; }
        catch (error) { return { ok: false, error: { code: error.code } }; }
    }, configuration);
    expectFailure(result);
    assert.equal(result.error.code, 'MAGDB_PROTECTED_MIGRATION_UNSUPPORTED');
    assert.equal(await inspect(page, physical), null);
    assert.ok(!requests.slice(beforeRequests).includes('/unsupported-protected/manifest.json'));
});

test('rejected protected manifests preserve authenticated CRUD and ciphertext', async ({ page }) => {
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
    assert.equal(result.code, 'MAGDB_PROTECTED_MIGRATION_UNSUPPORTED');
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

if (listOnly) {
    console.log(JSON.stringify(tests.map(scenario => scenario.name)));
    process.exit(0);
}
await access(playwrightEntry);
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
const report = { browser: browserName, startedAt: new Date().toISOString(), scenarios: [] };
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
                    timer = setTimeout(() => reject(new Error('Scenario exceeded 35 seconds.')), 35_000);
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
    report.expected = tests.length;
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
