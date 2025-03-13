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
let databases = [];

export function createDb(dbStore) {
    console.log("Debug: Received dbStore in createDb", dbStore);
    if (databases.find(d => d.name == dbStore.name) !== undefined)
        console.warn("Blazor.IndexedDB.Framework - Database already exists");

    const db = new Dexie(dbStore.name);

    const stores = {};
    for (let i = 0; i < dbStore.storeSchemas.length; i++) {
        // build string
        const schema = dbStore.storeSchemas[i];
        let def = "";
        if (schema.primaryKeyAuto)
            def = def + "++";
        if (schema.primaryKey !== null && schema.primaryKey !== "")
            def = def + schema.primaryKey;
        if (schema.uniqueIndexes !== undefined) {
            for (var j = 0; j < schema.uniqueIndexes.length; j++) {
                def = def + ",";
                var u = "&" + schema.uniqueIndexes[j];
                def = def + u;
            }
        }
        if (schema.indexes !== undefined) {
            for (var j = 0; j < schema.indexes.length; j++) {
                def = def + ",";
                var u = schema.indexes[j];
                def = def + u;
            }
        }
        stores[schema.name] = def;
    }
    db.version(dbStore.version).stores(stores);
    if (databases.find(d => d.name == dbStore.name) !== undefined) {
        databases.find(d => d.name == dbStore.name).db = db;
    }
    else {
        databases.push({
            name: dbStore.name,
            db: db
        });
    }
    db.open();
}

export function closeAll() {
    const dbs = databases;
    databases = [];
    for (db of dbs)
        db.db.close();
}

export async function deleteDb(dbName) {
    const db = await getDb(dbName);
    const index = databases.findIndex(d => d.name == dbName);
    databases.splice(index, 1);
    db.delete();
}

export async function addItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    return await table.add(item.record);
}

export async function bulkAddItem(dbName, storeName, items) {
    const table = await getTable(dbName, storeName);
    return await table.bulkAdd(items);
}

export async function countTable(dbName, storeName) {
    const table = await getTable(dbName, storeName);
    return await table.count();
}

export async function putItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    return await table.put(item.record);
}

export async function updateItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    return await table.update(item.key, item.record);
}

export async function bulkUpdateItem(items) {
    const table = await getTable(items[0].dbName, items[0].storeName);
    let updatedCount = 0;
    let errors = false;

    for (const item of items) {
        try {
            await table.update(item.key, item.record);
            updatedCount++;
        }
        catch (e) {
            console.error(e);
            errors = true;
        }
    }

    if (errors)
        throw new Error('Some items could not be updated');
    else
        return updatedCount;
}

export async function bulkDelete(dbName, storeName, keys) {
    const table = await getTable(dbName, storeName);
    let deletedCount = 0;
    let errors = false;

    for (const key of keys) {
        try {
            await table.delete(key);
            deletedCount++;
        }
        catch (e) {
            console.error(e);
            errors = true;
        }
    }

    if (errors)
        throw new Error('Some items could not be deleted');
    else
        return deletedCount;
}

export async function deleteItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    await table.delete(item.key)
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
    [QUERY_ADDITIONS.ORDER_BY]: [QUERY_ADDITIONS.SKIP, QUERY_ADDITIONS.TAKE],
    [QUERY_ADDITIONS.ORDER_BY_DESCENDING]: [QUERY_ADDITIONS.SKIP, QUERY_ADDITIONS.TAKE],
    [QUERY_ADDITIONS.FIRST]: [],
    [QUERY_ADDITIONS.LAST]: [],
    [QUERY_ADDITIONS.SKIP]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING],
    [QUERY_ADDITIONS.TAKE]: [QUERY_ADDITIONS.ORDER_BY, QUERY_ADDITIONS.ORDER_BY_DESCENDING],
    [QUERY_ADDITIONS.TAKE_LAST]: [],
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

export async function where(dbName, storeName, nestedOrFilter, QueryAdditions) {
    debugLog("whereJson called");


    let results = []; // Collect results here

    for await (let record of whereYield(dbName, storeName, nestedOrFilter, QueryAdditions)) {
        results.push(record);
    }

    debugLog("whereJson returning results", { count: results.length, results });

    return results; // Return all results at once
}

export async function* whereYield(dbName, storeName, nestedOrFilter, queryAdditions = []) {
    debugLog("Starting where function", { dbName, storeName, nestedOrFilter, queryAdditions });

    if (!isValidFilterObject(nestedOrFilter)) {
        throw new Error("Invalid filter object provided to where function.");
    }

    let db = await getDb(dbName);
    let table = db.table(storeName);

    if (!indexCache[dbName]) indexCache[dbName] = {};
    if (!indexCache[dbName][storeName]) {
        const schema = table.schema;
        indexCache[dbName][storeName] = { indexes: {}, primaryKey: schema.primKey.name };

        for (const index of schema.indexes) {
            indexCache[dbName][storeName].indexes[index.name] = true;
        }
    }

    let primaryKey = indexCache[dbName][storeName].primaryKey;
    let yieldedPrimaryKeys = new Set(); // Track already returned records

    debugLog("Validated schema & cached indexes", { primaryKey, indexes: indexCache[dbName][storeName].indexes });

    let requiresCursor = validateQueryAdditions(queryAdditions, indexCache, dbName, storeName) || validateQueryCombinations(nestedOrFilter);
    debugLog("Determined if query requires cursor", { requiresCursor });

    if (!nestedOrFilter.orGroups || nestedOrFilter.orGroups.length === 0) {
        debugLog("No filtering conditions detected. Fetching entire table.");
        let allRecords = await table.toArray();
        for (let record of applyIndexedQueryAdditions(table, allRecords, queryAdditions)) {
            if (!yieldedPrimaryKeys.has(record[primaryKey])) {
                yieldedPrimaryKeys.add(record[primaryKey]);
                debugLog("Yielding record", record);
                yield record;
            }
        }
        return;
    }

    // Indexed Queries will store fully indexed AND groups that can be optimized with IndexedDB
    let indexedQueries = [];

    // Cursor Conditions will store AND groups that must be processed manually in JavaScript
    let cursorConditions = [];

    /**
     * Iterate over the structured OR groups (|| logic)
     */
    for (const orGroup of nestedOrFilter.orGroups) {
        if (!orGroup.andGroups || orGroup.andGroups.length === 0) continue;

        for (const andGroup of orGroup.andGroups) { // Each OR group contains multiple AND conditions
            if (!andGroup.conditions || andGroup.conditions.length === 0) continue;

            let indexedConditions = [];
            let needsCursor = false;

            for (const condition of andGroup.conditions) { // Each condition within the AND group
                // Ensure the condition is valid
                if (!condition || typeof condition !== "object" || !condition.operation) {
                    debugLog("Skipping invalid condition", { condition });
                    continue;
                }

                // Determine if this condition is indexed
                const isIndexed = indexCache[dbName][storeName].indexes[condition.property] || false;
                condition.isIndex = isIndexed;

                /**
                 * If even **one** condition in an AND group is not indexed,
                 * we **must** process the entire AND group using a cursor.
                 */
                if (!isIndexed || !isSupportedIndexedOperation([condition])) {
                    needsCursor = true;
                    break; // Stop processing and mark this entire AND group for cursors
                } else {
                    indexedConditions.push(condition);
                }
            }

            /**
             * If **any condition in an AND group** requires a cursor, 
             * we push the **entire AND group** to `cursorConditions`.
             * 
             * Otherwise, if **all conditions were indexed**, we push 
             * them to `indexedQueries` for optimized IndexedDB execution.
             */
            if (needsCursor) {
                cursorConditions.push(andGroup.conditions); // Push entire AND group to cursor
            } else {
                indexedQueries.push(indexedConditions); // Push fully indexed AND group
            }
        }
    }

    /**
     * Summary of Logic:
     * - **Indexed Queries (`indexedQueries`)**  Fully indexed AND groups that can be run efficiently.
     * - **Cursor Conditions (`cursorConditions`)**  Groups requiring JavaScript-based filtering.
     * - **OR (`||`) relationships remain intact** because we handle indexed queries separately 
     *   from cursor-based queries and later merge their results.
     */
    debugLog("Indexed Queries vs Cursor Queries", { indexedQueries, cursorConditions });

    let optimizedIndexedQueries = optimizeIndexedQueries(indexedQueries);
    debugLog("Optimized Indexed Queries", { optimizedIndexedQueries });

    // Process Indexed Queries First
    // Memory safe by removing from memory as we send back to prevent double memory at any one point
    for (let query of optimizedIndexedQueries) {
        let records = await runIndexedQuery(table, query); // Load records first

        while (records.length > 0) {
            let record = records.shift(); // Remove first item (Frees memory)
            if (!yieldedPrimaryKeys.has(record[primaryKey])) {
                yieldedPrimaryKeys.add(record[primaryKey]);
                yield record;
            }
        }
    }


    // Pass yielded primary keys into cursor query to **skip already processed results**
    let cursorResults = await runCursorQuery(table, cursorConditions, yieldedPrimaryKeys);
    debugLog("Cursor Query Results Count", { count: cursorResults.length });

    // Apply query additions (sorting, skipping, taking, etc.)
    cursorResults = applyCursorQueryAdditions(cursorResults, queryAdditions);

    // Yield final cursor-based records
    for (let record of cursorResults) {
        yieldedPrimaryKeys.add(record[primaryKey]); // **Ensure it's tracked**
        yield record;
    }
}


function validateQueryAdditions(queryAdditions, indexCache, dbName, storeName) {
    queryAdditions = queryAdditions || []; // Ensure it's always an array
    let seenAdditions = new Set();
    let requiresCursor = false;

    for (const addition of queryAdditions) {
        let validCombos = QUERY_ADDITION_RULES[addition.Name];

        if (!validCombos) {
            throw new Error(`Unsupported query addition: ${addition.Name}`);
        }

        // **New Validation: ORDER_BY & ORDER_BY_DESCENDING Must Target Indexed Properties**
        if (addition.Name === QUERY_ADDITIONS.ORDER_BY || addition.Name === QUERY_ADDITIONS.ORDER_BY_DESCENDING) {
            const isIndexed = indexCache[dbName]?.[storeName]?.indexes?.[addition.StringValue] || false;
            if (!isIndexed) {
                debugLog(`Query requires cursor: ORDER_BY on non-indexed property ${addition.StringValue}`);
                requiresCursor = true; // Forces cursor usage
            }
        }

        // Check if the addition conflicts with previous additions
        for (const seen of seenAdditions) {
            if (!validCombos.includes(seen)) {
                requiresCursor = true;
                break;
            }
        }

        seenAdditions.add(addition.Name);
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
async function runIndexedQuery(table, indexedConditions) {
    debugLog("Executing runIndexedQuery", { indexedConditions });

    if (indexedConditions.length === 0) {
        debugLog("No indexed conditions provided, returning entire table.");
        return await table.toArray();
    }

    let query;

    // **Process the first condition as the main query**
    let firstCondition = indexedConditions[0];

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

    // **Apply additional indexed filters (if any)**
    for (let i = 1; i < indexedConditions.length; i++) {
        let condition = indexedConditions[i];

        switch (condition.operation) {
            case QUERY_OPERATIONS.EQUAL:
                query = query.and(record => record[condition.property] === condition.value);
                break;
            case QUERY_OPERATIONS.GREATER_THAN:
                query = query.and(record => record[condition.property] > condition.value);
                break;
            case QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL:
                query = query.and(record => record[condition.property] >= condition.value);
                break;
            case QUERY_OPERATIONS.LESS_THAN:
                query = query.and(record => record[condition.property] < condition.value);
                break;
            case QUERY_OPERATIONS.LESS_THAN_OR_EQUAL:
                query = query.and(record => record[condition.property] <= condition.value);
                break;
            case QUERY_OPERATIONS.STARTS_WITH:
                query = query.and(record => record[condition.property].startsWith(condition.value));
                break;
            default:
                throw new Error(`Unsupported indexed query operation: ${condition.operation}`);
        }
    }

    return await query.toArray();
}


/**
 *  Optimizes indexed queries by merging `anyOf()` conditions.
 */
function optimizeIndexedQueries(indexedQueries) {
    if (!indexedQueries || indexedQueries.length === 0) {
        return [];
    }

    let optimized = [];
    let groupedByProperty = {};

    // Group conditions by property for better optimization
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
                optimized.push([{
                    property,
                    operation: QUERY_OPERATIONS.IN,
                    value: conditions.map(c => c.value)
                }]);
            }
            // Convert multiple `.StartsWith()` conditions into `.anyOf()`
            else if (conditions.every(c => c.operation === QUERY_OPERATIONS.STARTS_WITH)) {
                optimized.push([{
                    property,
                    operation: QUERY_OPERATIONS.IN,
                    value: conditions.map(c => c.value)
                }]);
            }
            // Combine range conditions into `.between()` if possible
            else if (
                conditions.some(c => c.operation === QUERY_OPERATIONS.GREATER_THAN || c.operation === QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL) &&
                conditions.some(c => c.operation === QUERY_OPERATIONS.LESS_THAN || c.operation === QUERY_OPERATIONS.LESS_THAN_OR_EQUAL)
            ) {
                let min = conditions.find(c => c.operation.includes("Greater")).value;
                let max = conditions.find(c => c.operation.includes("Less")).value;
                optimized.push([{
                    property,
                    operation: "between",
                    value: [min, max]
                }]);
            }
            else {
                optimized.push(conditions);
            }
        } else {
            optimized.push(conditions);
        }
    }

    if (optimized.length === 0) {
        throw new Error("OptimizeIndexedQueries failed—No indexed queries were produced! Investigate input conditions.");
    }

    debugLog("Optimized Indexed Queries", { optimized });

    return optimized;
}

/**
 * Executes a cursor-based query using Dexie's `each()` for efficient iteration.
 * This ensures that records are not duplicated if they match multiple OR conditions.
 *
 * @param {Object} table - The Dexie table instance.
 * @param {Array} conditionsArray - Array of OR groups containing AND conditions.
 * @returns {Promise<Array>} - Filtered results based on conditions.
 */
async function runCursorQuery(table, conditions, yieldedPrimaryKeys) {
    debugLog("Running Cursor Query with Conditions", { conditions });

    let results = [];

    await table.each(record => {
        if (yieldedPrimaryKeys.has(record.id)) return; // Skip if already yielded

        if (conditions.some(andConditions => andConditions.every(condition => applyCondition(record, condition)))) {
            debugLog("Adding record from Cursor Query", record);
            results.push(record);
        }
    });

    debugLog("Cursor Query Completed", { count: results.length });
    return results;
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





function applyIndexedQueryAdditions(table, results, queryAdditions) {
    if (!queryAdditions || queryAdditions.length === 0) return results;

    debugLog("Applying indexed query additions in given order", { queryAdditions });

    for (const addition of queryAdditions) {
        switch (addition.Name) {
            case QUERY_ADDITIONS.ORDER_BY:
                results = results.orderBy(addition.StringValue);
                break;
            case QUERY_ADDITIONS.ORDER_BY_DESCENDING:
                results = results.orderBy(addition.StringValue).reverse();
                break;
            case QUERY_ADDITIONS.SKIP:
                results = results.offset(addition.IntValue);
                break;
            case QUERY_ADDITIONS.TAKE:
                results = results.limit(addition.IntValue);
                break;
            case QUERY_ADDITIONS.TAKE_LAST:
                results = results.reverse().limit(addition.IntValue).reverse(); // Ensures last `n` items are taken in order
                break;
            default:
                throw new Error(`Unsupported query addition: ${addition.Name}`);
        }
    }

    return results;
}


function applyCursorQueryAdditions(results, queryAdditions) {
    if (!queryAdditions || queryAdditions.length === 0) return results;

    debugLog("Applying cursor query additions in given order", { queryAdditions });

    for (const addition of queryAdditions) {
        switch (addition.Name) {
            case QUERY_ADDITIONS.ORDER_BY:
                results.sort((a, b) => a[addition.StringValue] - b[addition.StringValue]);
                break;
            case QUERY_ADDITIONS.ORDER_BY_DESCENDING:
                results.sort((a, b) => b[addition.StringValue] - a[addition.StringValue]);
                break;
            case QUERY_ADDITIONS.SKIP:
                results = results.slice(addition.IntValue);
                break;
            case QUERY_ADDITIONS.TAKE:
                results = results.slice(0, addition.IntValue);
                break;
            case QUERY_ADDITIONS.TAKE_LAST:
                results = results.slice(-addition.IntValue);
                break;
            default:
                throw new Error(`Unsupported query addition: ${addition.Name}`);
        }
    }

    return results;
}



async function getDb(dbName) {
    if (databases.find(d => d.name == dbName) === undefined) {
        console.warn("Blazor.IndexedDB.Framework - Database doesn't exist");
        var db1 = new Dexie(dbName);
        await db1.open();
        if (databases.find(d => d.name == dbName) !== undefined) {
            databases.find(d => d.name == dbName).db = db1;
        } else {
            databases.push({
                name: dbName,
                db: db1
            });
        }
        return db1;
    }
    else {
        return databases.find(d => d.name == dbName).db;
    }
}

async function getTable(dbName, storeName) {
    let db = await getDb(dbName);
    let table = db.table(storeName);
    return table;
}

function createFilterObject(filters) {
    const jsonFilter = {};
    for (const filter in filters) {
        if (filters.hasOwnProperty(filter))
            jsonFilter[filters[filter].indexName] = filters[filter].filterValue;
    }
    return jsonFilter;
}

function getAll(dotnetReference, transaction, dbName, storeName) {
    return new Promise((resolve, reject) => {
        getTable(dbName, storeName).then(table => {
            table.toArray().then(items => {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'getAll succeeded');
                resolve(items);
            }).catch(e => {
                console.error(e);
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'getAll failed');
                reject(e);
            });
        });
    });
}

export function encryptString(data, key) {
    // Convert the data to an ArrayBuffer
    let dataBuffer = new TextEncoder().encode(data).buffer;

    // Generate a random initialization vector
    let iv = crypto.getRandomValues(new Uint8Array(16));

    // Convert the key to an ArrayBuffer
    let keyBuffer = new TextEncoder().encode(key).buffer;

    // Create a CryptoKey object from the key buffer
    return crypto.subtle.importKey('raw', keyBuffer, { name: 'AES-CBC' }, false, ['encrypt'])
        .then(key => {
            // Encrypt the data with AES-CBC encryption
            return crypto.subtle.encrypt({ name: 'AES-CBC', iv }, key, dataBuffer);
        })
        .then(encryptedDataBuffer => {
            // Concatenate the initialization vector and encrypted data
            let encryptedData = new Uint8Array(encryptedDataBuffer);
            let encryptedDataWithIV = new Uint8Array(encryptedData.byteLength + iv.byteLength);
            encryptedDataWithIV.set(iv);
            encryptedDataWithIV.set(encryptedData, iv.byteLength);

            // Convert the encrypted data to a base64 string and return it
            return btoa(String.fromCharCode.apply(null, encryptedDataWithIV));
        });
}

export function decryptString(encryptedData, key) {
    // Convert the base64 string to a Uint8Array
    let encryptedDataWithIV = new Uint8Array(atob(encryptedData).split('').map(c => c.charCodeAt(0)));
    let iv = encryptedDataWithIV.slice(0, 16);
    let data = encryptedDataWithIV.slice(16);

    // Convert the key to an ArrayBuffer
    let keyBuffer = new TextEncoder().encode(key).buffer;

    // Create a CryptoKey object from the key buffer
    return crypto.subtle.importKey('raw', keyBuffer, { name: 'AES-CBC' }, false, ['decrypt'])
        .then(key => {
            // Decrypt the data with AES-CBC decryption
            return crypto.subtle.decrypt({ name: 'AES-CBC', iv }, key, data);
        })
        .then(decryptedDataBuffer => {
            // Convert the decrypted data to a string and return it
            return new TextDecoder().decode(decryptedDataBuffer);
        });
}