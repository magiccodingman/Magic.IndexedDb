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

async function getDb(dbName) {
    if (!databases.has(dbName)) {
        console.warn(`Blazor.IndexedDB.Framework - Database ${dbName} doesn't exist`);
        const db1 = new Dexie(dbName);
        await db1.open();
        databases.set(dbName, db1);
    }
    return databases.get(dbName);
}


/*export function createDb(dbName, storeSchemas) {
    console.log(`Creating database: ${dbName}`);

    if (databases.has(dbName)) {
        console.warn(`Blazor.IndexedDB.Framework - Database ${dbName} already exists.`);
        return databases.get(dbName).db;
    }

    const db = new Dexie(dbName);
    const stores = Object.fromEntries(
        storeSchemas.map(schema => {
            let def = schema.primaryKeyAuto ? "++" : "";
            def += schema.primaryKey ? schema.primaryKey : "";

            if (schema.uniqueIndexes && schema.uniqueIndexes.length) {
                def += "," + schema.uniqueIndexes.map(i => "&" + i).join(",");
            }

            if (schema.indexes && schema.indexes.length) {
                def += "," + schema.indexes.join(",");
            }

            if (schema.compoundKeys && schema.compoundKeys.length) {
                def += "," + schema.compoundKeys.map(keys => "[" + keys.join("+") + "]").join(",");
            }

            return [schema.name, def];
        })
    );

    db.version(1).stores(stores);
    databases.set(dbName, { name: dbName, db: db, isOpen: false });
    return db;
}*/

export function createDb(dbStore) {
    console.log("Debug: Received dbStore in createDb", dbStore);

    if (!dbStore || !dbStore.name) {
        console.error("Blazor.IndexedDB.Framework - Invalid dbStore provided");
        return;
    }

    const dbName = dbStore.name;

    if (databases.has(dbName)) {
        console.warn(`Blazor.IndexedDB.Framework - Database "${dbName}" already exists`);
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

/**
 * Opens a database if it is not already open.
 * @param {string} dbName - The database name.
 */
export async function openDb(dbName) {
    if (!databases.has(dbName)) {
        console.warn(`Blazor.IndexedDB.Framework - Database ${dbName} does not exist.`);
        return null;
    }

    const entry = databases.get(dbName);
    if (!entry.isOpen) {
        await entry.db.open();
        entry.isOpen = true;
        console.log(`Database ${dbName} opened successfully.`);
    }

    return entry.db;
}

export async function countTable(dbName, storeName) {
    const table = await getTable(dbName, storeName);
    return await table.count();
}

/**
 * Closes a specific database.
 */
export function closeDb(dbName) {
    if (databases.has(dbName)) {
        const entry = databases.get(dbName);
        entry.db.close();
        entry.isOpen = false;
        console.log(`Database ${dbName} closed successfully.`);
    } else {
        console.warn(`Blazor.IndexedDB.Framework - Database ${dbName} not found.`);
    }
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
    if (databases.has(dbName)) {
        const entry = databases.get(dbName);
        entry.db.close();
        databases.delete(dbName);
    }

    await Dexie.delete(dbName);
    console.log(`Database ${dbName} deleted.`);
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


/**
 * Deletes multiple items, supporting single and compound keys.
 */
export async function bulkDelete(dbName, storeName, items) {
    const table = await getTable(dbName, storeName);
    try {
        const formattedKeys = await Promise.all(items.map(async item => await formatKey(dbName, storeName, item)));

        await table.bulkDelete(formattedKeys);
        return items.length;
    } catch (e) {
        console.error(e);
        throw new Error('Some items could not be deleted');
    }
}


/**
 * Deletes a single item.
 */
export async function deleteItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    const key = await formatKey(item.dbName, item.storeName, item);
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
    let db = await getDb(dbName);
    let table = db.table(storeName);
    return table;
}

/**
 * Wrapper method for magicQueryAsync.
 * Automatically retrieves the Dexie instance from your manager using dbName.
 */
export async function wrapperMagicQueryAsync(dbName, storeName, nestedOrFilter, queryAdditions, forceCursor = false) {
    const db = await getDb(dbName); // Get the Dexie instance from your manager
    let table = db.table(storeName);
    return await magicQueryAsync(db, table, nestedOrFilter, queryAdditions, forceCursor);
}

/**
 * Wrapper method for magicQueryYield.
 * Automatically retrieves the Dexie instance from your manager using dbName.
 */
export async function* wrapperMagicQueryYield(dbName, storeName, nestedOrFilter, queryAdditions = [], forceCursor = false) {
    const db = await getDb(dbName); // Get the Dexie instance from your manager
    let table = db.table(storeName);
    yield* magicQueryYield(db, table, nestedOrFilter, queryAdditions, forceCursor);
}
