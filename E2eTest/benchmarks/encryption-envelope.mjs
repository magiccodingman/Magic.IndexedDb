// Run after: dotnet build E2eTest -c Release
// node E2eTest/benchmarks/encryption-envelope.mjs
import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { chromium } from "../bin/Release/net10.0/.playwright/package/index.mjs";

const moduleSource = await readFile(new URL(
    "../../Magic.IndexedDb/wwwroot/utilities/encryptionEnvelope.js", import.meta.url));
const server = createServer((request, response) => {
    if (request.url === "/envelope.js") {
        response.writeHead(200, { "Content-Type": "text/javascript" });
        response.end(moduleSource);
    } else {
        response.writeHead(200, { "Content-Type": "text/html" });
        response.end("<!doctype html><title>Magic encryption benchmark</title>");
    }
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));

let browser;
try {
    browser = await chromium.launch();
    const page = await browser.newPage();
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    const results = await page.evaluate(async () => {
        const { importEncryptionKey, encryptValue, decryptValue } = await import("/envelope.js");
        const key = await importEncryptionKey(crypto.getRandomValues(new Uint8Array(32)));
        const rowCount = 1000;
        const pointOperations = 100;
        const rounds = 11;
        const rows = Array.from({ length: rowCount }, (_, id) => ({
            id, group: id % 10, value: "x".repeat(256)
        }));
        const samples = { plaintext: [], encrypted: [] };
        const requestResult = request => new Promise((resolve, reject) => {
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        const create = async name => {
            const request = indexedDB.open(name, 1);
            request.onupgradeneeded = () =>
                request.result.createObjectStore("records", { keyPath: "id" }).createIndex("group", "group");
            return requestResult(request);
        };
        const write = (db, entries) => new Promise((resolve, reject) => {
            const transaction = db.transaction("records", "readwrite");
            transaction.oncomplete = resolve;
            transaction.onabort = () => reject(transaction.error);
            for (const entry of entries) transaction.objectStore("records").put(entry);
        });
        const context = (db, id) => ({
            database: db.name, account: "probe", store: "records", primaryKey: id, member: "value"
        });
        const run = async (round, encrypted) => {
            const name = "probe-" + crypto.randomUUID();
            const db = await create(name);
            const stats = {};
            const seal = async row => ({
                ...row, value: await encryptValue(row.value, key, "probe-key", context(db, row.id))
            });
            const open = row => decryptValue(row.value, key, "probe-key", context(db, row.id));
            const check = row => { if (row !== rows[0].value) throw new Error("Incorrect value"); };
            const measure = async (label, run) => {
                const start = performance.now();
                await run();
                stats[label] = performance.now() - start;
            };

            try {
                await measure("pointWrites100Ms", async () => {
                    for (const row of rows.slice(0, pointOperations)) {
                        await write(db, [encrypted ? await seal(row) : row]);
                    }
                });
                await measure("bulkWrites1000Ms", async () => {
                    await write(db, encrypted ? await Promise.all(rows.map(seal)) : rows);
                });
                await measure("pointReads100Ms", async () => {
                    for (const row of rows.slice(0, pointOperations)) {
                        const found = await requestResult(db.transaction("records").objectStore("records").get(row.id));
                        check(encrypted ? await open(found) : found.value);
                    }
                });

                for (const [label, indexed] of [["indexedRead100Ms", true], ["scan1000Ms", false]]) {
                    await measure(label, async () => {
                        const store = db.transaction("records").objectStore("records");
                        const found = await requestResult(indexed ? store.index("group").getAll(0) : store.getAll());
                        if (found.length !== (indexed ? rowCount / 10 : rowCount)) throw new Error("Incorrect count");
                        const values = encrypted ? await Promise.all(found.map(open)) : found.map(row => row.value);
                        values.forEach(check);
                    });
                }
            } finally {
                db.close();
                await requestResult(indexedDB.deleteDatabase(name));
            }

            if (round > 0) samples[encrypted ? "encrypted" : "plaintext"].push(stats);
        };

        for (let round = 0; round < rounds; round++) {
            await run(round, round % 2 === 1);
            await run(round, round % 2 === 0);
        }
        return { userAgent: navigator.userAgent, rowCount, pointOperations, measuredRuns: rounds - 1, samples };
    });
    console.log(JSON.stringify(results, null, 2));
} finally {
    await browser?.close();
    server.close();
}
