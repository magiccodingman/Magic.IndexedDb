import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';

test('worker ownership runs in both modes and only the CLI boundary is optional', () => {
    const scratch = mkdtempSync(path.join(tmpdir(), 'magic-cli-registration-'));
    try {
        const env = { ...process.env };
        delete env.MAGIC_CLI_FIXTURE;
        const list = () => spawnSync(process.execPath, [path.join(import.meta.dirname, 'run.mjs'), '--list'],
            { env, encoding: 'utf8' });
        const standalone = list();
        assert.equal(standalone.status, 0, standalone.stderr);
        const names = JSON.parse(standalone.stdout);
        assert.ok(names.includes('dedicated workers and tabs share migration ownership'));
        assert.ok(!names.some(name => name.startsWith('installed CLI output')));

        env.MAGIC_CLI_FIXTURE = path.join(scratch, 'manifest.json');
        const missing = list();
        assert.notEqual(missing.status, 0);
        assert.match(missing.stderr, /MAGIC_CLI_FIXTURE must point to/);
        writeFileSync(env.MAGIC_CLI_FIXTURE, JSON.stringify({ targetRevision: 2, schemas: [{ revision: 1 }, { revision: 2 }] }));
        const configured = list();
        assert.equal(configured.status, 0, configured.stderr);
        assert.deepEqual(JSON.parse(configured.stdout), [...names,
            'installed CLI output boots and upgrades through the browser protocol']);
    } finally { rmSync(scratch, { recursive: true, force: true }); }
});
