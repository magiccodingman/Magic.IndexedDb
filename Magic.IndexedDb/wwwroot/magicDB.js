"use strict";

/// <reference types="./dexie/dexie.d.ts" />
import Dexie from "./dexie/dexie.js";
import { magicQueryAsync, magicQueryYield, selectFields } from "./magicLinqToIndexedDb.js";
import { createProtectedStorage, ensureProtectedSchema, protectedDatabasePrefix } from "./utilities/protectedStorage.js";
import { encryptionMetadataStore } from "./utilities/encryptionPolicy.js";
import { assertEncryptionKey, importEncryptionKey } from "./utilities/encryptionEnvelope.js";
import { createProtectedMigrationAdapter } from "./migrations/protectedMigrationAdapter.js";
import {
    numericDatabasePrefix, numericMetadataStore, normalizeNumericStorage,
    createNumericPolicy, requireFreshNumericTarget, assertNumericPolicy
} from "./utilities/numericStorage.js";
import { debugLog } from "./utilities/utilityHelpers.js";
import { recoverDatabase, ensureDatabase, assertDatabaseReady, inspectDatabase, subscribeMigrationEvents, MigrationError } from "./migrations/migrationEngine.js";
import { canonicalJson, metadataStore } from "./migrations/protocol.js";
/**
 * @typedef {Object} DatabasesItem
 * @property {string} name
 * @property {Dexie} db
 */

/**
 * @type {Array.<DatabasesItem>}
 */


let databases = new Map(); // Change array to a Map
const protectedDatabases = new Map();
const numericDatabases = new Map();
const managedDatabases = new Map();
const recoveryAttempts = new Map();

export { inspectDatabase, subscribeMigrationEvents };

/** Cancel this tab's pending attempt without changing its committed journal. */
export function cancelMigration(name) {
    managedDatabases.get(name)?.controller?.abort();
    recoveryAttempts.get(name)?.abort();
}

export async function recoverMigration(options) {
    const managed = managedDatabases.get(options.name);
    if (managed?.protected) return runProtectedMigration(managed, options.recovery);
    if (recoveryAttempts.has(options.name))
        throw new MigrationError("MAGDB_RECOVERY_IN_PROGRESS", "This tab already has a recovery attempt for this database.");
    const controller = new AbortController();
    recoveryAttempts.set(options.name, controller);
    try { return await recoverDatabase({ ...options, signal: controller.signal }); }
    finally { recoveryAttempts.delete(options.name); }
}

function requireMigrationAuthorization(registration) {
    const authorization = registration.authorization;
    if (!authorization?.adapter || authorization.controller.signal.aborted)
        throw new MigrationError("MAGDB_AUTHORIZATION_REQUIRED", "Unlock this account's database before running its authenticated migrations.", {}, true);
    authorization.assertCurrent();
    return authorization;
}

/** Authorization is separate from migration execution so locking never waits for a long-running migration. */
async function authorizeProtectedMigration(registration, keyId, input, initialize) {
    if (registration.pending && registration.authorization?.adapter &&
        !registration.authorization.controller.signal.aborted)
        return; // Concurrent callers share the active authorization and attempt.
    registration.authorization?.controller.abort();
    const authorization = { controller: new AbortController(), adapter: null, key: null, keyId, initialize };
    authorization.assertCurrent = () => {
        if (registration.stale || registration.authorization !== authorization || authorization.controller.signal.aborted)
            throw new MigrationError("MAGDB_AUTHORIZATION_LOST", "The migration authorization was revoked. Unlock again to resume.", {}, true);
    };
    registration.authorization = authorization;
    try {
        let key;
        try {
            key = input instanceof Uint8Array ? await importEncryptionKey(input) : input;
            assertEncryptionKey(key);
        } catch {
            throw new Error("MAGIC_ENCRYPTION_INVALID_KEY: The provider did not supply a usable AES-GCM key.");
        }
        authorization.assertCurrent();
        authorization.key = key;
        authorization.adapter = await createProtectedMigrationAdapter({ name: registration.db.name,
            account: registration.account, keyId, key, signal: authorization.controller.signal,
            assertCurrent: authorization.assertCurrent });
        authorization.assertCurrent();
    } catch (error) {
        authorization.controller.abort();
        authorization.key = null;
        if (registration.authorization === authorization) registration.authorization = null;
        throw error;
    }
}

export async function ensureProtectedMigration(name) {
    const registration = managedDatabases.get(name);
    if (!registration?.protected) return;
    return runProtectedMigration(registration);
}

async function runProtectedMigration(registration, recovery) {
    if (registration.stale)
        throw new MigrationError("MAGDB_DATABASE_INCOMPATIBLE", "This account's database changed in another application. Reload before continuing.");
    const authorization = requireMigrationAuthorization(registration);
    if (registration.pending) {
        if (recovery) throw new MigrationError("MAGDB_RECOVERY_IN_PROGRESS", "This connection already has an authenticated migration attempt.");
        return registration.pending;
    }
    if (!recovery && registration.result) {
        await registration.db.open();
        await assertDatabaseReady(registration.db.backendDB(), registration.result, authorization.adapter);
        return registration.result;
    }
    const controller = new AbortController();
    registration.controller = controller;
    const revoke = () => controller.abort();
    authorization.controller.signal.addEventListener("abort", revoke, { once: true });
    registration.protection.lock();
    registration.db.close();
    const attempt = (async () => {
        try {
            authorization.assertCurrent();
            if (registration.numericStorage) await requireFreshNumericTarget(registration.db.name, registration.numericLegacyName);
            const options = { name: registration.db.name, schema: registration.schema,
                manifestUrl: registration.manifestUrl, protection: authorization.adapter,
                initialize: authorization.initialize, signal: controller.signal,
                ...(recovery ? { recovery } : {}) };
            if (recovery) {
                // Recovery completes only its approved edge. Normal opening still advances later revisions.
                registration.result = null;
                return await recoverDatabase(options);
            }
            const result = await ensureDatabase(options);
            authorization.assertCurrent();
            await registration.db.open();
            await assertDatabaseReady(registration.db.backendDB(), result, authorization.adapter);
            registration.protection.setManagedReadiness(result);
            await registration.protection.unlock(authorization.keyId, authorization.key, false);
            authorization.assertCurrent();
            registration.result = result;
            return result;
        } catch (error) {
            registration.result = null;
            registration.protection.lock();
            registration.db.close();
            throw error;
        } finally {
            authorization.controller.signal.removeEventListener("abort", revoke);
        }
    })();
    registration.pending = attempt;
    try { return await attempt; }
    finally { if (registration.pending === attempt) registration.pending = null; }
}

function registerProtectedMigration(dbStore, numericStorage) {
    const schema = nativeSchema(dbStore, numericStorage);
    const identity = canonicalJson({ schema, manifestUrl: dbStore.migrationManifestUrl,
        account: dbStore.encryption.account, physicalName: dbStore.encryption.physicalName ?? dbStore.name });
    const previous = managedDatabases.get(dbStore.name);
    if (previous) {
        if (!previous.protected || previous.identity !== identity)
            throw new MigrationError("MAGDB_CONFIGURATION_CONFLICT", "This protected route is already registered with another model or manifest.");
        if (previous.stale) throw new MigrationError("MAGDB_DATABASE_INCOMPATIBLE", "Reload this account's database before reopening.");
        return;
    }
    if (protectedDatabases.has(dbStore.name))
        throw new MigrationError("MAGDB_CONFIGURATION_CONFLICT", "Release the existing protected route before registering its managed history.");
    const db = new Dexie(dbStore.encryption.physicalName ?? dbStore.name);
    const protection = createProtectedStorage(db, dbStore.encryption.account, dbStore.storeSchemas, numericStorage,
        { allowEmptyProtection: true, managedSchema: schema });
    const registration = { protected: true, identity, name: dbStore.name, db, protection, schema,
        manifestUrl: dbStore.migrationManifestUrl, account: dbStore.encryption.account,
        numericStorage, numericLegacyName: dbStore.numericLegacyName, result: null,
        pending: null, authorization: null, controller: null, stale: false };
    managedDatabases.set(dbStore.name, registration);
    protectedDatabases.set(dbStore.name, protection);
    databases.set(dbStore.name, db);
    db.on("versionchange", () => {
        registration.stale = true;
        registration.authorization?.controller.abort();
        protection.dispose();
        db.close();
    });
}

function nativeSchema(dbStore, numericStorage) {
    return dbStore.migrationSchema ?? {
        protocolVersion: 1, schemaId: dbStore.schemaId ?? dbStore.numericLegacyName ?? dbStore.name,
        numericFormat: numericStorage ? "lossless-v1" : "legacy", requiredCapabilities: [],
        stores: dbStore.storeSchemas.map(store => {
            const indexes = new Map();
            for (const member of store.indexes ?? []) indexes.set(member, { name: member, keyPath: member, unique: false, multiEntry: false });
            for (const member of store.uniqueIndexes ?? []) indexes.set(member, { name: member, keyPath: member, unique: true, multiEntry: false });
            for (const parts of store.columnNamesInCompoundIndex ?? []) indexes.set(`[${parts.join("+")}]`, {
                name: `[${parts.join("+")}]`, keyPath: parts, unique: false, multiEntry: false });
            const keys = store.columnNamesInCompoundKey;
            return { name: store.tableName, keyPath: keys.length === 1 ? keys[0] : keys, autoIncrement: Boolean(store.primaryKeyAuto),
                indexes: [...indexes.values()].sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0), fields: [] };
        }).sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0)
    };
}

function createManagedDatabase(dbStore, numericStorage) {
    const schema = nativeSchema(dbStore, numericStorage);
    const identity = canonicalJson({ schema, manifestUrl: dbStore.migrationManifestUrl ?? null });
    const previous = managedDatabases.get(dbStore.name);
    if (previous) {
        if (previous.identity !== identity) throw new MigrationError("MAGDB_CONFIGURATION_CONFLICT", "This physical database is already registered with another model or manifest.");
        if (previous.stale) throw new MigrationError("MAGDB_DATABASE_INCOMPATIBLE", "This connection was invalidated by another application. Reload before reopening.");
        return previous.ready.then(() => getDb(dbStore.name)).then(() => undefined);
    }
    const db = new Dexie(dbStore.name);
    const registration = { identity, db, ready: null, result: null, stale: false, controller: new AbortController(),
        numericPolicy: numericStorage ? createNumericPolicy(dbStore.storeSchemas, numericStorage) : null };
    managedDatabases.set(dbStore.name, registration);
    databases.set(dbStore.name, db);
    if (numericStorage) numericDatabases.set(dbStore.name, registration);
    db.on("versionchange", () => { registration.stale = true; db.close(); });
    registration.ready = (async () => {
        try {
            if (numericStorage) await requireFreshNumericTarget(dbStore.name, dbStore.numericLegacyName);
            registration.result = await ensureDatabase({ name: dbStore.name, schema, manifestUrl: dbStore.migrationManifestUrl,
                signal: registration.controller.signal });
            await db.open(); // Dynamic open: Dexie must not repair or infer a schema.
            await assertDatabaseReady(db.backendDB(), registration.result);
            if (registration.numericPolicy) await assertNumericPolicy(db, registration.numericPolicy);
        } catch (error) {
            if (managedDatabases.get(dbStore.name) === registration) {
                managedDatabases.delete(dbStore.name); databases.delete(dbStore.name); numericDatabases.delete(dbStore.name);
            }
            db.close();
            throw error;
        }
    })();
    return registration.ready;
}

function rejectUnconfiguredNumericDatabase(name) {
    if (name.startsWith(numericDatabasePrefix) && !numericDatabases.has(name)) {
        throw new Error("MAGIC_NUMERIC_POLICY_REQUIRED: The lossless database is not configured.");
    }
}

function rejectUnconfiguredProtectedDatabase(name) {
    if (name.startsWith(protectedDatabasePrefix) && !protectedDatabases.has(name)) {
        throw new Error("MAGIC_ENCRYPTION_POLICY_REQUIRED: The protected database is not configured.");
    }
}

function protectedTable(database, store) {
    if (store === metadataStore) throw new MigrationError("MAGDB_RESERVED_STORE", "Migration metadata is reserved for the engine.");
    rejectUnconfiguredNumericDatabase(database);
    if (numericDatabases.has(database) && store === numericMetadataStore) {
        throw new Error("Magic numeric storage metadata is reserved.");
    }
    rejectUnconfiguredProtectedDatabase(database);
    if (protectedDatabases.has(database) && store === encryptionMetadataStore) {
        throw new Error("Magic encryption metadata is reserved.");
    }
    const managed = managedDatabases.get(database);
    if (managed?.protected && (managed.stale || managed.pending || !managed.result))
        throw new MigrationError("MAGDB_AUTHORIZATION_REQUIRED", "Complete the authenticated migration before accessing this database.", {}, true);
    return protectedDatabases.get(database)?.forTable(store);
}

export function unlockProtectedDatabase(name, keyId, key, initialize = false) {
    rejectUnconfiguredProtectedDatabase(name);
    const storage = protectedDatabases.get(name);
    if (!storage) throw new Error("The database does not have a Magic encryption policy.");
    const managed = managedDatabases.get(name);
    if (managed?.protected) return authorizeProtectedMigration(managed, keyId, key, initialize);
    return storage.unlock(keyId, key, initialize);
}

export function isProtectedDatabaseUnlocked(name) {
    return protectedDatabases.get(name)?.isUnlocked() ?? false;
}

export function getProtectedDatabaseState(name) {
    const managed = managedDatabases.get(name);
    if (managed?.protected && !managed.result) return managed.stale ? "PolicyMismatch" : "Locked";
    return protectedDatabases.get(name)?.state() ?? "Inactive";
}

export function lockProtectedDatabase(name) {
    const managed = managedDatabases.get(name);
    if (managed?.protected) {
        managed.authorization?.controller.abort();
        if (managed.authorization) managed.authorization.key = null;
        managed.authorization = null;
        managed.result = null;
    }
    protectedDatabases.get(name)?.lock();
}

export function releaseProtectedDatabase(name) {
    const protection = protectedDatabases.get(name);
    if (!protection) return;
    lockProtectedDatabase(name);
    const managed = managedDatabases.get(name);
    if (managed?.protected) {
        managed.stale = true;
        managedDatabases.delete(name);
    }
    protection.dispose();
    protectedDatabases.delete(name);
    databases.get(name).close();
    databases.delete(name);
}

export async function destroyProtectedDatabase(physicalName, registeredRoute = null) {
    const boundRoute = registeredRoute !== null && protectedDatabases.has(registeredRoute) &&
        managedDatabases.get(registeredRoute)?.protected && databases.get(registeredRoute)?.name === physicalName;
    if (typeof physicalName !== "string" ||
        (registeredRoute !== null ? !boundRoute : !physicalName.startsWith(protectedDatabasePrefix)))
        throw new Error("A protected database name or its explicitly registered managed route is required.");
    for (const [name, db] of databases) {
        if (db.name === physicalName && protectedDatabases.has(name)) releaseProtectedDatabase(name);
    }
    await Dexie.delete(physicalName);
}

export function patchPublicRecord(name, store, key, changes) {
    const table = protectedTable(name, store);
    if (!table) throw new Error("A public patch requires a protected table.");
    return table.patch(key, changes);
}

export async function projectPublicRecords(name, store, predicate, additions, forceCursor, fields) {
    const table = protectedTable(name, store);
    if (!table) throw new Error("A public projection requires a protected table.");
    const rows = [];
    for await (const row of table.query(predicate, additions, forceCursor, fields)) rows.push(row);
    return rows;
}

export async function openDb(dbName) {
    const db = await getDb(dbName);
    // Managed storage policies do not hand out an unguarded Dexie handle.
    return protectedDatabases.has(dbName) || numericDatabases.has(dbName) || managedDatabases.has(dbName) ? undefined : db;
}

async function getDb(dbName) {
    if (!dbName || typeof dbName !== "string") {
        throw new Error("openDb: Invalid database name.");
    }

    rejectUnconfiguredProtectedDatabase(dbName);
    rejectUnconfiguredNumericDatabase(dbName);

    const managed = managedDatabases.get(dbName);
    if (managed) {
        if (managed.protected) {
            await ensureProtectedMigration(dbName);
            return managed.db;
        }
        await managed.ready;
        if (managed.stale) throw new MigrationError("MAGDB_DATABASE_INCOMPATIBLE", "The database changed in another application. Reload before continuing.");
        if (!managed.db.isOpen()) await managed.db.open();
        await assertDatabaseReady(managed.db.backendDB(), managed.result);
        if (managed.numericPolicy) await assertNumericPolicy(managed.db, managed.numericPolicy);
        return managed.db;
    }

    if (databases.has(dbName)) {
        const existingDb = databases.get(dbName);
        if (!existingDb.isOpen()) {
            await existingDb.open(); // Re-open if it was closed
        }
        return existingDb;
    }

    throw new MigrationError("MAGDB_DATABASE_UNREGISTERED", "Declare and initialize the logical database before querying an instance.");
}


export async function closeDb(dbName) {
    const db = databases.get(dbName);
    lockProtectedDatabase(dbName);
    if (db?.isOpen()) db.close();
    if (protectedDatabases.has(dbName) || numericDatabases.has(dbName) || managedDatabases.has(dbName)) return;
    databases.delete(dbName);
}

export function isDbOpen(dbName) {
    if (!dbName || typeof dbName !== "string") {
        console.error("isDbOpen: Invalid database name.");
        return false;
    }

    const db = databases.get(dbName);

    if (!db) {
        // Not cached = definitely not open
        return false;
    }

    if (typeof db.isOpen === "function") {
        return db.isOpen(); // Dexie provides this
    }

    // Fallback just in case
    return false;
}

export function listOpenDatabases() {
    return Array.from(databases.entries())
        .filter(([_, db]) => db.isOpen?.())
        .map(([name]) => name);
}


export function createDb(dbStore) {
    debugLog("Received dbStore in createDb", dbStore);

    if (!dbStore || typeof dbStore.name !== "string" || !dbStore.name || !Array.isArray(dbStore.storeSchemas))
        throw new MigrationError("MAGDB_CONFIGURATION_INVALID", "A database name and store schema array are required.");

    
    const dbName = dbStore.name;

    const encryption = dbStore.encryption;
    const numericStorage = normalizeNumericStorage(dbStore.numericStorage);
    if (!encryption) {
        if (numericStorage && !dbName.startsWith(numericDatabasePrefix)) throw new Error("MAGIC_NUMERIC_POLICY_INVALID: Lossless databases require their reserved physical namespace.");
        if (!numericStorage && dbName.startsWith(numericDatabasePrefix)) throw new Error("MAGIC_NUMERIC_POLICY_REQUIRED: Lossless namespace requires its policy.");
        if (dbName.startsWith(protectedDatabasePrefix)) throw new Error("Protected database names require their storage policy.");
        return createManagedDatabase(dbStore, numericStorage);
    }
    if (!dbName.startsWith(protectedDatabasePrefix) ||
        typeof encryption.account !== "string" || !encryption.account ||
        (encryption.physicalName != null &&
            (typeof encryption.physicalName !== "string" || !encryption.physicalName ||
                /[\u0000-\u001f\u007f]/.test(encryption.physicalName) ||
                (encryption.explicitPhysicalBinding !== true &&
                    (!encryption.physicalName.startsWith(protectedDatabasePrefix) ||
                        !dbName.startsWith(encryption.physicalName + ":")))))) {
        throw new Error("Magic encrypted databases require a reserved name and account.");
    }
    if (encryption.explicitPhysicalBinding === true && dbStore.migrationManifestUrl == null)
        throw new MigrationError("MAGDB_CONFIGURATION_INVALID", "An explicit physical binding requires a managed migration manifest.");
    if (dbStore.migrationManifest != null)
        throw new MigrationError("MAGDB_CONFIGURATION_INVALID", "Managed protected registration requires an application-pinned manifest URL.");
    if (dbStore.migrationManifestUrl != null) return registerProtectedMigration(dbStore, numericStorage);
    if (protectedDatabases.has(dbName)) throw new Error("This protected database is already configured.");

    // Authenticated storage retains its existing guard until a migration adapter
    // can enforce the same policy while transforming records. There is no second
    // unguarded Dexie schema-deployment path here.
    const db = new Dexie(encryption.physicalName ?? dbName);
    const protection = createProtectedStorage(db, encryption.account, dbStore.storeSchemas, numericStorage);
    protectedDatabases.set(dbName, protection);
    db.on("versionchange", () => releaseProtectedDatabase(dbName));
    databases.set(dbName, db);
    return (async () => {
        if (numericStorage) await requireFreshNumericTarget(db.name, dbStore.numericLegacyName);
        await ensureProtectedSchema(db.name, dbStore.storeSchemas);
        await db.open();
    })().then(() => undefined, error => {
        protectedDatabases.delete(dbName);
        databases.delete(dbName);
        protection.dispose();
        db.close();
        throw error;
    });
}


/**
 * Creates multiple databases based on an array of dbStores.
 * @param {Array.<{ name: string, storeSchemas: StoreSchema[] }>} dbStores - List of database configurations.
 */
export function createDatabases(dbStores) {
    return Promise.all(dbStores.map(dbStore => createDb(dbStore)));
}


export async function countTable(dbName, storeName) {
    const protection = protectedTable(dbName, storeName);
    if (protection) return protection.count();
    const table = await getTable(dbName, storeName);
    return await table.count();
}

/**
 * Closes all open databases.
 */
export function closeAll() {
    for (const controller of recoveryAttempts.values()) controller.abort();
    databases.forEach((db, dbName) => {
        if (protectedDatabases.has(dbName)) return;
        if (db.isOpen?.()) db.close();
        debugLog(`Database ${dbName} closed.`);
    });
}

/**
 * Deletes a specific database.
 */
export async function deleteDb(dbName) {
    if (!dbName || typeof dbName !== "string") {
        console.error("deleteDb: Invalid database name.");
        return;
    }

    rejectUnconfiguredProtectedDatabase(dbName);
    rejectUnconfiguredNumericDatabase(dbName);
    const managed = managedDatabases.get(dbName);
    if (managed) {
        if (managed.protected) {
            const physical = managed.db.name;
            releaseProtectedDatabase(dbName);
            await Dexie.delete(physical);
            return;
        }
        try { await managed.ready; } catch { /* This is an explicit caller-requested delete. */ }
        managed.stale = true;
        managed.db.close();
        await Dexie.delete(dbName);
        managedDatabases.delete(dbName); databases.delete(dbName); numericDatabases.delete(dbName);
        return;
    }
    if (protectedDatabases.has(dbName)) {
        const registered = databases.get(dbName);
        releaseProtectedDatabase(dbName);
        await registered.delete();
        return;
    }

    try {
        const db = new Dexie(dbName);

        try {
            await db.open();
            if (db.isOpen()) {
                db.close();
            }
        } catch (openErr) {
            console.warn(`deleteDb: Couldn't open DB '${dbName}' before deletion. Proceeding anyway.`, openErr);
            // Still proceed � might be locked or unopened in current context
        }

        await Dexie.delete(dbName);
        debugLog(`Database '${dbName}' deleted.`);
    } catch (deleteErr) {
        throw deleteErr;
    }
}

export async function doesDbExist(dbName) {
    if (protectedDatabases.has(dbName)) dbName = databases.get(dbName).name;
    if (!dbName || typeof dbName !== "string") {
        console.error("doesDbExist: Invalid database name.");
        return false;
    }

    // Fast path: Chromium
    if (isChromium()) {
        try {
            const dbs = await indexedDB.databases();
            return dbs.some(db => db.name === dbName);
        } catch (err) {
            console.warn("doesDbExist (Chromium): Failed to list databases. Falling back.", err);
            // Fall through to bulletproof fallback
        }
    }

    // Bulletproof fallback (works in all browsers)
    return new Promise((resolve) => {
        let resolved = false;

        const request = indexedDB.open(dbName);

        request.onupgradeneeded = function () {
            request.transaction.abort(); // Prevent creating the DB
            if (!resolved) {
                resolved = true;
                resolve(false);
            }
        };

        request.onsuccess = function () {
            request.result.close();
            if (!resolved) {
                resolved = true;
                resolve(true);
            }
        };

        request.onerror = function (event) {
            const err = event.target.error;
            if (!resolved) {
                resolved = true;
                if (err?.name === "NotFoundError") {
                    resolve(false);
                } else {
                    console.warn("doesDbExist: Unexpected error during fallback check", err);
                    resolve(false);
                }
            }
        };

        // Just in case nothing fires (paranoia safety)
        setTimeout(() => {
            if (!resolved) {
                resolved = true;
                resolve(false);
            }
        }, 1000);
    });
}

function isChromium() {
    try {
        // Modern detection via userAgentData
        if (navigator.userAgentData?.brands?.some(b => b.brand.includes("Chromium"))) {
            return true;
        }

        // Legacy fallback detection
        return /Chrome/.test(navigator.userAgent) &&
            !!window.chrome &&
            typeof indexedDB.databases === "function";
    } catch (err) {
        console.warn("isChromium: Detection failed due to unexpected error.", err);
        return false;
    }
}

/**
 * Deletes all databases.
 */
export async function deleteAllDatabases() {
    for (const dbName of databases.keys()) {
        await deleteDb(dbName);
    }
    debugLog("All databases deleted.");
}




/**
 * Retrieves the primary key structure for a given table.
 * Read the live table schema so distinct names and recreated databases cannot
 * share stale metadata through a concatenated cache key.
 */
async function getPrimaryKey(dbName, storeName) {
    const table = await getTable(dbName, storeName);
    const primaryKey = table.schema.primKey; // Retrieve the primary key metadata

    let keyStructure;
    if (Array.isArray(primaryKey.keyPath)) {
        keyStructure = { isCompound: true, keys: primaryKey.keyPath };
    } else {
        keyStructure = { isCompound: false, keys: [primaryKey.keyPath] };
    }

    return keyStructure;
}

/**
 * Formats keys correctly based on the table's primary key structure.
 */
async function formatKey(dbName, storeName, keyData) {
    const keyInfo = await getPrimaryKey(dbName, storeName);

    if (!keyInfo.isCompound) {
        return keyData[keyInfo.keys[0]]; // Extract the single primary key
    }

    return keyInfo.keys.map(pk => keyData[pk]); // Extract multiple keys for compound key
}

/**
 * Adds a single item, dynamically determining primary key structure.
 */
export async function addItem(item) {
    const protection = protectedTable(item.dbName, item.storeName);
    if (protection) return protection.write([item.record], (table, [row]) => table.add(row));
    const table = await getTable(item.dbName, item.storeName);
    return await table.add({ ...item.record });
}

/**
 * Bulk adds multiple items.
 */
export async function bulkAddItem(dbName, storeName, items) {
    const protection = protectedTable(dbName, storeName);
    if (protection) return protection.write(items, (table, rows) => table.bulkAdd(rows));
    const table = await getTable(dbName, storeName);
    return await table.bulkAdd(items.map(item => ({ ...item })));
}



/**
 * Inserts or updates a single item.
 */
export async function putItem(item) {
    const protection = protectedTable(item.dbName, item.storeName);
    if (protection) return protection.write([item.record], (table, [row]) => table.put(row));
    const table = await getTable(item.dbName, item.storeName);
    // Older WebKit can remove the original row before a unique-index put fails.
    // An explicit transaction rolls that mutation back when the request rejects.
    return await table.db.transaction("rw", table, () => table.put({ ...item.record }));
}

// Bulk put function for Dexie.js
export async function bulkPutItems(items) {
    if (!items.length) return;

    const { dbName, storeName } = items[0];
    const protection = protectedTable(dbName, storeName);
    if (items.some(item => item.dbName.startsWith(protectedDatabasePrefix))) {
        requireOneProtectedTarget(items, dbName, storeName);
    }
    if (protection) {
        return protection.write(items.map(item => item.record), (table, rows) => table.bulkPut(rows));
    }
    const table = await getTable(dbName, storeName);
    return await table.db.transaction("rw", table,
        () => table.bulkPut(items.map(item => ({ ...item.record }))));
}


/**
 * Updates an item using the correct primary key format.
 */
export async function updateItem(item) {
    const protection = protectedTable(item.dbName, item.storeName);
    if (protection) return protection.write([item.record], (table, [row]) => table.update(protection.key(row), row));
    const table = await getTable(item.dbName, item.storeName);
    const key = await formatKey(item.dbName, item.storeName, item.record);

    // Keep update-only/count semantics while giving failed replacements the
    // same explicit rollback boundary as protected writes.
    return await table.db.transaction("rw", table, () => table.update(key, item.record));
}

/**
 * Bulk updates items, ensuring keys are properly formatted.
 */
function requireOneProtectedTarget(items, database, store) {
    if (items.some(item => item.dbName !== database || item.storeName !== store)) {
        throw new Error("A protected batch must target one database and store.");
    }
}

export async function bulkUpdateItem(items) {
    if (!items.length) return 0;
    const { dbName, storeName } = items[0];
    const protection = protectedTable(dbName, storeName);
    if (items.some(item => item.dbName.startsWith(protectedDatabasePrefix))) {
        requireOneProtectedTarget(items, dbName, storeName);
    }
    if (protection) {
        await protection.write(items.map(item => item.record), (table, rows) => table.bulkPut(rows));
        return items.length;
    }
    const table = await getTable(items[0].dbName, items[0].storeName);
    try {
        await table.db.transaction("rw", table,
            () => table.bulkPut(items.map(item => ({ ...item.record }))));
        return items.length;
    } catch (e) {
        console.error(e);
        throw new Error('Some items could not be updated');
    }
}

async function getKeyArrayForDelete(dbName, storeName, keyData) {
    const keyInfo = await getPrimaryKey(dbName, storeName);
    const getName = key => key.jsName ?? key.JsName;
    const getValue = key => key.value ?? key.Value;

    if (!keyInfo.isCompound) {
        const key = keyData.find(candidate => getName(candidate) === keyInfo.keys[0]);
        return key === undefined ? undefined : getValue(key);
    }

    return keyInfo.keys.map(pk => {
        const part = keyData.find(candidate => getName(candidate) === pk);
        if (!part) throw new Error(`Missing key part: ${pk}`);
        return getValue(part);
    });
}

/**
 * Deletes multiple items, supporting single and compound keys.
 */
export async function bulkDelete(dbName, storeName, items) {
    const protection = protectedTable(dbName, storeName);
    if (protection) {
        const keys = await Promise.all(items.map(item => getKeyArrayForDelete(dbName, storeName, item)));
        await protection.mutate(table => table.bulkDelete(keys));
        return items.length;
    }
    const table = await getTable(dbName, storeName);
    try {
        const formattedKeys = await Promise.all(
            items.map(item => getKeyArrayForDelete(dbName, storeName, item))
        );

        debugLog('Keys to delete:', formattedKeys);
        await table.bulkDelete(formattedKeys);
        return items.length;
    } catch (e) {
        console.error('bulkDelete error:', e);
        throw new Error('Some items could not be deleted');
    }
}




/**
 * Deletes a single item.
 */
export async function deleteItem(item) {
    const protection = protectedTable(item.dbName, item.storeName);
    if (protection) return protection.mutate(table => table.delete(protection.key(item.record)));
    const table = await getTable(item.dbName, item.storeName);
    const key = await formatKey(item.dbName, item.storeName, item.record);
    await table.delete(key);
}


export async function clear(dbName, storeName) {
    const protection = protectedTable(dbName, storeName);
    if (protection) return protection.mutate(table => table.clear());
    const table = await getTable(dbName, storeName);
    await table.clear();
}

export async function findItem(dbName, storeName, keyValue) {
    const protection = protectedTable(dbName, storeName);
    if (protection) return protection.read(keyValue);
    const table = await getTable(dbName, storeName);
    return await table.get(keyValue);
}

export async function toArray(dbName, storeName) {
    const protection = protectedTable(dbName, storeName);
    if (protection) return collectProtectedQuery(protection.query());
    const table = await getTable(dbName, storeName);
    return await table.toArray();
}
export async function getStorageEstimate() {
    if (navigator.storage?.estimate) {
        return await navigator.storage.estimate();
    }

    return { quota: 0, usage: 0 };
}

async function collectProtectedQuery(iterator) {
    const results = [];
    for await (const row of iterator) results.push(row);
    return results;
}

async function getTable(dbName, storeName) {
    if (protectedDatabases.has(dbName) && storeName === encryptionMetadataStore) {
        throw new Error("Magic encryption metadata is reserved.");
    }
    let db = await getDb(dbName);
    let table = db.table(storeName);
    return table;
}

/**
 * Wrapper method for magicQueryAsync.
 * Automatically retrieves the Dexie instance from your manager using dbName.
 */
export async function wrapperMagicQueryAsync(dbName, storeName, nestedOrFilter, queryAdditions, forceCursor = false) {
    const protection = protectedTable(dbName, storeName);
    if (protection) return collectProtectedQuery(protection.query(nestedOrFilter, queryAdditions, forceCursor));
    const db = await getDb(dbName); // Get the Dexie instance from your manager
    const table = db.table(storeName);
    return await magicQueryAsync(db, table, nestedOrFilter, queryAdditions, forceCursor);
}

/**
 * Wrapper method for magicQueryYield.
 * Automatically retrieves the Dexie instance from your manager using dbName.
 */
export async function* wrapperMagicQueryYield(dbName, storeName, nestedOrFilter, queryAdditions = [], forceCursor = false) {
    const protection = protectedTable(dbName, storeName);
    if (protection) {
        yield* protection.query(nestedOrFilter, queryAdditions, forceCursor);
        return;
    }
    const db = await getDb(dbName); // Get the Dexie instance from your manager
    const table = db.table(storeName);
    yield* magicQueryYield(db, table, nestedOrFilter, queryAdditions, forceCursor);
}

export async function projectRecords(dbName, storeName, predicate, additions, forceCursor, fields) {
    return collectProtectedQuery(projectRecordStream(dbName, storeName, predicate, additions, forceCursor, fields));
}

export async function* projectRecordStream(dbName, storeName, predicate, additions, forceCursor, fields) {
    const protection = protectedTable(dbName, storeName);
    if (protection) {
        for await (const row of protection.query(predicate, additions, forceCursor))
            yield selectFields(row, fields);
        return;
    }

    const db = await getDb(dbName);
    yield* magicQueryYield(db, db.table(storeName), predicate, additions, forceCursor, fields);
}
