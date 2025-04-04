"use strict";

/// <reference types="./dexie/dexie.d.ts" />
import Dexie from "./dexie/dexie.js";
import { magicQueryAsync, magicQueryYield } from "./magicLinqToIndexedDb.js";
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

export async function openDb(dbName) {
    if (!dbName || typeof dbName !== "string") {
        throw new Error("openDb: Invalid database name.");
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
    if (db?.isOpen()) db.close();
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
    console.log("Debug: Received dbStore in createDb", dbStore);

    if (!dbStore || !dbStore.name) {
        console.error("Blazor.IndexedDB.Framework - Invalid dbStore provided");
        return;
    }

    
    const dbName = dbStore.name;

    if (isDbOpen(dbName)) {
        return;
    }


    const db = new Dexie(dbName);
    const stores = {};

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
        if (Array.isArray(schema.uniqueIndexes)) {
            for (let j = 0; j < schema.uniqueIndexes.length; j++) {
                def += `,&${schema.uniqueIndexes[j]}`;
            }
        }

        // **Handle Standard Indexes**
        if (Array.isArray(schema.indexes)) {
            for (let j = 0; j < schema.indexes.length; j++) {
                def += `,${schema.indexes[j]}`;
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

    console.log("Dexie Store Definition:", stores);

    db.version(dbStore.version).stores(stores);

    // Store the database in the Map (overwriting if it already exists)
    databases.set(dbName, db);

    db.open().catch(error => {
        console.error(`Failed to open IndexedDB for "${dbName}":`, error);
    });
}


/**
 * Creates multiple databases based on an array of dbStores.
 * @param {Array.<{ name: string, storeSchemas: StoreSchema[] }>} dbStores - List of database configurations.
 */
export function createDatabases(dbStores) {
    dbStores.forEach(dbStore => createDb(dbStore.name, dbStore.storeSchemas));
}


export async function countTable(dbName, storeName) {
    const table = await getTable(dbName, storeName);
    return await table.count();
}

/**
 * Closes all open databases.
 */
export function closeAll() {
    databases.forEach((entry, dbName) => {
        entry.db.close();
        entry.isOpen = false;
        console.log(`Database ${dbName} closed.`);
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
        console.log(`Database '${dbName}' deleted.`);
    } catch (deleteErr) {
        console.error(`deleteDb: Failed to delete DB '${dbName}'`, deleteErr);
    }
}

export async function doesDbExist(dbName) {
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
    console.log("All databases deleted.");
}




const keyCache = new Map(); // Caches key structures for each (db, storeName) combination

/**
 * Retrieves the primary key structure for a given table.
 * Caches the key structure to avoid redundant lookups.
 */
async function getPrimaryKey(dbName, storeName) {
    const cacheKey = `${dbName}.${storeName}`;

    if (keyCache.has(cacheKey)) {
        return keyCache.get(cacheKey);
    }

    const table = await getTable(dbName, storeName);
    const primaryKey = table.schema.primKey; // Retrieve the primary key metadata

    let keyStructure;
    if (Array.isArray(primaryKey.keyPath)) {
        keyStructure = { isCompound: true, keys: primaryKey.keyPath };
    } else {
        keyStructure = { isCompound: false, keys: [primaryKey.keyPath] };
    }

    keyCache.set(cacheKey, keyStructure);
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
    const table = await getTable(item.dbName, item.storeName);
    const key = await formatKey(item.dbName, item.storeName, item.record);

    return await table.add({
        ...item.record,
        id: key
    });
}

/**
 * Bulk adds multiple items.
 */
export async function bulkAddItem(dbName, storeName, items) {
    const table = await getTable(dbName, storeName);

    const formattedItems = await Promise.all(items.map(async item => ({
        ...item,
        id: await formatKey(dbName, storeName, item)
    })));

    return await table.bulkAdd(formattedItems);
}



/**
 * Inserts or updates a single item.
 */
export async function putItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    const key = await formatKey(item.dbName, item.storeName, item.record);

    return await table.put({
        ...item.record,
        id: key
    });
}

// Bulk put function for Dexie.js
export async function bulkPutItems(items) {
    if (!items.length) return;

    const { dbName, storeName } = items[0];
    const table = await getTable(dbName, storeName);

    const formattedItems = await Promise.all(items.map(async item => {
        const key = await formatKey(item.dbName, item.storeName, item.record);
        return {
            ...item.record,
            id: key
        };
    }));

    return await table.bulkPut(formattedItems);
}


/**
 * Updates an item using the correct primary key format.
 */
export async function updateItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    const key = await formatKey(item.dbName, item.storeName, item.record);

    return await table.update(key, item.record);
}

/**
 * Bulk updates items, ensuring keys are properly formatted.
 */
export async function bulkUpdateItem(items) {
    const table = await getTable(items[0].dbName, items[0].storeName);
    try {
        const formattedItems = await Promise.all(items.map(async item => ({
            ...item.record,
            id: await formatKey(item.dbName, item.storeName, item.record)
        })));

        await table.bulkPut(formattedItems);
        return items.length;
    } catch (e) {
        console.error(e);
        throw new Error('Some items could not be updated');
    }
}

async function getKeyArrayForDelete(dbName, storeName, keyData) {
    const keyInfo = await getPrimaryKey(dbName, storeName);

    if (!keyInfo.isCompound) {
        return keyData.find(k => k.JsName === keyInfo.keys[0])?.Value;
    }

    return keyInfo.keys.map(pk => {
        const part = keyData.find(k => k.JsName === pk);
        if (!part) throw new Error(`Missing key part: ${pk}`);
        return part.Value;
    });
}

/**
 * Deletes multiple items, supporting single and compound keys.
 */
export async function bulkDelete(dbName, storeName, items) {
    const table = await getTable(dbName, storeName);
    try {
        const formattedKeys = await Promise.all(
            items.map(item => getKeyArrayForDelete(dbName, storeName, item))
        );

        console.log('Keys to delete:', formattedKeys);
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
    const table = await getTable(item.dbName, item.storeName);
    const key = await formatKey(item.dbName, item.storeName, item.record);
    await table.delete(key);
}


export async function clear(dbName, storeName) {
    const table = await getTable(dbName, storeName);
    await table.clear();
}

export async function findItem(dbName, storeName, keyValue) {
    const table = await getTable(dbName, storeName);
    return await table.get(keyValue);
}

export async function toArray(dbName, storeName) {
    const table = await getTable(dbName, storeName);
    return await table.toArray();
}
export function getStorageEstimate() {
    return navigator.storage.estimate();
}

async function getTable(dbName, storeName) {
    let db = await openDb(dbName);
    let table = db.table(storeName);
    return table;
}

/**
 * Wrapper method for magicQueryAsync.
 * Automatically retrieves the Dexie instance from your manager using dbName.
 */
export async function wrapperMagicQueryAsync(dbName, storeName, nestedOrFilter, queryAdditions, forceCursor = false) {
    const db = await openDb(dbName); // Get the Dexie instance from your manager
    let table = db.table(storeName);
    return await magicQueryAsync(db, table, nestedOrFilter, queryAdditions, forceCursor);
}

/**
 * Wrapper method for magicQueryYield.
 * Automatically retrieves the Dexie instance from your manager using dbName.
 */
export async function* wrapperMagicQueryYield(dbName, storeName, nestedOrFilter, queryAdditions = [], forceCursor = false) {
    const db = await openDb(dbName); // Get the Dexie instance from your manager
    let table = db.table(storeName);
    yield* magicQueryYield(db, table, nestedOrFilter, queryAdditions, forceCursor);
}
