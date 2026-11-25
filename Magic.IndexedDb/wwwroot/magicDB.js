"use strict";

/// <reference types="./dexie/dexie.d.ts" />
import Dexie from "./dexie/dexie.js";
import { magicQueryAsync, magicQueryYield, selectFields } from "./magicLinqToIndexedDb.js";
import { createProtectedStorage, ensureProtectedSchema, protectedDatabasePrefix } from "./utilities/protectedStorage.js";
import { encryptionMetadataStore } from "./utilities/encryptionPolicy.js";
import {
    numericDatabasePrefix, numericMetadataStore, normalizeNumericStorage,
    createNumericPolicy, numericPolicyIdentity, requireFreshNumericTarget,
    ensureNumericSchema, assertNumericPolicy
} from "./utilities/numericStorage.js";
import { debugLog } from "./utilities/utilityHelpers.js";
/**
 * @typedef {Object} DatabasesItem
 * @property {string} name
 * @property {Dexie} db
 */

/**
 * @type {Array.<DatabasesItem>}
 */


//export async function initializeMagicMigration() {
//    const { MagicMigration } = await import('/magicMigration.js');  // Dynamically import it
//    const magicMigration = new MagicMigration(db);  // Pass only Dexie.js
//    magicMigration.Initialize();  // Call method (optional)
//}

let databases = new Map(); // Change array to a Map
const protectedDatabases = new Map();
const numericDatabases = new Map();

function rejectUnconfiguredNumericDatabase(name) {
    if (name.startsWith(numericDatabasePrefix) && !numericDatabases.has(name)) {
        throw new Error("MAGIC_NUMERIC_POLICY_REQUIRED: The lossless database is not configured.");
    }
}

function releaseNumericDatabase(name) {
    const registration = numericDatabases.get(name);
    if (!registration) return;
    registration.released = true;
    registration.db.close();
    numericDatabases.delete(name);
    databases.delete(name);
}

function createNumericDatabase(dbStore, numericStorage) {
    const name = dbStore.name;
    if (!name.startsWith(numericDatabasePrefix)) {
        throw new Error("MAGIC_NUMERIC_POLICY_INVALID: Lossless databases require their reserved physical namespace.");
    }
    const policy = createNumericPolicy(dbStore.storeSchemas, numericStorage);
    const identity = numericPolicyIdentity(policy);
    const previous = numericDatabases.get(name);
    if (previous) {
        if (previous.identity !== identity) {
            throw new Error("MAGIC_NUMERIC_POLICY_MISMATCH: This connection already has a different numeric storage policy.");
        }
        return previous.ready.then(() => getDb(name)).then(() => undefined);
    }

    const db = new Dexie(name);
    const registration = { db, policy, identity, ready: null, released: false };
    numericDatabases.set(name, registration);
    databases.set(name, db);
    db.on("versionchange", () => releaseNumericDatabase(name));
    registration.ready = (async () => {
        try {
            await requireFreshNumericTarget(name, dbStore.numericLegacyName);
            await ensureNumericSchema(name, policy);
            await db.open();
            await assertNumericPolicy(db, policy);
            if (registration.released || numericDatabases.get(name) !== registration) {
                throw new Error("MAGIC_NUMERIC_POLICY_REQUIRED: The numeric database connection was released.");
            }
        } catch (error) {
            if (numericDatabases.get(name) === registration) releaseNumericDatabase(name);
            throw error;
        }
    })();
    return registration.ready;
}

function rejectUnconfiguredProtectedDatabase(name) {
    if (name.startsWith(protectedDatabasePrefix) && !protectedDatabases.has(name)) {
        throw new Error("MAGIC_ENCRYPTION_POLICY_REQUIRED: The protected database is not configured.");
    }
}

function protectedTable(database, store) {
    rejectUnconfiguredNumericDatabase(database);
    if (numericDatabases.has(database) && store === numericMetadataStore) {
        throw new Error("Magic numeric storage metadata is reserved.");
    }
    rejectUnconfiguredProtectedDatabase(database);
    if (protectedDatabases.has(database) && store === encryptionMetadataStore) {
        throw new Error("Magic encryption metadata is reserved.");
    }
    return protectedDatabases.get(database)?.forTable(store);
}

export function unlockProtectedDatabase(name, keyId, key, initialize = false) {
    rejectUnconfiguredProtectedDatabase(name);
    const storage = protectedDatabases.get(name);
    if (!storage) throw new Error("The database does not have a Magic encryption policy.");
    return storage.unlock(keyId, key, initialize);
}

export function isProtectedDatabaseUnlocked(name) {
    return protectedDatabases.get(name)?.isUnlocked() ?? false;
}

export function getProtectedDatabaseState(name) {
    return protectedDatabases.get(name)?.state() ?? "Inactive";
}

export function lockProtectedDatabase(name) {
    protectedDatabases.get(name)?.lock();
}

export function releaseProtectedDatabase(name) {
    const protection = protectedDatabases.get(name);
    if (!protection) return;
    protection.dispose();
    protectedDatabases.delete(name);
    databases.get(name).close();
    databases.delete(name);
}

export async function destroyProtectedDatabase(physicalName) {
    if (!physicalName.startsWith(protectedDatabasePrefix)) {
        throw new Error("A protected database name is required.");
    }
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
    return protectedDatabases.has(dbName) || numericDatabases.has(dbName) ? undefined : db;
}

async function getDb(dbName) {
    if (!dbName || typeof dbName !== "string") {
        throw new Error("openDb: Invalid database name.");
    }

    rejectUnconfiguredProtectedDatabase(dbName);
    rejectUnconfiguredNumericDatabase(dbName);

    const numeric = numericDatabases.get(dbName);
    if (numeric) {
        await numeric.ready;
        if (!numeric.db.isOpen()) await numeric.db.open();
        await assertNumericPolicy(numeric.db, numeric.policy);
        if (numeric.released || numericDatabases.get(dbName) !== numeric) {
            throw new Error("MAGIC_NUMERIC_POLICY_REQUIRED: The numeric database connection was released.");
        }
        return numeric.db;
    }

    if (databases.has(dbName)) {
        const existingDb = databases.get(dbName);
        if (!existingDb.isOpen()) {
            await existingDb.open(); // Re-open if it was closed
        }
        return existingDb;
    }

    const db = new Dexie(dbName);
    await db.open();
    databases.set(dbName, db);
    return db;
}


export async function closeDb(dbName) {
    const db = databases.get(dbName);
    lockProtectedDatabase(dbName);
    if (db?.isOpen()) db.close();
    if (protectedDatabases.has(dbName) || numericDatabases.has(dbName)) return;
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

    if (!dbStore || !dbStore.name) {
        console.error("Blazor.IndexedDB.Framework - Invalid dbStore provided");
        return;
    }

    
    const dbName = dbStore.name;

    const encryption = dbStore.encryption;
    const numericStorage = normalizeNumericStorage(dbStore.numericStorage);
    if (!encryption && numericStorage) return createNumericDatabase(dbStore, numericStorage);
    if (!encryption && dbName.startsWith(numericDatabasePrefix)) {
        throw new Error("MAGIC_NUMERIC_POLICY_REQUIRED: The lossless namespace requires its numeric storage policy.");
    }
    const existingProtection = protectedDatabases.get(dbName);
    if (encryption) {
        if (!dbName.startsWith(protectedDatabasePrefix) ||
            typeof encryption.account !== "string" || !encryption.account ||
            (encryption.physicalName != null &&
                (!encryption.physicalName.startsWith(protectedDatabasePrefix) ||
                    !dbName.startsWith(encryption.physicalName + ":")))) {
            throw new Error("Magic encrypted databases require a reserved name and account.");
        }
        if (existingProtection) throw new Error("This protected database is already configured.");
    } else if (dbName.startsWith(protectedDatabasePrefix)) {
        throw new Error("Protected Magic database names require a storage policy.");
    } else if (isDbOpen(dbName)) {
        return;
    }


    const db = new Dexie(encryption?.physicalName ?? dbName);
    const stores = Object.create(null);

    for (let i = 0; i < dbStore.storeSchemas.length; i++) {
        const schema = dbStore.storeSchemas[i];

        if (!schema || !schema.tableName) {
            console.error(`Invalid schema at index ${i}:`, schema);
            continue;
        }

        let def = "";

        // **Handle Primary Key (Single or Compound)**
        if (Array.isArray(schema.columnNamesInCompoundKey) && schema.columnNamesInCompoundKey.length > 0) {
            if (schema.columnNamesInCompoundKey.length === 1) {
                // Single primary key
                if (schema.primaryKeyAuto) def += "++"; // Auto increment
                def += schema.columnNamesInCompoundKey[0]; // Primary key column
            } else {
                // Compound primary key
                def += `[${schema.columnNamesInCompoundKey.join('+')}]`;
            }
        }

        // **Handle Unique Indexes**
        const uniqueIndexes = new Set(Array.isArray(schema.uniqueIndexes) ? schema.uniqueIndexes : []);
        for (const member of uniqueIndexes) {
            def += `,&${member}`;
        }

        // **Handle Standard Indexes**
        if (Array.isArray(schema.indexes)) {
            for (const member of new Set(schema.indexes)) {
                // Compound members and explicit annotations can also be unique.
                // Emit one native index without changing the original schema
                // payload used by authenticated Legacy vault fingerprints.
                if (!uniqueIndexes.has(member)) def += `,${member}`;
            }
        }

        // **Handle Compound Indexes**
        if (Array.isArray(schema.columnNamesInCompoundIndex)) {
            for (let j = 0; j < schema.columnNamesInCompoundIndex.length; j++) {
                let compoundIndex = schema.columnNamesInCompoundIndex[j];
                if (compoundIndex.length > 0) {
                    def += `,[${compoundIndex.join('+')}]`; // Correct format for compound indexes
                }
            }
        }

        stores[schema.tableName] = def;
    }

    debugLog("Dexie Store Definition:", stores);

    const protection = encryption
        ? createProtectedStorage(db, encryption.account, dbStore.storeSchemas, numericStorage)
        : null;
    if (protection) {
        protectedDatabases.set(dbName, protection);
        db.on("versionchange", () => releaseProtectedDatabase(dbName));
    } else {
        db.version(dbStore.version).stores(stores);
    }

    // Store the database in the Map (overwriting if it already exists)
    databases.set(dbName, db);

    if (protection) {
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

    db.open().catch(error => {
        console.error(`Failed to open IndexedDB for "${dbName}":`, error);
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
    if (numericDatabases.has(dbName)) {
        const registration = numericDatabases.get(dbName);
        try { await registration.ready; }
        catch { /* Explicit deletion may discard an invalid numeric target. */ }
        releaseNumericDatabase(dbName);
        await registration.db.delete();
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
        console.error(`deleteDb: Failed to delete DB '${dbName}'`, deleteErr);
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
