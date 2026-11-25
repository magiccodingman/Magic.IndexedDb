import assert from 'node:assert/strict';

// The public registration/unlock/CRUD boundary, using real Web Crypto and IndexedDB.
export function registerProtectedRegistrationTests({ test, schema, bundle, appendRevision, assets, jsonAsset, copy, registrationStore, inspect }) {
    function model(fields = ['secret']) {
        const result = schema();
        result.requiredCapabilities = ['authenticatedMigrationsV1'];
        result.stores[0].fields.push({ name: 'secret', type: 'string', nullable: false, codec: 'json' });
        result.stores[0].encryptedFields = fields;
        return result;
    }
    function plan(before, after, operations, options = {}) {
        return bundle(before, after, operations, { ...options,
            changeMigration: migration => { migration.requiredCapabilities = ['authenticatedMigrationsV1']; },
            changeManifest: manifest => { manifest.requiredCapabilities = ['authenticatedMigrationsV1']; }
        });
    }
    function initial(source) {
        const result = plan(source, source, []);
        result.manifest.targetRevision = 1;
        result.manifest.schemas = result.manifest.schemas.slice(0, 1);
        result.manifest.migrations = [];
        jsonAsset(result.base + '/manifest.json', result.manifest);
        return result;
    }
    function configuration(physical, route, source, manifestUrl) {
        return { name: physical + ':' + route, encryption: { account: 'registration-account', physicalName: physical },
            migrationSchema: source, migrationManifestUrl: manifestUrl,
            storeSchemas: [{ ...registrationStore(), encryptedFields: source.stores[0].encryptedFields }] };
    }

    test('authenticated registration authorizes before bootstrap and preserves ciphertext across lock', async ({ page }) => {
        const physical = '__magic_protected__:managed-bootstrap';
        const target = model();
        const config = configuration(physical, 'route', target, initial(target).url);
        const result = await page.evaluate(async ({ config, physical }) => {
            const api = await import('/engine/magicDB.js');
            await api.createDb(config);
            // Deferred authorization must use the registration snapshot, even
            // if a caller later reuses or mutates its original configuration.
            config.migrationSchema.schemaId = 'caller-mutated-schema';
            config.migrationSchema.stores[0].encryptedFields = [];
            const beforeAuthorization = await engine.inspectDatabase(physical);
            await api.unlockProtectedDatabase(config.name, 'test-key', new Uint8Array(32).fill(41), true);
            const afterAuthorization = await engine.inspectDatabase(physical);
            await api.ensureProtectedMigration(config.name);
            await api.addItem({ dbName: config.name, storeName: 'clients',
                record: { id: 1, name: 'Public', secret: 'Never persist this plaintext' } });
            const raw = (await readRows(physical))[0];
            const initial = await engine.inspectDatabase(physical);
            api.lockProtectedDatabase(config.name);
            let lockedCode;
            try { await api.findItem(config.name, 'clients', 1); }
            catch (error) { lockedCode = error.code; }
            await api.unlockProtectedDatabase(config.name, 'test-key', new Uint8Array(32).fill(41));
            await api.ensureProtectedMigration(config.name);
            const row = await api.findItem(config.name, 'clients', 1);
            const reopened = await engine.inspectDatabase(physical);
            api.releaseProtectedDatabase(config.name);
            return { beforeAuthorization, afterAuthorization, rawSecretType: typeof raw.secret,
                ciphertextBytes: raw.secret.ciphertext.byteLength, initial, reopened, lockedCode, row };
        }, { config, physical });
        assert.equal(result.beforeAuthorization, null);
        assert.equal(result.afterAuthorization, null);
        assert.equal(result.rawSecretType, 'object');
        assert.ok(result.ciphertextBytes > 0);
        assert.equal(result.initial.state.protocolVersion, 3);
        assert.equal(result.initial.state.status, 'ready');
        assert.equal(result.lockedCode, 'MAGDB_AUTHORIZATION_REQUIRED');
        assert.deepEqual(result.reopened, result.initial);
        assert.deepEqual(result.row, { id: 1, name: 'Public', secret: 'Never persist this plaintext' });
    });

    test('authenticated registration adopts a legacy vault and retires the older protected route', async ({ page }) => {
        const physical = '__magic_protected__:managed-legacy-adoption';
        const before = model();
        const after = model(['name', 'secret']);
        const migration = plan(before, after, [{ op: 'reprotect', store: 'clients' }], { mode: 'resumable', baseline: before });
        const legacy = configuration(physical, 'legacy', before, undefined);
        delete legacy.migrationManifestUrl;
        delete legacy.migrationSchema;
        const managed = configuration(physical, 'managed', after, migration.url);
        const result = await page.evaluate(async ({ legacy, managed, physical }) => {
            const api = await import('/engine/magicDB.js');
            await api.createDb(legacy);
            await api.unlockProtectedDatabase(legacy.name, 'test-key', new Uint8Array(32).fill(42), true);
            await api.addItem({ dbName: legacy.name, storeName: 'clients',
                record: { id: 1, name: 'Now private', secret: 'Already private' } });
            await api.createDb(managed);
            await api.unlockProtectedDatabase(managed.name, 'test-key', new Uint8Array(32).fill(42));
            await api.ensureProtectedMigration(managed.name);
            const row = await api.findItem(managed.name, 'clients', 1);
            const raw = (await readRows(physical))[0];
            let oldRejected = false;
            try { await api.addItem({ dbName: legacy.name, storeName: 'clients', record: { id: 2, name: 'Stale', secret: 'Stale' } }); }
            catch { oldRejected = true; }
            api.releaseProtectedDatabase(managed.name);
            return { row, oldRejected, rawNameType: typeof raw.name, rawSecretType: typeof raw.secret,
                nameCiphertextBytes: raw.name.ciphertext.byteLength, secretCiphertextBytes: raw.secret.ciphertext.byteLength };
        }, { legacy, managed, physical });
        assert.deepEqual(result.row, { id: 1, name: 'Now private', secret: 'Already private' });
        assert.equal(result.oldRejected, true);
        assert.equal(result.rawNameType, 'object');
        assert.equal(result.rawSecretType, 'object');
        assert.ok(result.nameCiphertextBytes > 0 && result.secretCiphertextBytes > 0);
        const saved = await inspect(page, physical);
        assert.equal(saved.state.revision, 2);
        assert.equal(saved.state.protocolVersion, 3);
    });

    test('locking an active authenticated wrapper migration revokes writes and permits authorized retry', async ({ page }) => {
        const physical = '__magic_protected__:managed-lock';
        const source = model();
        const first = configuration(physical, 'first', source, initial(source).url);
        const next = plan(source, copy(source), [{ op: 'transform', store: 'clients' }], { mode: 'resumable', module: `
            export async function transform(row) {
                if (row.id === 2 && globalThis.pauseProtectedHook) {
                    globalThis.protectedHookWaiting = true;
                    await new Promise(() => {});
                }
                return { ...row, visits: (row.visits ?? 0) + 1 };
            }` });
        const config = configuration(physical, 'next', source, next.url);
        await page.evaluate(async ({ first, config }) => {
            window.protectedApi = await import('/engine/magicDB.js');
            await protectedApi.createDb(first);
            await protectedApi.unlockProtectedDatabase(first.name, 'test-key', new Uint8Array(32).fill(43), true);
            await protectedApi.ensureProtectedMigration(first.name);
            for (const id of [1, 2]) await protectedApi.addItem({ dbName: first.name, storeName: 'clients',
                record: { id, name: 'Public ' + id, secret: 'Private ' + id, visits: 0 } });
            protectedApi.releaseProtectedDatabase(first.name);
            await protectedApi.createDb(config);
            await protectedApi.unlockProtectedDatabase(config.name, 'test-key', new Uint8Array(32).fill(43));
            window.pauseProtectedHook = true;
            window.protectedAttempt = protectedApi.ensureProtectedMigration(config.name)
                .then(() => ({ ok: true }), error => ({ code: error.code }));
        }, { first, config });
        await page.waitForFunction(() => window.protectedHookWaiting === true);
        const result = await page.evaluate(async ({ config, physical }) => {
            protectedApi.lockProtectedDatabase(config.name);
            const stopped = await protectedAttempt;
            const checkpoint = await engine.inspectDatabase(physical);
            const partial = (await readRows(physical)).map(row => ({ visits: row.visits, secretType: typeof row.secret }));
            window.pauseProtectedHook = false;
            await protectedApi.unlockProtectedDatabase(config.name, 'test-key', new Uint8Array(32).fill(43));
            await protectedApi.ensureProtectedMigration(config.name);
            const rows = await Promise.all([1, 2].map(id => protectedApi.findItem(config.name, 'clients', id)));
            protectedApi.releaseProtectedDatabase(config.name);
            return { stopped, checkpoint, partial, rows };
        }, { config, physical });
        assert.ok(['MAGDB_CANCELLED', 'MAGDB_AUTHORIZATION_LOST'].includes(result.stopped.code));
        assert.equal(result.checkpoint.state.status, 'migrating');
        assert.equal(result.checkpoint.state.active.processedRecords, 0);
        assert.deepEqual(result.partial, [{ visits: 0, secretType: 'object' }, { visits: 0, secretType: 'object' }]);
        assert.deepEqual(result.rows, [1, 2].map(id => ({ id, name: 'Public ' + id, secret: 'Private ' + id, visits: 1 })));
        assert.equal((await inspect(page, physical)).state.revision, 2);
    });

    test('explicit physical binding encrypts an existing plain database and later makes every field public', async ({ page }) => {
        const physical = 'existing-plain-records';
        const plain = model([]), protectedModel = model();
        let migration = plan(plain, protectedModel, [{ op: 'reprotect', store: 'clients' }],
            { mode: 'resumable', baseline: plain });
        function binding(route, target) {
            const configured = configuration(physical, route, target, migration.url);
            configured.name = '__magic_protected__:explicit-binding:' + route;
            configured.encryption.explicitPhysicalBinding = true;
            return configured;
        }
        const encrypted = binding('encrypted', protectedModel);
        await page.evaluate(async ({ physical, plain, encrypted }) => {
            await makeLegacy(physical, plain, [{ id: 1, name: 'Public', secret: 'Existing plaintext' }]);
            window.bindingApi = await import('/engine/magicDB.js');
            await bindingApi.createDb(encrypted);
            await bindingApi.unlockProtectedDatabase(encrypted.name, 'test-key', new Uint8Array(32).fill(44), true);
            await bindingApi.ensureProtectedMigration(encrypted.name);
        }, { physical, plain, encrypted });
        const protectedResult = await page.evaluate(async ({ physical, encrypted }) => {
            const raw = (await readRows(physical))[0];
            const logical = await bindingApi.findItem(encrypted.name, 'clients', 1);
            bindingApi.releaseProtectedDatabase(encrypted.name);
            return { logical, rawSecretType: typeof raw.secret, ciphertextBytes: raw.secret.ciphertext.byteLength };
        }, { physical, encrypted });
        assert.equal(protectedResult.logical.secret, 'Existing plaintext');
        assert.equal(protectedResult.rawSecretType, 'object');
        assert.ok(protectedResult.ciphertextBytes > 0);

        migration = appendRevision(migration, plain, [{ op: 'reprotect', store: 'clients', allowProtectionLoss: true }], 'resumable');
        const document = JSON.parse(assets.get(migration.lastMigrationPath).body);
        document.requiredCapabilities = ['authenticatedMigrationsV1'];
        migration.manifest.migrations.at(-1).sha256 = jsonAsset(migration.lastMigrationPath, document).sha256;
        jsonAsset(migration.base + '/manifest.json', migration.manifest);
        const declassified = binding('public', plain);
        const final = await page.evaluate(async ({ physical, declassified }) => {
            await bindingApi.createDb(declassified);
            await bindingApi.unlockProtectedDatabase(declassified.name, 'test-key', new Uint8Array(32).fill(44));
            await bindingApi.ensureProtectedMigration(declassified.name);
            const raw = (await readRows(physical))[0];
            const logical = await bindingApi.findItem(declassified.name, 'clients', 1);
            const snapshot = await engine.inspectDatabase(physical);
            bindingApi.releaseProtectedDatabase(declassified.name);
            return { raw, logical, snapshot };
        }, { physical, declassified });
        assert.deepEqual(final.raw, { id: 1, name: 'Public', secret: 'Existing plaintext' });
        assert.deepEqual(final.logical, final.raw);
        assert.equal(final.snapshot.state.revision, 3);
        assert.equal(final.snapshot.state.protocolVersion, 3);
        assert.equal(final.snapshot.state.status, 'ready');
    });
}
