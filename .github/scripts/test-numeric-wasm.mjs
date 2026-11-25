import { spawn } from 'node:child_process';
import { access, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const wwwroot = path.resolve(process.argv[2] ?? 'artifacts/numeric-wasm/wwwroot');
const playwrightEntry = path.resolve(process.argv[3] ?? 'E2eTest/bin/Release/net10.0/.playwright/package/index.mjs');
const outputDirectory = path.resolve(process.argv[4] ?? 'artifacts/numeric-wasm-results');
const expectedScenarios = [
    'RoundTripAndStream',
    'IndexedPredicatesAndOrdering',
    'ExplicitKeysAndBulkCrud',
    'CanonicalUniqueIndexes',
    'NativeAutoIncrementKeys',
    'ProtectedPublicAccess'
];

await access(path.join(wwwroot, 'index.html'));
await access(playwrightEntry);
await mkdir(outputDirectory, { recursive: true });

let server;
let browser;
let page;
let serverOutput = '';
const consoleMessages = [];
const pageErrors = [];
let report;
let failure;

try {
    const { chromium } = await import(pathToFileURL(playwrightEntry).href);
    server = spawn('python3', ['-u', '-m', 'http.server', '0', '--bind', '127.0.0.1', '--directory', wwwroot],
        { stdio: ['ignore', 'pipe', 'pipe'] });
    server.stdout.on('data', chunk => { serverOutput += chunk.toString(); });
    server.stderr.on('data', chunk => { serverOutput += chunk.toString(); });
    const port = await new Promise((resolve, reject) => {
        const timer = setTimeout(() => finish(new Error('The static fixture server did not start.')), 15_000);
        const onData = chunk => {
            const match = chunk.toString().match(/Serving HTTP on .* port (\d+)/);
            if (match) finish(null, Number(match[1]));
        };
        const onError = error => finish(error);
        const onExit = code => finish(new Error('The static fixture server exited with code ' + code + '.'));
        function finish(error, value) {
            clearTimeout(timer);
            server.stdout.off('data', onData);
            server.off('error', onError);
            server.off('exit', onExit);
            error ? reject(error) : resolve(value);
        }
        server.stdout.on('data', onData);
        server.once('error', onError);
        server.once('exit', onExit);
    });

    browser = await chromium.launch({ headless: true });
    // A fresh browser context makes both database creation and the key-generator
    // boundary test reproducible, including repeated invocations of this script.
    const context = await browser.newContext();
    page = await context.newPage();
    page.on('console', message => consoleMessages.push(message.type() + ': ' + message.text()));
    page.on('pageerror', error => pageErrors.push(error.stack ?? String(error)));
    await page.goto('http://127.0.0.1:' + port + '/', { waitUntil: 'domcontentloaded', timeout: 30_000 });
    await page.waitForFunction(() => {
        const state = document.querySelector('#numeric-wasm-results')?.getAttribute('data-state');
        return state === 'passed' || state === 'failed';
    }, null, { timeout: 300_000 });

    report = JSON.parse(await page.locator('#numeric-wasm-results').innerText());
    await writeFile(path.join(outputDirectory, 'numeric-wasm-report.json'), JSON.stringify(report, null, 2) + '\n');
    if (report.state !== 'passed') throw new Error(report.failure ?? 'The WASM fixture failed.');
    if (report.isBrowser !== true) throw new Error('The fixture did not report a browser runtime.');
    const names = report.scenarios?.filter(scenario => scenario.state === 'passed').map(scenario => scenario.name);
    if (!Array.isArray(names) || names.length !== expectedScenarios.length ||
        expectedScenarios.some(name => !names.includes(name)))
        throw new Error('The WASM fixture did not execute all six numeric scenarios.');
    if (report.benchmark?.length !== 2 || ['Legacy', 'Lossless'].some(mode =>
        !report.benchmark.some(result => result.mode === mode && result.rowCount === 5_000 && result.repetitions === 3 &&
            result.writeMilliseconds.length === 3 && result.rangeMilliseconds.length === 3 && result.orderMilliseconds.length === 3)))
        throw new Error('The numeric benchmark did not report both complete comparisons.');
    if (pageErrors.length > 0) throw new Error('The browser reported an unhandled error.\n' + pageErrors.join('\n'));

    console.log('All six numeric scenarios passed in ' + report.runtime + ' (.NET ' + report.runtimeVersion + ').');
    console.log(JSON.stringify({ benchmark: report.benchmark }, null, 2));
} catch (error) {
    failure = error;
    console.error(error.stack ?? error);
    if (page) {
        try {
            await writeFile(path.join(outputDirectory, 'page.html'), await page.content());
            await page.screenshot({ path: path.join(outputDirectory, 'failure.png'), fullPage: true });
            if (!report) {
                const partial = await page.locator('#numeric-wasm-results').textContent({ timeout: 1_000 });
                if (partial) await writeFile(path.join(outputDirectory, 'partial-report.json'), partial);
            }
        } catch (captureError) {
            consoleMessages.push('Failed to capture browser state: ' + String(captureError));
        }
    }
} finally {
    await writeFile(path.join(outputDirectory, 'browser-console.log'), consoleMessages.join('\n') + '\n');
    await writeFile(path.join(outputDirectory, 'browser-errors.log'), pageErrors.join('\n') + '\n');
    if (browser) await browser.close();
    if (server) server.kill('SIGTERM');
    await writeFile(path.join(outputDirectory, 'static-server.log'), serverOutput);
    if (failure) {
        await writeFile(path.join(outputDirectory, 'failure.log'), failure.stack ?? String(failure));
        process.exitCode = 1;
    }
}
