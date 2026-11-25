import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { mkdir, readFile, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const directory = path.resolve(process.argv[2] ?? 'artifacts/released-upgrade');
const driver = path.resolve(process.argv[3] ?? 'tests/migrations/BrowserDriver/bin/Release/net10.0/.playwright/package/index.mjs');
const browserName = process.argv[4] ?? 'chromium';
const playwright = await import(pathToFileURL(driver));
const versions = (await readFile(path.join(directory, 'historical-versions.txt'), 'utf8')).trim().split(/\s+/);
assert.ok(versions.every(version => /^2\.0\.[1-7]$/.test(version)), 'The build did not record valid historical app versions.');
const types = { '.wasm': 'application/wasm', '.js': 'text/javascript', '.json': 'application/json', '.html': 'text/html' };
let deployment;
const server = createServer(async (request, response) => {
    const root = path.join(directory, 'published', deployment, 'wwwroot');
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

async function snapshot(page, name) {
    return page.evaluate(async databaseName => {
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
                return [name, { keyPath: store.keyPath, autoIncrement: store.autoIncrement, indexes, keys, rows }];
            });
            return { version: db.version, data: Object.fromEntries(await Promise.all(entries)) };
        } finally { db.close(); }
    }, name);
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

async function load(context) {
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(String(error)));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    await page.goto(origin);
    const result = page.locator('#fixture-result[data-state=passed], #fixture-result[data-state=failed]');
    try { await result.waitFor({ timeout: 60_000 }); }
    catch (error) { throw new Error(errors.join('\n') || 'Blazor did not report a fixture result.', { cause: error }); }
    assert.equal(await result.getAttribute('data-state'), 'passed', await result.innerText());
    return page;
}

const results = [];
const losslessEast = '__magic_numeric_v1__:' +
    createHash('sha256').update(JSON.stringify(['fixture-east'])).digest('hex').toUpperCase();
let browser;
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
            deployment = version === '2.0.7' ? 'codecs' : 'legacy';
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
            result.state = 'passed';
            result.nativeVersions = Object.fromEntries(Object.values(names).map(name => [name, [before[name].version, before[name].version + 1]]));
            result.recordsPerDatabase = 5;
            console.log('PASS', version, result.nativeVersions);
        } catch (error) {
            result.error = error.stack;
            console.error('FAIL', version, error);
        } finally { await context.close(); }
    }
} finally {
    await mkdir(directory, { recursive: true });
    const report = { browser: browserName, browserVersion: browser?.version(), results };
    await writeFile(path.join(directory, 'upgrade-' + browserName + '.json'), JSON.stringify(report, null, 2) + '\n');
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
}
if (results.length !== versions.length || results.some(result => result.state !== 'passed')) process.exitCode = 1;
