"use strict";

/// <reference types="./dexie/dexie.d.ts" />
import Dexie from "./dexie/dexie.js";

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

const DEBUG_MODE = true; // Set to false before release

function debugLog(...args) {
    if (DEBUG_MODE) {
        console.log("[DEBUG]", ...args);
    }
}


//  Query Operations
const QUERY_OPERATIONS = {
    EQUAL: "Equal",
    NOT_EQUAL: "NotEqual",
    GREATER_THAN: "GreaterThan",
    GREATER_THAN_OR_EQUAL: "GreaterThanOrEqual",
    LESS_THAN: "LessThan",
    LESS_THAN_OR_EQUAL: "LessThanOrEqual",
    STARTS_WITH: "StartsWith",
    CONTAINS: "Contains",
    NOT_CONTAINS: "NotContains",
    IN: "In",
};


//  Query Additions (Sorting & Pagination)
const QUERY_ADDITIONS = {
    ORDER_BY: "orderBy",
    ORDER_BY_DESCENDING: "orderByDescending",
    FIRST: "first",
    LAST: "last",
    SKIP: "skip",
    TAKE: "take",
    TAKE_LAST: "takeLast",
};

//  Query Combination Ruleset (What Can Be Combined in AND `&&`)
const QUERY_COMBINATION_RULES = {
    [QUERY_OPERATIONS.EQUAL]: [QUERY_OPERATIONS.EQUAL, QUERY_OPERATIONS.IN],
    [QUERY_OPERATIONS.GREATER_THAN]: [QUERY_OPERATIONS.LESS_THAN, QUERY_OPERATIONS.LESS_THAN_OR_EQUAL],
    [QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL]: [QUERY_OPERATIONS.LESS_THAN, QUERY_OPERATIONS.LESS_THAN_OR_EQUAL],
    [QUERY_OPERATIONS.LESS_THAN]: [QUERY_OPERATIONS.GREATER_THAN, QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL],
    [QUERY_OPERATIONS.LESS_THAN_OR_EQUAL]: [QUERY_OPERATIONS.GREATER_THAN, QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL],
    [QUERY_OPERATIONS.STARTS_WITH]: [], //  Cannot combine StartsWith with anything
    [QUERY_OPERATIONS.IN]: [QUERY_OPERATIONS.EQUAL],
    [QUERY_OPERATIONS.CONTAINS]: [], //  Must always go to a cursor
};

//  Query Addition Ruleset (Which Operations Can Be Stacked)
const QUERY_ADDITION_RULES = {
    [QUERY_ADDITIONS.ORDER_BY]: [QUERY_ADDITIONS.SKIP, QUERY_ADDITIONS.TAKE, QUERY_ADDITIONS.TAKE_LAST],
    [QUERY_ADDITIONS.ORDER_BY_DESCENDING]: [QUERY_ADDITIONS.SKIP, QUERY_ADDITIONS.TAKE, QUERY_ADDITIONS.TAKE_LAST],
    [QUERY_ADDITIONS.FIRST]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING],
    [QUERY_ADDITIONS.LAST]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING],
    [QUERY_ADDITIONS.SKIP]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING],
    [QUERY_ADDITIONS.TAKE]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING],
    [QUERY_ADDITIONS.TAKE_LAST]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING], // **Allow TAKE_LAST after ORDER_BY**
};



const indexCache = {}; //  Cache indexed properties per DB+Store

function isValidFilterObject(obj) {
    if (!obj || !Array.isArray(obj.orGroups)) return false;

    return obj.orGroups.every(orGroup =>
        Array.isArray(orGroup.andGroups) &&
        orGroup.andGroups.every(andGroup =>
            Array.isArray(andGroup.conditions) &&
            andGroup.conditions.every(condition =>
                typeof condition.property === 'string' &&
                typeof condition.operation === 'string' &&
                (typeof condition.isString === 'boolean') &&
                (typeof condition.caseSensitive === 'boolean') &&
                ('value' in condition) // Ensures 'value' exists, but allows different types (number, string, etc.)
            )
        )
    );
}

function isValidQueryAdditions(arr) {
    if (!Array.isArray(arr)) {
        console.error("Invalid input: Expected an array but received:", arr);
        return false;
    }

    let isValid = true;

    arr.forEach((obj, index) => {
        if (!obj || typeof obj !== 'object') {
            console.error(`Error at index ${index}: Expected an object but received:`, obj);
            isValid = false;
            return;
        }

        if (typeof obj.additionFunction !== 'string') {
            console.error(`Error at index ${index}: additionFunction must be a string but got:`, obj.additionFunction);
            isValid = false;
        } else if (!Object.values(QUERY_ADDITIONS).includes(obj.additionFunction)) {
            console.error(`Error at index ${index}: additionFunction '${obj.additionFunction}' is not a valid QUERY_ADDITIONS value.`);
            isValid = false;
        }

        if (typeof obj.intValue !== 'number' || !Number.isInteger(obj.intValue)) {
            console.error(`Error at index ${index}: intValue must be an integer but got:`, obj.intValue);
            isValid = false;
        }

        if (obj.property !== undefined && obj.property !== null && typeof obj.property !== 'string') {
            console.error(`Error at index ${index}: property must be a string, null, or undefined but got:`, obj.property);
            isValid = false;
        }
    });

    return isValid;
}


export async function magicQueryAsync(dbName, storeName, nestedOrFilter, QueryAdditions, forceCursor = false) {
    debugLog("whereJson called");


    let results = []; // Collect results here

    for await (let record of magicQueryYield(dbName, storeName, nestedOrFilter, QueryAdditions, forceCursor)) {
        results.push(record);
    }

    debugLog("whereJson returning results", { count: results.length, results });

    return results; // Return all results at once
}

export async function* magicQueryYield(dbName, storeName, nestedOrFilter, queryAdditions = [], forceCursor = false) {
    debugLog("Starting where function", { dbName, storeName, nestedOrFilter, queryAdditions });

    if (!isValidFilterObject(nestedOrFilter)) {
        throw new Error("Invalid filter object provided to where function.");
    }
    if (!isValidQueryAdditions(queryAdditions)) {
        throw new Error("Invalid addition query provided to where function.");
    }
    
    let db = await getDb(dbName);
    let table = db.table(storeName);

    if (!indexCache[dbName]) indexCache[dbName] = {};
    if (!indexCache[dbName][storeName]) {
        const schema = table.schema;

        debugLog(`[IndexCache Init] Creating cache for database: ${dbName}, store: ${storeName}`, { schema });

        indexCache[dbName][storeName] = {
            indexes: new Set(),
            primaryKey: schema.primKey.name,
            uniqueKeys: new Set(),
            compoundKeys: new Map(), // Store compound keys properly
            compoundIndexes: new Map() // Store compound indexes properly
        };

        debugLog(`[IndexCache Init] Primary Key: ${schema.primKey.name}`);

        for (const index of schema.indexes) {
            debugLog(`[IndexCache Init] Processing index`, { index });

            // **Handle Normal Indexes**
            if (typeof index.keyPath === "string") {
                indexCache[dbName][storeName].indexes.add(index.keyPath);
                debugLog(`[IndexCache Init] Added single index: ${index.keyPath}`);
            }

            // **Handle Unique Indexes**
            if (index.unique) {
                indexCache[dbName][storeName].uniqueKeys.add(index.keyPath);
                debugLog(`[IndexCache Init] Added unique index: ${index.keyPath}`);
            }

            // **Handle Compound Indexes**
            if (Array.isArray(index.keyPath)) {
                const compoundKeySet = new Set(index.keyPath);
                indexCache[dbName][storeName].compoundIndexes.set(index.keyPath.join(","), compoundKeySet);

                debugLog(`[IndexCache Init] Added compound index: ${index.keyPath.join(", ")}`);

                // **Add each field as an indexed column individually**
                for (const field of index.keyPath) {
                    if (!indexCache[dbName][storeName].indexes.has(field)) {
                        indexCache[dbName][storeName].indexes.add(field);
                        debugLog(`[IndexCache Init] Marked field as indexed (from compound index): ${field}`);
                    }
                }
            }
        }

        // **Handle Compound Primary Keys (Treat them as Compound Indexes)**
        if (schema.primKey && Array.isArray(schema.primKey.keyPath)) {
            const primaryCompoundKeySet = new Set(schema.primKey.keyPath);
            indexCache[dbName][storeName].compoundIndexes.set(schema.primKey.keyPath.join(","), primaryCompoundKeySet);

            debugLog(`[IndexCache Init] Added compound primary key as a compound index: ${schema.primKey.keyPath.join(", ")}`);

            for (const field of schema.primKey.keyPath) {
                if (!indexCache[dbName][storeName].indexes.has(field)) {
                    indexCache[dbName][storeName].indexes.add(field);
                    debugLog(`[IndexCache Init] Marked primary key field as indexed: ${field}`);
                }
            }
        }

        debugLog(`[IndexCache Init] Final Index Cache`, {
            primaryKey: indexCache[dbName][storeName].primaryKey,
            indexes: [...indexCache[dbName][storeName].indexes],
            uniqueKeys: [...indexCache[dbName][storeName].uniqueKeys],
            compoundIndexes: [...indexCache[dbName][storeName].compoundIndexes.keys()]
        });
    }






    let primaryKey = indexCache[dbName][storeName].primaryKey;
    let yieldedPrimaryKeys = new Set(); // Track already returned records

    debugLog("Validated schema & cached indexes", { primaryKey, indexes: indexCache[dbName][storeName].indexes });

    // **Step 1: Clean up `nestedOrFilter` to remove empty structures**
    nestedOrFilter = cleanNestedOrFilter(nestedOrFilter);
    debugLog("Cleaned Filter Object", { nestedOrFilter });

    // **Step 2: Check if filter is now empty**
    let isFilterEmpty = !nestedOrFilter || !nestedOrFilter.orGroups || nestedOrFilter.orGroups.length === 0;

    debugLog("Filter Check After Cleaning", { isFilterEmpty });

    // **Step 3: Handle empty filter cases**
    if (isFilterEmpty) {
        if (!queryAdditions || queryAdditions.length === 0) {
            // **Case 1: No filter & No Query Additions -> Fetch Entire Table**
            debugLog("No filtering or query additions. Fetching entire table.");

            let allRecords = await table.toArray();
            while (allRecords.length > 0) {
                let record = allRecords.shift(); // Remove first item (Frees memory)
                if (!yieldedPrimaryKeys.has(record[primaryKey])) {
                    yieldedPrimaryKeys.add(record[primaryKey]);
                    yield record;
                }
            }
            return;
        } else {
            // **Case 2: No Filter but Query Additions Exist -> Trick Indexed Query**
            debugLog("Empty filter but query additions exist. Applying primary key GREATER_THAN_OR_EQUAL trick.");

            nestedOrFilter = {
                orGroups: [{
                    andGroups: [{
                        conditions: [{
                            property: primaryKey,
                            operation: QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL,
                            value: -Infinity // Trick Dexie into fetching everything
                        }]
                    }]
                }]
            };
        }
    }


    let requiresCursor = validateQueryAdditions(queryAdditions, indexCache, dbName, storeName)
        || validateQueryCombinations(nestedOrFilter) || forceCursor;
    debugLog("Determined if query requires cursor", { requiresCursor });


    // partition function to correctly separate IndexedDB vs. Cursor**
    let { indexedQueries, compoundIndexQueries, cursorConditions } =
        partitionQueryConditions(nestedOrFilter, queryAdditions, indexCache, dbName, storeName, requiresCursor);

    debugLog("Final Indexed Queries vs. Compound Queries vs. Cursor Queries", { indexedQueries, compoundIndexQueries, cursorConditions });

    // **Only process indexed queries if at least one exists**
    if (indexedQueries.length > 0 || compoundIndexQueries.length > 0) {
        let { optimizedSingleIndexes, optimizedCompoundIndexes } = optimizeIndexedQueries(indexedQueries, compoundIndexQueries);

        debugLog("Optimized Indexed Queries", { optimizedSingleIndexes, optimizedCompoundIndexes });

        // **Merge both index types into one execution list**
        let allOptimizedQueries = [...optimizedSingleIndexes, ...optimizedCompoundIndexes];

        for (let query of allOptimizedQueries) {
            let records = await runIndexedQuery(table, query, queryAdditions); // Load batch of records first

            while (records.length > 0) {
                let record = records.shift(); // Remove first item (Frees memory immediately)
                if (!yieldedPrimaryKeys.has(record[primaryKey])) {
                    yieldedPrimaryKeys.add(record[primaryKey]);
                    yield record;
                }
            }
        }
    }



    // Pass yielded primary keys into cursor query to **skip already processed results**
    if (Array.isArray(cursorConditions) && cursorConditions.length > 0) {
        let cursorResults = await runCursorQuery(table, cursorConditions, queryAdditions, yieldedPrimaryKeys, primaryKey);
        debugLog("Cursor Query Results Count", { count: cursorResults.length });

        // Efficiently yield results and free memory
        while (cursorResults.length > 0) {
            let record = cursorResults.shift(); // Remove from array to free memory
            yieldedPrimaryKeys.add(record[primaryKey]);
            yield record;
        }
    }
}

function cleanNestedOrFilter(filter) {
    if (!filter || !Array.isArray(filter.orGroups)) return null;

    let cleanedOrGroups = filter.orGroups.map(orGroup => {
        if (!Array.isArray(orGroup.andGroups)) return null;

        // Remove empty AND groups
        let cleanedAndGroups = orGroup.andGroups.filter(andGroup =>
            Array.isArray(andGroup.conditions) && andGroup.conditions.length > 0
        );

        return cleanedAndGroups.length > 0 ? { andGroups: cleanedAndGroups } : null;
    }).filter(orGroup => orGroup !== null); // Remove any fully empty OR groups

    return cleanedOrGroups.length > 0 ? { orGroups: cleanedOrGroups } : null;
}


/**
 * Partitions query conditions into IndexedDB-optimized and cursor-based conditions.
 *
 * @param {Object} nestedOrFilter - The structured query filter containing OR and AND conditions.
 * @param {Array} queryAdditions - Additional sorting, pagination (TAKE, SKIP).
 * @param {Object} indexCache - Cached index metadata for the database and table.
 * @param {string} dbName - The database name.
 * @param {string} storeName - The store/table name.
 * @param {boolean} requiresCursor - Whether the entire query must use a cursor.
 * @returns {Object} - Returns `{ indexedQueries, cursorConditions }`
 */
function partitionQueryConditions(nestedOrFilter, queryAdditions, indexCache, dbName, storeName, requiresCursor) {
    debugLog("Partitioning query conditions", { nestedOrFilter, queryAdditions, requiresCursor });

    let indexedQueries = [];
    let compoundIndexQueries = [];
    let cursorConditions = [];

    if (requiresCursor) {
        debugLog("Forcing all conditions to use cursor due to validation.");
        for (const orGroup of nestedOrFilter.orGroups || []) {
            for (const andGroup of orGroup.andGroups || []) {
                if (andGroup.conditions && andGroup.conditions.length > 0) {
                    cursorConditions.push(andGroup.conditions);
                }
            }
        }
        return { indexedQueries: [], compoundIndexQueries: [], cursorConditions };
    }

    for (const orGroup of nestedOrFilter.orGroups || []) {
        if (!orGroup.andGroups || orGroup.andGroups.length === 0) continue;

        for (const andGroup of orGroup.andGroups) {
            if (!andGroup.conditions || andGroup.conditions.length === 0) continue;

            let needsCursor = false;
            let singleFieldConditions = [];

            const schema = indexCache[dbName][storeName];

            // **Step 1: Detect if this is a compound query**
            let compoundQuery = detectCompoundQuery(andGroup.conditions, indexCache, dbName, storeName);

            if (compoundQuery) {
                compoundIndexQueries.push(compoundQuery);
                continue;
            }

            // **Step 2: Process as a single-field indexed or cursor query**
            for (const condition of andGroup.conditions) {
                if (!condition || typeof condition !== "object" || !condition.operation) {
                    debugLog("Skipping invalid condition", { condition });
                    continue;
                }

                const isPrimaryKey = condition.property === schema.primaryKey;
                const isUniqueKey = schema.uniqueKeys.has(condition.property);
                const isStandaloneIndex = schema.indexes.has(condition.property);

                const isIndexed = isPrimaryKey || isUniqueKey || isStandaloneIndex;
                condition.isIndex = isIndexed;

                if (!isIndexed || !isSupportedIndexedOperation([condition])) {
                    needsCursor = true;
                    break;
                } else {
                    singleFieldConditions.push(condition);
                }
            }

            if (needsCursor) {
                cursorConditions.push(andGroup.conditions);
            } else {
                indexedQueries.push(singleFieldConditions);
            }
        }
    }

    /**
     * **Final Check:** If query additions (`TAKE`, `SKIP`, etc.) exist and there are multiple indexed queries,
     * force all queries (including compound index queries) to cursor execution.
     */
    const hasTakeOrSkipOrFirstOrLast = queryAdditions.some(addition =>
        [QUERY_ADDITIONS.TAKE, QUERY_ADDITIONS.SKIP, QUERY_ADDITIONS.TAKE_LAST,
        QUERY_ADDITIONS.LAST, QUERY_ADDITIONS.FIRST].includes(addition.additionFunction)
    );

    if (hasTakeOrSkipOrFirstOrLast && (indexedQueries.length + compoundIndexQueries.length) > 1) {
        debugLog("Multiple indexed/compound queries detected with TAKE/SKIP, forcing all to cursor.");
        cursorConditions = [...cursorConditions, ...indexedQueries.map(q => q.conditions), ...compoundIndexQueries.map(q => q.conditions)];
        indexedQueries = [];
        compoundIndexQueries = [];
    }

    debugLog("Partitioned Queries", { indexedQueries, compoundIndexQueries, cursorConditions });

    return { indexedQueries, compoundIndexQueries, cursorConditions };
}



function detectCompoundQuery(andConditions, indexCache, dbName, storeName) {
    debugLog("Checking if AND conditions match a compound index", { andConditions });

    const schema = indexCache[dbName][storeName];

    for (const fieldSet of schema.compoundIndexes.values()) {
        let matchedFields = new Set();

        // **Check if all fields in the compound index are present in the conditions**
        for (const cond of andConditions) {
            if (fieldSet.has(cond.property)) {
                matchedFields.add(cond.property);
            }
        }

        // **Ensure the query contains ALL fields required for the compound index**
        if (matchedFields.size === fieldSet.size) {
            // **Sort conditions to match the compound index order**
            let sortedConditions = [...andConditions]
                .filter(cond => fieldSet.has(cond.property))
                .sort((a, b) => [...fieldSet].indexOf(a.property) - [...fieldSet].indexOf(b.property));

            debugLog("Detected valid compound query", { properties: [...fieldSet], sortedConditions });

            return {
                properties: [...fieldSet],
                conditions: sortedConditions
            };
        }
    }

    debugLog("No matching compound index found");
    return null;
}





function validateQueryAdditions(queryAdditions, indexCache, dbName, storeName) {
    queryAdditions = queryAdditions || []; // Ensure it's always an array
    let seenAdditions = new Set();
    let requiresCursor = false;

    for (const addition of queryAdditions) {
        let validCombos = QUERY_ADDITION_RULES[addition.additionFunction];

        if (!validCombos) {
            console.error(`Unsupported query addition: ${addition.additionFunction}`);
            console.error(`Available keys in QUERY_ADDITION_RULES:`, Object.keys(QUERY_ADDITION_RULES));
            throw new Error(`Unsupported query addition: ${addition.additionFunction} (valid additions: ${Object.keys(QUERY_ADDITION_RULES).join(", ")})`);
        }


        // **Ensure ORDER_BY targets an indexed property**
        if (addition.additionFunction === QUERY_ADDITIONS.ORDER_BY || addition.additionFunction === QUERY_ADDITIONS.ORDER_BY_DESCENDING) {
            const isIndexed = indexCache[dbName]?.[storeName]?.indexes?.[addition.property] || false;
            if (!isIndexed) {
                debugLog(`Query requires cursor: ORDER_BY on non-indexed property ${addition.property}`);
                requiresCursor = true; // Forces cursor usage
            }
        }

        // **If TAKE_LAST is used after ORDER_BY, allow it (when indexed)**
        if (addition.additionFunction === QUERY_ADDITIONS.TAKE_LAST) {
            let prevAddition = queryAdditions[queryAdditions.indexOf(addition) - 1];
            if (!prevAddition || (prevAddition.additionFunction !== QUERY_ADDITIONS.ORDER_BY && prevAddition.additionFunction !== QUERY_ADDITIONS.ORDER_BY_DESCENDING)) {
                debugLog(`TAKE_LAST requires ORDER_BY but was not found before it.`);
                requiresCursor = true; // Force cursor if there's no ORDER_BY before it.
            }
        }

        // Check if the addition conflicts with previous additions
        for (const seen of seenAdditions) {
            if (!validCombos.includes(seen)) {
                requiresCursor = true;
                break;
            }
        }

        seenAdditions.add(addition.additionFunction);
    }

    return requiresCursor;
}




function validateQueryCombinations(nestedOrFilter) {
    debugLog("Validating Query Combinations", { nestedOrFilter });

    if (!nestedOrFilter || !Array.isArray(nestedOrFilter.orGroups)) {
        debugLog("Skipping validation: Filter object is invalid or missing OR groups.", { nestedOrFilter });
        return true; // Default to cursor processing if invalid
    }

    for (const orGroup of nestedOrFilter.orGroups) {
        if (!orGroup || !Array.isArray(orGroup.andGroups) || orGroup.andGroups.length === 0) {
            debugLog("Skipping empty or improperly formatted OR group", { orGroup });
            continue; // Skip empty or malformed groups
        }

        let needsCursor = false;

        for (const andGroup of orGroup.andGroups) {
            if (!andGroup || !Array.isArray(andGroup.conditions) || andGroup.conditions.length === 0) {
                debugLog("Skipping empty or improperly formatted AND group", { andGroup });
                continue;
            }

            let seenOperations = new Set();

            for (const condition of andGroup.conditions) {
                if (!condition || typeof condition !== 'object' || !condition.operation) {
                    debugLog("Skipping invalid condition", { condition });
                    continue;
                }

                debugLog("Checking condition for IndexedDB compatibility", { condition });

                if (!QUERY_COMBINATION_RULES[condition.operation]) {
                    debugLog(`Condition operation not supported: ${condition.operation}`, { condition });
                    needsCursor = true;
                    break;
                }

                for (const seenOp of seenOperations) {
                    if (!QUERY_COMBINATION_RULES[seenOp]?.includes(condition.operation)) {
                        debugLog(`Incompatible combination detected: ${seenOp} with ${condition.operation}`, { condition });
                        needsCursor = true;
                        break;
                    }
                }

                seenOperations.add(condition.operation);
            }

            if (needsCursor) {
                debugLog("Query requires cursor processing due to invalid operation combination.", { andGroup });
                return true; // Forces cursor fallback if AND/OR mix isn't possible
            }
        }
    }

    debugLog("Query can be fully indexed!", { nestedOrFilter });
    return false; // Can use IndexedDB directly
}


/**
 *  Determines if an indexed operation is supported.
 */
function isSupportedIndexedOperation(conditions) {
    for (const condition of conditions) {
        switch (condition.operation) {
            case QUERY_OPERATIONS.EQUAL:
            case QUERY_OPERATIONS.GREATER_THAN:
            case QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL:
            case QUERY_OPERATIONS.LESS_THAN:
            case QUERY_OPERATIONS.LESS_THAN_OR_EQUAL:
            case QUERY_OPERATIONS.IN:
                break; //  Supported
            case QUERY_OPERATIONS.STARTS_WITH:
                if (condition.caseSensitive) return false; //  Needs Cursor
                break;
            default:
                return false; //  Unsupported operation
        }
    }
    return true;
}

/**
 * Executes an indexed query using IndexedDB.
 * @param {Object} table - The Dexie table instance.
 * @param {Array} indexedConditions - The array of indexed conditions.
 * @returns {AsyncGenerator} - A generator that yields query results.
 */
async function runIndexedQuery(table, indexedConditions, queryAdditions = []) {
    debugLog("Executing runIndexedQuery", { indexedConditions, queryAdditions });

    if (indexedConditions.length === 0) {
        debugLog("No indexed conditions provided, returning entire table.");
        return await table.toArray();
    }

    let query;
    let firstCondition = indexedConditions[0];

    // **Detect Compound Index Query**
    if (Array.isArray(firstCondition.properties)) {
        debugLog("Detected Compound Index Query!", { properties: firstCondition.properties });

        query = table.where(firstCondition.properties);

        if (firstCondition.operation === QUERY_OPERATIONS.IN) {
            query = query.anyOf(firstCondition.value);
        } else {
            query = query.equals(firstCondition.value);
        }
    }
    // **Handle Single Indexed Query**
    else if (firstCondition.property) {
        debugLog("Detected Single-Index Query!", { property: firstCondition.property });

        switch (firstCondition.operation) {
            case QUERY_OPERATIONS.EQUAL:
                query = table.where(firstCondition.property).equals(firstCondition.value);
                break;
            case QUERY_OPERATIONS.IN:
                query = table.where(firstCondition.property).anyOf(firstCondition.value);
                break;
            case QUERY_OPERATIONS.GREATER_THAN:
                query = table.where(firstCondition.property).above(firstCondition.value);
                break;
            case QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL:
                query = table.where(firstCondition.property).aboveOrEqual(firstCondition.value);
                break;
            case QUERY_OPERATIONS.LESS_THAN:
                query = table.where(firstCondition.property).below(firstCondition.value);
                break;
            case QUERY_OPERATIONS.LESS_THAN_OR_EQUAL:
                query = table.where(firstCondition.property).belowOrEqual(firstCondition.value);
                break;
            case QUERY_OPERATIONS.STARTS_WITH:
                query = table.where(firstCondition.property).startsWith(firstCondition.value);
                break;
            default:
                throw new Error(`Unsupported indexed query operation: ${firstCondition.operation}`);
        }
    } else {
        throw new Error("Invalid indexed condition—missing `properties` or `property`.");
    }

    // **Process Query Additions**
    let needsReverse = false;
    let takeCount = null;
    let orderByProperty = null;

    for (const addition of queryAdditions) {
        switch (addition.additionFunction) {
            case QUERY_ADDITIONS.ORDER_BY:
                if (addition.property) {
                    orderByProperty = addition.property;
                }
                break;
            case QUERY_ADDITIONS.ORDER_BY_DESCENDING:
                if (addition.property) {
                    orderByProperty = addition.property;
                    needsReverse = true;
                }
                break;
            case QUERY_ADDITIONS.SKIP:
                query = query.offset(addition.intValue);
                break;
            case QUERY_ADDITIONS.TAKE:
                takeCount = addition.intValue;
                break;
            case QUERY_ADDITIONS.TAKE_LAST:
                needsReverse = true;
                takeCount = addition.intValue;
                break;
            case QUERY_ADDITIONS.FIRST:
                return query.first().then(result => (result ? [result] : []));
            case QUERY_ADDITIONS.LAST:
                return query.last().then(result => (result ? [result] : []));
            default:
                throw new Error(`Unsupported query addition: ${addition.additionFunction}`);
        }
    }

    // **Ensure TAKE_LAST applies reverse before limiting**
    let results;
    if (orderByProperty) {
        debugLog("Applying ORDER BY operation", { orderByProperty });

        results = await query.sortBy(orderByProperty);
        if (needsReverse) results.reverse();
    } else {
        results = await query.toArray();
        if (needsReverse) results.reverse();
    }

    // **Apply TAKE (last or normal)**
    if (takeCount !== null) {
        results = results.slice(0, takeCount);
    }

    debugLog("Final Indexed Query Results Count", { count: results.length });
    return results;
}






/**
 *  Optimizes indexed queries by merging `anyOf()` conditions and recognizing compound indexes.
 */
function optimizeIndexedQueries(indexedQueries, compoundIndexQueries) {
    if ((!indexedQueries || indexedQueries.length === 0) && (!compoundIndexQueries || compoundIndexQueries.length === 0)) {
        return { optimizedSingleIndexes: [], optimizedCompoundIndexes: [] };
    }

    let optimizedSingleIndexes = [];
    let optimizedCompoundIndexes = [];
    let groupedByProperty = {};

    // **Optimize Single-Field Indexed Queries**
    for (let query of indexedQueries) {
        for (let condition of query) {
            if (!groupedByProperty[condition.property]) {
                groupedByProperty[condition.property] = [];
            }
            groupedByProperty[condition.property].push(condition);
        }
    }

    for (let [property, conditions] of Object.entries(groupedByProperty)) {
        if (conditions.length > 1) {
            // Convert multiple `.Equal` conditions into `.anyOf()`
            if (conditions.every(c => c.operation === QUERY_OPERATIONS.EQUAL)) {
                optimizedSingleIndexes.push([{
                    property,
                    operation: QUERY_OPERATIONS.IN,
                    value: conditions.map(c => c.value)
                }]);
            }
            // Convert multiple `.StartsWith()` conditions into `.anyOf()`
            else if (conditions.every(c => c.operation === QUERY_OPERATIONS.STARTS_WITH)) {
                optimizedSingleIndexes.push([{
                    property,
                    operation: QUERY_OPERATIONS.IN,
                    value: conditions.map(c => c.value)
                }]);
            }
            // Combine range conditions into `.between()` if possible
            else if (
                conditions.some(c => c.operation.includes("Greater")) &&
                conditions.some(c => c.operation.includes("Less"))
            ) {
                let min = conditions.find(c => c.operation.includes("Greater")).value;
                let max = conditions.find(c => c.operation.includes("Less")).value;
                optimizedSingleIndexes.push([{
                    property,
                    operation: "between",
                    value: [min, max]
                }]);
            } else {
                optimizedSingleIndexes.push(conditions);
            }
        } else {
            optimizedSingleIndexes.push(conditions);
        }
    }

    // **Optimize Compound Indexed Queries (Fixing the `.map` issue)**
    for (let compoundQuery of compoundIndexQueries) {
        let propertyKeys = compoundQuery.properties.join("+"); // Ensure correct key format
        let conditions = compoundQuery.conditions; // Access conditions safely

        let canMerge = conditions.every(c => c.operation === QUERY_OPERATIONS.EQUAL);

        if (canMerge) {
            optimizedCompoundIndexes.push([{
                properties: compoundQuery.properties, // Keep original properties array
                operation: QUERY_OPERATIONS.IN,
                value: [conditions.map(c => c.value)] // Fix: Access conditions properly
            }]);
        } else {
            optimizedCompoundIndexes.push(conditions);
        }
    }

    if (optimizedSingleIndexes.length === 0 && optimizedCompoundIndexes.length === 0) {
        throw new Error("OptimizeIndexedQueries failed—No indexed queries were produced! Investigate input conditions.");
    }

    debugLog("Optimized Indexed Queries", { optimizedSingleIndexes, optimizedCompoundIndexes });

    return { optimizedSingleIndexes, optimizedCompoundIndexes };
}




/**
 * Executes a cursor-based query using Dexie's `each()` for efficient iteration.
 * This ensures that records are not duplicated if they match multiple OR conditions.
 *
 * @param {Object} table - The Dexie table instance.
 * @param {Array} conditionsArray - Array of OR groups containing AND conditions.
 * @returns {Promise<Array>} - Filtered results based on conditions.
 */
async function runCursorQuery(table, conditions, queryAdditions, yieldedPrimaryKeys, primaryKey) {
    debugLog("Running Cursor Query with Conditions", { conditions, queryAdditions });

    // **Extract metadata (primary keys & sorting properties)**
    let primaryKeyList = await runMetaDataCursorQuery(table, conditions, queryAdditions, yieldedPrimaryKeys, primaryKey);

    // **Apply sorting, take, and skip operations**
    let finalPrimaryKeys = applyCursorQueryAdditions(primaryKeyList, queryAdditions, primaryKey);

    // **Fetch only the required records from IndexedDB**
    let finalRecords = await fetchRecordsByPrimaryKeys(table, finalPrimaryKeys, primaryKey);

    debugLog("Final Cursor Query Records Retrieved", { count: finalRecords.length });
    return finalRecords; // Ready for yielding
}

/**
 * Extracts only the necessary metadata for cursor-based queries.
 * Returns an array of objects containing the primary key and only the required properties.
 */
async function runMetaDataCursorQuery(table, conditions, queryAdditions, yieldedPrimaryKeys, primaryKey) {
    debugLog("Extracting Metadata for Cursor Query", { conditions, queryAdditions });

    let primaryKeyList = []; // Store only necessary metadata
    let requiredProperties = new Set();
    let magicOrder = 0; // NEW: Counter to track cursor order

    // Extract properties used in filtering (conditions)
    for (const andGroup of conditions) {
        for (const condition of andGroup) {
            if (condition.property) requiredProperties.add(condition.property);
        }
    }

    // Extract properties needed for sorting
    for (const addition of queryAdditions) {
        if ((addition.additionFunction === QUERY_ADDITIONS.ORDER_BY || addition.additionFunction === QUERY_ADDITIONS.ORDER_BY_DESCENDING) &&
            addition.property) {
            requiredProperties.add(addition.property);
        }
    }

    // Always include the primary key
    requiredProperties.add(primaryKey);

    // NEW: Include _MagicOrderId in metadata
    requiredProperties.add("_MagicOrderId");

    debugLog("Properties needed for cursor processing", { requiredProperties: [...requiredProperties] });

    // Iterate over each record, storing only required properties
    await table.each(record => {
        if (yieldedPrimaryKeys.has(record[primaryKey])) return;

        if (conditions.some(andConditions => andConditions.every(condition => applyCondition(record, condition)))) {
            let sortingProperties = {};

            // Store necessary properties
            for (const prop of requiredProperties) {
                sortingProperties[prop] = record[prop];
            }

            // NEW: Assign a unique order ID based on cursor sequence
            sortingProperties["_MagicOrderId"] = magicOrder++;

            primaryKeyList.push({
                primaryKey: record[primaryKey],
                sortingProperties
            });
        }
    });

    debugLog("Primary Key List Collected", { count: primaryKeyList.length });
    return primaryKeyList;
}


/**
 *  Applies a single filtering condition on a record.
 */
function applyCondition(record, condition) {
    let recordValue = record[condition.property];
    let queryValue = condition.value;

    if (typeof recordValue === "string" && typeof queryValue === "string") {
        if (!condition.caseSensitive) {
            recordValue = recordValue.toLowerCase();
            queryValue = queryValue.toLowerCase();
        }
    }

    switch (condition.operation) {
        case QUERY_OPERATIONS.EQUAL:
            return recordValue === queryValue;
        case QUERY_OPERATIONS.NOT_EQUAL:
            return recordValue !== queryValue;
        case QUERY_OPERATIONS.GREATER_THAN:
            return recordValue > queryValue;
        case QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL:
            return recordValue >= queryValue;
        case QUERY_OPERATIONS.LESS_THAN:
            return recordValue < queryValue;
        case QUERY_OPERATIONS.LESS_THAN_OR_EQUAL:
            return recordValue <= queryValue;
        case QUERY_OPERATIONS.STARTS_WITH:
            return typeof recordValue === "string" && recordValue.startsWith(queryValue);
        case QUERY_OPERATIONS.CONTAINS:
            return typeof recordValue === "string" && recordValue.includes(queryValue);
        case QUERY_OPERATIONS.NOT_CONTAINS:
            return typeof recordValue === "string" && !recordValue.includes(queryValue);
        case QUERY_OPERATIONS.IN:
            return Array.isArray(queryValue) && queryValue.includes(recordValue);
        default:
            throw new Error(`Unsupported condition: ${condition.operation}`);
    }
}

async function fetchRecordsByPrimaryKeys(table, primaryKeys, primaryKeyName, batchSize = 500) {
    if (!primaryKeys || primaryKeys.length === 0) return [];

    debugLog(`Fetching ${primaryKeys.length} final objects in parallel batches of ${batchSize}`, { primaryKeys });

    let batchPromises = [];

    for (let i = 0; i < primaryKeys.length; i += batchSize) {
        let batch = primaryKeys.slice(i, i + batchSize);
        batchPromises.push(table.where(primaryKeyName).anyOf(batch).toArray()); // Uses correct primary key
    }

    // Execute all batches in parallel and wait for the slowest one
    let batchResults = await Promise.all(batchPromises);

    // Flatten the results into a single array
    return batchResults.flat();
}


function applyCursorQueryAdditions(primaryKeyList, queryAdditions, primaryKey, flipSkipTakeOrder = true) {
    if (!queryAdditions || queryAdditions.length === 0) {
        return primaryKeyList.map(item => item.primaryKey);
    }

    debugLog("Applying cursor query additions in strict given order", { queryAdditions });

    let additions = [...queryAdditions]; // Copy to avoid modifying the original array
    let needsReverse = false;

    // **Handle special IndexedDB SKIP/TAKE order flipping**
    if (flipSkipTakeOrder) {
        let takeIndex = additions.findIndex(a => a.additionFunction === QUERY_ADDITIONS.TAKE);
        let skipIndex = additions.findIndex(a => a.additionFunction === QUERY_ADDITIONS.SKIP);

        if (takeIndex !== -1 && skipIndex !== -1 && takeIndex < skipIndex) {
            debugLog("Flipping TAKE and SKIP order for cursor consistency");
            [additions[takeIndex], additions[skipIndex]] = [additions[skipIndex], additions[takeIndex]];
        }
    }

    // **Step 1: Sort primary keys by `_MagicOrderId` first (natural row order)**
    primaryKeyList.sort((a, b) => a.sortingProperties["_MagicOrderId"] - b.sortingProperties["_MagicOrderId"]);

    // **Step 2: Process Query Additions in Exact Order**
    for (const addition of additions) {
        switch (addition.additionFunction) {
            case QUERY_ADDITIONS.ORDER_BY:
            case QUERY_ADDITIONS.ORDER_BY_DESCENDING:
                primaryKeyList.sort((a, b) => {
                    let prop = addition.property;
                    let valueA = a.sortingProperties[prop];
                    let valueB = b.sortingProperties[prop];

                    if (valueA !== valueB) {
                        return addition.additionFunction === QUERY_ADDITIONS.ORDER_BY_DESCENDING
                            ? (valueB > valueA ? 1 : -1)
                            : (valueA > valueB ? 1 : -1);
                    }
                    return a.sortingProperties["_MagicOrderId"] - b.sortingProperties["_MagicOrderId"];
                });
                break;

            case QUERY_ADDITIONS.SKIP:
                primaryKeyList = primaryKeyList.slice(addition.intValue);
                break;

            case QUERY_ADDITIONS.TAKE:
                primaryKeyList = primaryKeyList.slice(0, addition.intValue);
                break;

            case QUERY_ADDITIONS.TAKE_LAST:
                needsReverse = true;
                primaryKeyList = primaryKeyList.slice(-addition.intValue);
                break;

            case QUERY_ADDITIONS.FIRST:
                primaryKeyList = primaryKeyList.length > 0 ? [primaryKeyList[0]] : [];
                break;

            case QUERY_ADDITIONS.LAST:
                primaryKeyList = primaryKeyList.length > 0 ? [primaryKeyList[primaryKeyList.length - 1]] : [];
                break;

            default:
                throw new Error(`Unsupported query addition: ${addition.additionFunction}`);
        }
    }

    // **Step 3: Reverse if TAKE_LAST was used**
    if (needsReverse) {
        primaryKeyList.reverse();
    }

    debugLog("Final Ordered Primary Key List", primaryKeyList);

    return primaryKeyList.map(item => item.primaryKey);
}


