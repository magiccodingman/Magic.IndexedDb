// dotnet build E2eTest -c Release
// dotnet publish TestWasm -c Release -o ./work/encryption-wasm
// node E2eTest/benchmarks/encryption-wasm.mjs ./work/encryption-wasm/wwwroot
import assert from "node:assert/strict";
import { createServer } from "node:http";
import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import path from "node:path";
import { chromium, firefox, webkit } from "../bin/Release/net10.0/.playwright/package/index.mjs";

const root = path.resolve(process.argv[2]);
const engine = process.argv[3] ?? "chromium";
const rounds = Number(process.argv[4] ?? 11);
const rows = Number(process.argv[5] ?? 1000);
const fieldCharacters = Number(process.argv[6] ?? 256);
const workload = `Rows=${rows}&Characters=${fieldCharacters}`;
const types = {
    ".css": "text/css", ".html": "text/html", ".js": "text/javascript", ".json": "application/json",
    ".wasm": "application/wasm", ".dat": "application/octet-stream"
};
const server = createServer(async (request, response) => {
    const url = new URL(request.url, "http://127.0.0.1");
    const file = path.resolve(root, "." + (url.pathname === "/encryption-benchmark" ? "/index.html" : url.pathname));
    if (!file.startsWith(root + path.sep)) {
        response.writeHead(404).end();
        return;
    }
    try {
        const info = await stat(file);
        if (!info.isFile()) throw new Error("Expected a file");
        response.writeHead(200, {
            "Content-Type": types[path.extname(file)] ?? "application/octet-stream",
            "Content-Length": info.size
        });
        createReadStream(file).pipe(response);
    } catch {
        response.writeHead(404).end();
    }
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));

let browser;
try {
    browser = await { chromium, firefox, webkit }[engine].launch();
    const samples = { plain: [], protected: [] };
    const base = `http://127.0.0.1:${server.address().port}/encryption-benchmark`;
    let userAgent;
    let reloadVerified = false;
    let navigationVerified = false;
    let separateTabVerified = false;

    const getResult = async page => {
        const target = page.locator("#encryption-result");
        await target.waitFor({ timeout: 60000 });
        await page.waitForFunction(() => document.querySelector("#encryption-result")?.textContent !== "Running",
            null, { timeout: 60000 });
        const raw = await target.textContent();
        assert(raw.startsWith("{"), raw);
        return JSON.parse(raw);
    };

    for (let round = 0; round < rounds; round++) {
        for (const mode of round % 2 ? ["protected", "plain"] : ["plain", "protected"]) {
            const context = await browser.newContext();
            try {
                // The sample app's optional web font is irrelevant to this measurement.
                await context.route("https://fonts.googleapis.com/**", route => route.abort());
                const page = await context.newPage();
                page.on("pageerror", error => process.stderr.write(`${error}\n`));
                await page.goto(`${base}?mode=${mode}&${workload}`);
                userAgent ??= await page.evaluate(() => navigator.userAgent);
                const timing = await getResult(page);
                if (round > 0) samples[mode].push(timing);
                if (round === 0 && mode === "protected") {
                    await page.evaluate(() => Blazor.navigateTo("/counter"));
                    await page.getByRole("heading", { name: "Counter" }).waitFor();
                    await page.evaluate(parameters => Blazor.navigateTo(
                        "/encryption-benchmark?mode=protected&Navigated=true&" + parameters), workload);
                    assert.equal((await getResult(page)).navigationPassed, 1);
                    navigationVerified = true;

                    const separateTab = await context.newPage();
                    await separateTab.goto(`${base}?mode=protected&Reload=true&${workload}`);
                    assert.equal((await getResult(separateTab)).reloadPassed, 1);
                    separateTabVerified = true;
                    await separateTab.close();

                    await page.goto(`${base}?mode=protected&Reload=true&${workload}`);
                    assert.equal((await getResult(page)).reloadPassed, 1);
                    reloadVerified = true;
                }
            } finally {
                await context.close();
            }
        }
    }
    console.log(JSON.stringify({ engine, version: browser.version(), userAgent, rows,
        pointOperations: 25, fieldCharacters, measuredPairs: rounds - 1, reloadVerified, navigationVerified, separateTabVerified, samples }, null, 2));
} finally {
    await browser?.close();
    server.close();
}
