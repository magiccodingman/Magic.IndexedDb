import assert from 'node:assert/strict';
import { platform, release, arch, version as osVersion } from 'node:os';
import { execFileSync } from 'node:child_process';
import { createServer } from 'node:http';
import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { mkdir, readFile, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const directory = path.resolve(process.argv[2] ?? 'artifacts/released-upgrade');
const driver = path.resolve(process.argv[3] ?? 'tests/migrations/BrowserDriver/bin/Release/net10.0/.playwright/package/index.mjs');
const browserName = process.argv[4] ?? 'chromium';
const build = JSON.parse(await readFile(path.join(directory, 'build.json'), 'utf8'));
const buildMode = process.argv[5] ?? (build.modes.length === 1 ? build.modes[0] : 'trimmed');
assert.ok(build.modes.includes(buildMode), 'The requested build mode was not published.');
const sourceSha = process.env.MAGIC_UPGRADE_SOURCE_SHA ??
    execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
assert.equal(build.sourceSha, sourceSha, 'Fixture provenance does not match the checkout.');
const playwright = await import(pathToFileURL(driver));
const playwrightVersion = JSON.parse(await readFile(path.join(path.dirname(driver), 'package.json'), 'utf8')).version;
const versions = (await readFile(path.join(directory, 'historical-versions.txt'), 'utf8')).trim().split(/\s+/);
assert.equal(new Set(versions).size, versions.length);
assert.ok(versions.every(version => /^2\.0\.[1-7]$/.test(version)), 'The build did not record valid historical app versions.');
if (process.env.MAGIC_UPGRADE_REQUIRE_ALL === '1')
    assert.deepEqual(versions, ['2.0.1', '2.0.2', '2.0.3', '2.0.4', '2.0.5', '2.0.6', '2.0.7']);
const types = { '.wasm': 'application/wasm', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.html': 'text/html' };
let deployment;
const server = createServer(async (request, response) => {
    const root = path.join(directory, 'published', ...deployment.split('/'), 'wwwroot');
    const url = new URL(request.url, 'http://localhost');
    const asset = path.join(root, url.pathname === '/' ? 'index.html' : url.pathname);
    const file = asset.startsWith(root + path.sep) && await stat(asset).catch(() => null);
    if (!file?.isFile()) { response.writeHead(404).end(); return; }
    response.writeHead(200, { 'Content-Type': types[path.extname(asset)] ?? 'application/octet-stream',
        'Cache-Control': 'no-store', 'Content-Length': file.size });
    createReadStream(asset).pipe(response);
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = 'http://127.0.0.1:' + server.address().port;

async function snapshot(page, name, encryptedMember = null) {
    return page.evaluate(async ({ databaseName, encryptedMember }) => {
        const result = request => new Promise((resolve, reject) => {
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        const opening = indexedDB.open(databaseName);
        opening.onupgradeneeded = () => opening.transaction.abort();
        const db = await result(opening);
        try {
            const stores = [...db.objectStoreNames];
            const transaction = db.transaction(stores);
            const entries = stores.map(async name => {
                const store = transaction.objectStore(name);
                const indexes = [...store.indexNames].map(name => {
                    const index = store.index(name);
                    return { name, keyPath: index.keyPath, unique: index.unique, multiEntry: index.multiEntry };
                });
                const [keys, rows] = await Promise.all([result(store.getAllKeys()), result(store.getAll())]);
                const ciphertext = encryptedMember && name === 'records' ? rows.map(row =>
                    row[encryptedMember]?.ciphertext instanceof ArrayBuffer && row[encryptedMember].ciphertext.byteLength > 0) : null;
                return [name, { keyPath: store.keyPath, autoIncrement: store.autoIncrement, indexes, keys, rows,
                    ...(ciphertext && { ciphertext }) }];
            });
            return { version: db.version, data: Object.fromEntries(await Promise.all(entries)) };
        } finally { db.close(); }
    }, { databaseName: name, encryptedMember });
}

async function nextAuditKey(page, name) {
    return page.evaluate(databaseName => new Promise((resolve, reject) => {
        const open = indexedDB.open(databaseName);
        open.onupgradeneeded = () => open.transaction.abort();
        open.onerror = () => reject(open.error);
        open.onsuccess = () => {
            const db = open.result;
            const tx = db.transaction('audit', 'readwrite');
            const added = tx.objectStore('audit').add({ InvoiceId: 0, Action: 'key generator probe' });
            tx.onabort = () => {
                db.close();
                if (added.error) reject(added.error);
                else resolve(added.result);
            };
            tx.oncomplete = () => {
                db.close();
                reject(new Error('The key generator probe must roll back.'));
            };
            added.onsuccess = () => tx.abort();
        };
    }), name);
}

async function load(context, query = '') {
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(String(error)));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    assert.ok((await page.goto(origin + '/' + query)).ok(), 'The requested Blazor deployment is missing.');
    const result = page.locator('#fixture-result[data-state=passed], #fixture-result[data-state=failed]');
    try { await result.waitFor({ timeout: 60_000 }); }
    catch (error) { throw new Error(errors.join('\n') || 'Blazor did not report a fixture result.', { cause: error }); }
    assert.equal(await result.getAttribute('data-state'), 'passed', await result.innerText());
    return page;
}

const results = [];
const cutoverCases = versions.includes('2.0.7') ? ['uncooperative-v2-writer', 'cooperative-v2-bridge'] : [];
const expected = versions.length + cutoverCases.length + 2 + (versions.includes('2.0.7') ? 2 : 0);

async function runCase(name, action) {
    const result = { name, state: 'failed' };
    results.push(result);
    const context = await browser.newContext();
    try {
        await action(context, result);
        result.state = 'passed';
        console.log('PASS', name);
    } catch (error) {
        result.error = error.stack;
        console.error('FAIL', name, error);
    } finally { await context.close(); }
}

async function readManifest(schema, contract) {
    const root = path.join(directory, 'published', buildMode, contract + '-forward', 'wwwroot/db/forward', schema);
    return JSON.parse(await readFile(path.join(root, 'manifest.json'), 'utf8'));
}

async function verifyForward(page, previous, name, schema, contract) {
    const after = await snapshot(page, name);
    assert.equal(after.version, previous.version + 1);
    const manifest = await readManifest(schema, contract);
    const [edge] = manifest.migrations;
    assert.equal(manifest.targetRevision, 2);
    assert.equal(manifest.migrations.length, 1);
    const [journal] = after.data.__magic_migrations__.rows;
    assert.equal(after.data.__magic_migrations__.rows.length, 1);
    assert.equal(journal.revision, 2);
    assert.equal(journal.status, 'ready');
    assert.equal(journal.schemaId, schema);
    assert.equal(journal.active, null);
    assert.deepEqual(journal.history, [{ id: edge.id, sha256: edge.sha256 }]);
    const expectedInvoices = structuredClone(previous.data.invoices);
    expectedInvoices.rows = expectedInvoices.rows.map(row => ({ ...row, MigrationCount: 1 }));
    expectedInvoices.indexes.push({ name: 'MigrationCount', keyPath: 'MigrationCount', unique: false, multiEntry: false });
    expectedInvoices.indexes.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
    assert.deepEqual(after.data.invoices, expectedInvoices);
    assert.deepEqual(after.data.audit, previous.data.audit);
    if (name === losslessEast) {
        // The exact codec values stay unchanged. Only the physical policy's
        // invoice index list changes, and managed reopen verifies that policy.
        const oldPolicy = previous.data.__magic_numeric_policy.rows[0];
        const newPolicy = after.data.__magic_numeric_policy.rows[0];
        const expectedPolicy = structuredClone(oldPolicy);
        expectedPolicy.schema.find(store => store.name === 'invoices').indexes = expectedInvoices.indexes;
        assert.deepEqual(newPolicy, expectedPolicy);
    }
    assert.equal(await nextAuditKey(page, name), 3);
    return after;
}

const losslessEast = '__magic_numeric_v1__:' +
    createHash('sha256').update(JSON.stringify(['fixture-east'])).digest('hex').toUpperCase();
let browser;
let infrastructureFailure;
try {
    browser = await playwright[browserName].launch();
    for (const version of versions) {
        const names = { west: 'fixture-west', east: version === '2.0.7' ? losslessEast : 'fixture-east' };
        const context = await browser.newContext();
        const paths = [];
        context.on('response', response => {
            if (response.ok()) paths.push(new URL(response.url()).pathname);
        });
        const result = { version, state: 'failed' };
        results.push(result);
        try {
            deployment = version;
            const oldPage = await load(context);
            const before = {};
            for (const name of Object.values(names)) before[name] = await snapshot(oldPage, name);
            await oldPage.close();
            paths.length = 0;
            const contract = version === '2.0.7' ? 'codecs' : 'legacy';
            deployment = buildMode + '/' + contract;
            const newPage = await load(context);
            for (const [schema, name] of Object.entries(names)) {
                const previous = before[name];
                const after = await snapshot(newPage, name);
                assert.equal(after.version, previous.version + 1, name);
                const journal = after.data.__magic_migrations__.rows;
                assert.equal(journal.length, 1);
                assert.equal(journal[0].revision, 1);
                assert.equal(journal[0].status, 'ready');
                assert.equal(journal[0].schemaId, schema);
                assert.deepEqual(journal[0].history, []);
                delete after.data.__magic_migrations__;
                assert.deepEqual(after.data, previous.data, name + ' records, policy or native schema changed');
                assert.equal(previous.data.invoices.rows.length, 3);
                assert.equal(previous.data.audit.rows.length, 2);
                assert.ok(previous.data.invoices.indexes.some(index =>
                    Array.isArray(index.keyPath) && index.keyPath.join(',') === 'Account,Sequence'));
                assert.equal(await nextAuditKey(newPage, name), 3);
                const settled = previous.data.invoices.rows[0];
                if (version === '2.0.7') assert.equal(settled.Reference, 'INV4821');
                if (name === losslessEast) {
                    assert.equal(previous.data.__magic_numeric_policy.rows[0].mode, 'lossless');
                    assert.equal(typeof settled.AmountMinor, 'string');
                    assert.equal(typeof settled.Tax, 'string');
                }
            }
            const tenant = await snapshot(newPage, 'fixture-west-tenant');
            assert.deepEqual(tenant.data.invoices.keys, [701]);
            for (const schema of ['east', 'west']) assert.ok(paths.includes('/db/use/' + schema + '/manifest.json'));
            assert.ok(paths.includes('/_content/Magic.IndexedDb/migrations/migrationEngine.js'));
            assert.ok(paths.includes('/_content/Magic.IndexedDb/migrations/protocol.js'));
            await newPage.close();
            paths.length = 0;
            deployment += '-forward';
            const forward = await load(context);
            const migrated = {};
            for (const [schema, name] of Object.entries(names)) {
                migrated[name] = await verifyForward(forward,
                    { version: before[name].version + 1, data: before[name].data }, name, schema, contract);
                assert.ok(paths.includes('/db/forward/' + schema + '/manifest.json'));
            }
            await forward.close();
            const reopened = await load(context);
            for (const name of Object.values(names)) assert.deepEqual(await snapshot(reopened, name), migrated[name]);
            result.state = 'passed';
            result.nativeVersions = Object.fromEntries(Object.values(names).map(name => [name, [before[name].version, before[name].version + 1, migrated[name].version]]));
            result.recordsPerDatabase = 5;
            console.log('PASS', version, result.nativeVersions);
        } catch (error) {
            result.error = error.stack;
            console.error('FAIL', version, error);
        } finally { await context.close(); }
    }

    await runCase('explicit-recovery-in-blazor', async context => {
        deployment = buildMode + '/recovery';
        await load(context);
    });

    await runCase('authenticated-migrations-in-blazor', async (context, result) => {
        let old;
        const physical = '__magic_protected__:published-migration-fixture';
        const route = physical + ':released';
        if (versions.includes('2.0.7')) {
            deployment = '2.0.7';
            old = await load(context);
            await old.evaluate(async ({ physical, route }) => {
                window.released = await import('/_content/Magic.IndexedDb/magicDB.js');
                await released.createDb({ name: route, version: 1,
                    encryption: { account: 'published-account', physicalName: physical },
                    storeSchemas: [{ tableName: 'records', primaryKeyAuto: false, columnNamesInCompoundKey: ['Id'],
                        indexes: ['Id'], uniqueIndexes: [], columnNamesInCompoundIndex: [], encryptedFields: ['Secret'] }] });
                await released.unlockProtectedDatabase(route, 'fixture-key', new Uint8Array(32).fill(55), true);
                for (const Id of [1, 2, 3]) await released.addItem({ dbName: route, storeName: 'records',
                    record: { Id, Name: 'Public ' + Id, Secret: 'Private ' + Id, Visits: 0 } });
            }, { physical, route });
            const before = await snapshot(old, physical, 'Secret');
            assert.deepEqual(before.data.records.keys, [1, 2, 3]);
            assert.deepEqual(before.data.records.ciphertext, [true, true, true]);
            assert.equal(before.data.__magic_migrations__, undefined);
            result.seededBy = 'Magic.IndexedDb 2.0.7 static adapter';
        }
        deployment = buildMode + '/protected';
        const current = await load(context, old ? '?released=1' : '');
        if (old) {
            const fenced = await old.evaluate(async route => {
                try {
                    await released.addItem({ dbName: route, storeName: 'records',
                        record: { Id: 4, Name: 'Unexpected old write', Secret: 'Old policy', Visits: 0 } });
                    return false;
                } catch { return true; }
            }, route);
            assert.equal(fenced, true, 'A still-loaded released protected writer must be fenced.');
            assert.deepEqual((await snapshot(current, physical)).data.records.keys, [1, 2, 3]);
        }
    });

    if (versions.includes('2.0.7')) for (const legacyProtected of [false, true]) {
        const scenario = legacyProtected ? 'released-numeric-vault' : 'released-numeric-enrollment';
        await runCase(scenario, async (context, result) => {
            deployment = '2.0.7';
            const old = await load(context);
            const physical = (legacyProtected ? '__magic_protected__:' : '__magic_numeric_v1__:') + scenario;
            const route = '__magic_protected__:enrollment:' + scenario;
            const legacyRoute = legacyProtected ? physical + ':released' : physical;
            const numeric = { numericStorage: { version: 1, mode: 'lossless' }, numericLegacyName: scenario };
            const store = { tableName: 'records', primaryKeyAuto: false, columnNamesInCompoundKey: ['Name', 'Id'],
                indexes: ['Id'], uniqueIndexes: [], columnNamesInCompoundIndex: [] };
            const encryption = { account: 'published-account', physicalName: physical };
            const rows = [1, 2, 3].map(Id => ({ Id, Name: 'Account', Secret: 'Private ' + Id, Visits: 0,
                Wide: '~ml1:09232379236109516801', Reference: 'INV4821' }));
            await old.evaluate(async ({ legacyRoute, numeric, store, encryption, rows, legacyProtected }) => {
                const released = await import('/_content/Magic.IndexedDb/magicDB.js');
                await released.createDb({ name: legacyRoute, version: 1, ...numeric, storeSchemas: [
                    { ...store, ...(legacyProtected && { encryptedFields: ['Secret'] }) }],
                    ...(legacyProtected && { encryption }) });
                if (legacyProtected)
                    await released.unlockProtectedDatabase(legacyRoute, 'fixture-key', new Uint8Array(32).fill(55), true);
                for (const record of rows) await released.addItem({ dbName: legacyRoute, storeName: 'records', record });
            }, { legacyRoute, numeric, store, encryption, rows, legacyProtected });
            const before = await snapshot(old, physical, legacyProtected ? 'Secret' : null);
            assert.deepEqual(before.data.records.keys, rows.map(row => [row.Name, row.Id]));
            assert.deepEqual(before.data.records.rows.map(row => [row.Wide, row.Reference]), rows.map(row => [row.Wide, row.Reference]));
            assert.equal(Boolean(before.data.__magic_numeric_policy), !legacyProtected);
            if (legacyProtected) assert.deepEqual(before.data.records.ciphertext, [true, true, true]);
            else assert.deepEqual(before.data.records.rows, rows);
            await old.close();

            deployment = buildMode + '/protected';
            const current = await context.newPage();
            const manifestUrl = `/releases/legacy-numeric/${scenario}/manifest.json`;
            assert.ok((await current.goto(origin + manifestUrl)).ok());
            const after = await current.evaluate(async ({ manifestUrl, route, numeric, store, encryption, rows, legacyProtected }) => {
                const api = await import('/_content/Magic.IndexedDb/magicDB.js');
                const manifest = await (await fetch(manifestUrl)).json();
                const reference = manifest.schemas.find(schema => schema.revision === manifest.targetRevision);
                const model = await (await fetch(new URL(reference.path, location.href))).json();
                const config = { name: route, ...numeric, migrationManifestUrl: manifestUrl, migrationSchema: model,
                    encryption: { ...encryption, explicitPhysicalBinding: true },
                    storeSchemas: [{ ...store, ...(legacyProtected ? { exposedFields: ['Name', 'Id'] } : { encryptedFields: ['Secret'] }) }] };
                const read = () => Promise.all(rows.map(row => api.findItem(route, 'records', [row.Name, row.Id])));
                await api.createDb(config);
                await api.unlockProtectedDatabase(route, 'fixture-key', new Uint8Array(32).fill(55), !legacyProtected);
                await api.ensureProtectedMigration(route);
                const adopted = await read();
                const newKeyId = 'rotated-numeric-key';
                const newKey = new Uint8Array(32).fill(91);
                const rotation = await api.rotateManagedEncryptionKey(route, { targetKeyId: newKeyId,
                    mode: 'online', batchSize: 1, recoverable: true }, newKey);
                api.releaseProtectedDatabase(route);
                await api.createDb(config);
                await api.unlockProtectedDatabase(route, newKeyId, newKey);
                await api.ensureProtectedMigration(route);
                const reopened = await read();
                api.releaseProtectedDatabase(route);
                return { adopted, rotation, reopened };
            }, { manifestUrl, route, numeric, store, encryption, rows, legacyProtected });
            assert.deepEqual(after.adopted, rows);
            assert.deepEqual(after.reopened, rows);
            assert.equal(after.rotation.retirementReady, true);
            assert.equal(after.rotation.converted, rows.length);
            assert.equal(after.rotation.verified, rows.length);
            const final = await snapshot(current, physical, legacyProtected ? '__magic_record' : 'Secret');
            assert.deepEqual(final.data.records.keys, before.data.records.keys);
            assert.deepEqual(final.data.records.indexes, before.data.records.indexes);
            assert.equal(final.data.__magic_migrations__.rows[0].protocolVersion, 3);
            assert.equal(final.data.__magic_migrations__.rows[0].revision, 2);
            assert.equal(final.data.__magic_numeric_policy.rows[0].mode, 'lossless');
            assert.deepEqual(final.data.records.ciphertext, [true, true, true]);
            assert.ok(final.data.records.rows.every(row =>
                row[legacyProtected ? '__magic_record' : 'Secret'].keyId === 'rotated-numeric-key'));
            if (legacyProtected) assert.ok(final.data.records.rows.every(row =>
                !Object.hasOwn(row, 'Secret') && !Object.hasOwn(row, 'Wide') && !Object.hasOwn(row, 'Reference')));
            else assert.ok(final.data.records.rows.every((row, index) =>
                row.Wide === rows[index].Wide && row.Reference === rows[index].Reference));
            result.seededBy = 'Magic.IndexedDb 2.0.7 static adapter';
        });
    }

    if (cutoverCases.length) {
        await runCase(cutoverCases[0], async context => {
            deployment = '2.0.7';
            const old = await load(context);
            const module = await old.evaluate(() =>
                performance.getEntriesByType('resource').find(resource => resource.name.includes('/magicDB.js'))?.name);
            assert.ok(module);
            deployment = buildMode + '/codecs';
            const adopted = await load(context);
            await old.evaluate(async module => (await import(module)).addItem({
                dbName: 'fixture-west', storeName: 'invoices', record: { invoice_id: 901, Account: 'uncooperative', Sequence: 1 }
            }), module);
            assert.deepEqual((await snapshot(adopted, 'fixture-west')).data.invoices.keys, [1, 2, 3, 901]);
        });

        await runCase(cutoverCases[1], async context => {
            deployment = '2.0.7';
            const old = await load(context);
            await old.evaluate(async () => {
                window.bridge = await import('/cutover.js');
                window.worker = new Worker('/cutover-worker.js', { type: 'module' });
                await new Promise((resolve, reject) => {
                    worker.onmessage = event => event.data === 'ready' ? resolve() : reject(new Error(event.data));
                    worker.onerror = event => reject(new Error(event.message));
                });
            });
            assert.equal(await old.evaluate(async () => {
                try { await bridge.beginCutover(() => false, () => { throw new Error('Unexpected initialization'); }); }
                catch (error) { return error.message; }
            }), 'APP_CUTOVER_NOT_APPROVED');
            await old.evaluate(() => bridge.legacyWrite(() => Promise.resolve()));
            const before = await snapshot(old, 'fixture-west');

            await old.evaluate(() => {
                window.inFlight = bridge.legacyWrite(() => new Promise(resolve => { window.finishWrite = resolve; }));
                window.cutover = bridge.beginCutover(() => true,
                    () => new Promise((_, reject) => {
                        window.finishInitialization = () => reject(new Error('Injected initialization failure'));
                    })).catch(error => error.message);
            });
            await old.waitForFunction(() => typeof window.finishWrite === 'function');
            assert.equal(await old.evaluate(() => typeof window.finishInitialization), 'undefined');
            await old.evaluate(() => finishWrite());
            await old.waitForFunction(() => typeof window.finishInitialization === 'function');
            assert.equal(await old.evaluate(async () => { finishInitialization(); return cutover; }),
                'Injected initialization failure');
            assert.deepEqual(await snapshot(old, 'fixture-west'), before);
            await context.setOffline(true);
            try {
                const tabResult = await old.evaluate(async () => {
                    try { await bridge.legacyWrite(() => { throw new Error('Legacy write was allowed'); }); }
                    catch (error) { return error.message; }
                });
                const workerResult = await old.evaluate(() => new Promise(resolve => {
                    worker.onmessage = event => resolve(event.data);
                    worker.postMessage(902);
                }));
                assert.equal(tabResult, 'APP_LEGACY_WRITES_STOPPED');
                assert.equal(workerResult, 'APP_LEGACY_WRITES_STOPPED');
            } finally { await context.setOffline(false); }
            await old.evaluate(() => {
                window.retryCutover = bridge.beginCutover(() => true,
                    () => new Promise(resolve => { window.finishRetry = resolve; }));
            });
            await old.waitForFunction(() => typeof window.finishRetry === 'function');
            deployment = buildMode + '/codecs';
            const adopted = await load(context);
            await old.evaluate(async () => { finishRetry(); await retryCutover; });
            const after = await snapshot(adopted, 'fixture-west');
            assert.deepEqual(after.data.invoices, before.data.invoices);
        });
    }
} catch (error) {
    infrastructureFailure = error.stack;
    console.error(error);
} finally {
    await mkdir(directory, { recursive: true });
    const report = { sourceSha, runtimeVersion: build.runtimeVersion, cliVersion: build.cliVersion,
        sdkVersion: build.sdkVersion, packageVersions: versions, buildMode,
        platform: platform(), platformRelease: release(), platformVersion: osVersion(), architecture: arch(),
        nodeVersion: process.version, playwrightVersion,
        browser: browserName, browserVersion: browser?.version(),
        expected, executed: results.length, skipped: 0, missing: expected - results.length,
        failures: results.filter(result => result.state !== 'passed').length,
        ...(infrastructureFailure && { infrastructureFailure }), results };
    await writeFile(path.join(directory, 'upgrade-' + buildMode + '-' + browserName + '.json'), JSON.stringify(report, null, 2) + '\n');
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
}
if (infrastructureFailure || results.length !== expected || results.some(result => result.state !== 'passed')) process.exitCode = 1;
