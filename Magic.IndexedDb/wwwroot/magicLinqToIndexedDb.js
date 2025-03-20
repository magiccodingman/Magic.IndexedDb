"use strict";
import { partitionQueryConditions } from "./utilities/partitionLinqQueries.js";
import { QUERY_OPERATIONS, QUERY_ADDITIONS } from "./utilities/queryConstants.js";
import {
    buildIndexMetadata, normalizeCompoundKey,
    hasYieldedKey, addYieldedKey, debugLog
} from "./utilities/utilityHelpers.js";

import { initiateNestedOrFilter } from "./utilities/nestedOrFilterUtilities.js";


export async function magicQueryAsync(db, table, nestedOrFilter,
    QueryAdditions, forceCursor = false) {
    debugLog("whereJson called");


    let results = []; // Collect results here

    for await (let record of magicQueryYield(db, table, nestedOrFilter,
        QueryAdditions, forceCursor)) {
        results.push(record);
    }

    debugLog("whereJson returning results", { count: results.length, results });

    return results; // Return all results at once
}

export async function* magicQueryYield(db, table, nestedOrFilterUnclean,
    queryAdditions = [], forceCursor = false) {

    if (!table || !(table instanceof table.constructor)) {
        throw new Error("A valid Dexie table instance must be provided.");
    }

    debugLog("Starting where function", { nestedOrFilterUnclean, queryAdditions });

    let indexCache = buildIndexMetadata(table);
    let primaryKeys = [...indexCache.compoundKeys];

    let yieldedPrimaryKeys = new Map(); // **Structured compound key tracking**

    debugLog("Validated schema & cached indexes", { primaryKeys, indexes: indexCache.indexes });

    let { isFilterEmpty, nestedOrFilter } =
        initiateNestedOrFilter(nestedOrFilterUnclean, queryAdditions, primaryKeys);

    // No need for processing anything, we can just immediately return results.
    if (isFilterEmpty) {
        debugLog("No filtering or query additions. Fetching entire table.");
        let allRecords = await table.toArray();

        while (allRecords.length > 0) {
            let record = allRecords.shift(); // Remove from memory before processing
            let recordKey = normalizeCompoundKey(primaryKeys, record);

            if (!hasYieldedKey(yieldedPrimaryKeys, recordKey)) {
                addYieldedKey(yieldedPrimaryKeys, recordKey);
                yield record;
            }
        }
        return;
    }

    let { indexedQueries, compoundIndexQueries, cursorConditions } =
        partitionQueryConditions(nestedOrFilter, queryAdditions, indexCache, forceCursor);

    debugLog("Final Indexed Queries vs. Compound Queries vs. Cursor Queries", { indexedQueries, compoundIndexQueries, cursorConditions });

    if (indexedQueries.length > 0 || compoundIndexQueries.length > 0) {
        let { optimizedSingleIndexes, optimizedCompoundIndexes } = optimizeIndexedQueries(indexedQueries, compoundIndexQueries);
        debugLog("Optimized Indexed Queries", { optimizedSingleIndexes, optimizedCompoundIndexes });

        let allOptimizedQueries = [...optimizedSingleIndexes, ...optimizedCompoundIndexes];

        // ** Execute queries in parallel and get a streamed result set**
        let results = await runIndexedQueries(db, table, allOptimizedQueries,
            queryAdditions, primaryKeys, yieldedPrimaryKeys);

        // **Process records one at a time, maintaining low memory usage**
        while (results.length > 0) {
            let record = results.shift(); // ** Immediately remove from memory**
            yield record;
        }
    }



    if (Array.isArray(cursorConditions) && cursorConditions.length > 0) {
        let cursorResults = await runCursorQuery(db, table, cursorConditions, queryAdditions, yieldedPrimaryKeys, primaryKeys);
        debugLog("Cursor Query Results Count", { count: cursorResults.length });

        while (cursorResults.length > 0) {
            let record = cursorResults.shift(); // Remove the first record from memory immediately
            let recordKey = normalizeCompoundKey(primaryKeys, record);

            if (!hasYieldedKey(yieldedPrimaryKeys, recordKey)) {
                addYieldedKey(yieldedPrimaryKeys, recordKey);
                yield record; // The record no longer exists in cursorResults after yielding
            }
        }
    }

}

async function runIndexedQueries(db, table, universalQueries,
    queryAdditions, primaryKeys, yieldedPrimaryKeys) {
    if (universalQueries.length === 0) {
        debugLog("No indexed conditions provided, returning entire table.");
        return await table.toArray(); // Immediate return if no conditions
    }

    let queries = [];

    for (let query of universalQueries) {
        let q = runIndexedQuery(table, query, queryAdditions);
        queries.push(q);
    }

    let finalResults = [];

    await Promise.all(
        queries.map(async (q) => {
            await db.transaction('r', table, async () => {
                // **Check if it's a single-record result (`first()` or `last()`)**
                if (q instanceof Promise) {
                    let record = await q; // Get single result

                    if (record) { // Ensure it's not null
                        let recordKey = normalizeCompoundKey(primaryKeys, record);
                        if (!hasYieldedKey(yieldedPrimaryKeys, recordKey)) {
                            addYieldedKey(yieldedPrimaryKeys, recordKey);
                            finalResults.push(record);
                        }
                    }
                }
                // **Otherwise, it's a collection, so process with `.each()`**
                else {
                    await q.each((record) => {
                        let recordKey = normalizeCompoundKey(primaryKeys, record);
                        if (!hasYieldedKey(yieldedPrimaryKeys, recordKey)) {
                            addYieldedKey(yieldedPrimaryKeys, recordKey);
                            finalResults.push(record);
                        }
                    });
                }
            });
        })
    );

    return finalResults;
}




/**
 * Executes an indexed query using IndexedDB.
 * @param {Object} table - The Dexie table instance.
 * @param {Array} indexedConditions - The array of indexed conditions.
 * @returns {AsyncGenerator} - A generator that yields query results.
 */
function runIndexedQuery(table, indexedConditions, queryAdditions = []) {
    debugLog("Executing runIndexedQuery", { indexedConditions, queryAdditions });

    let query;
    let firstCondition = indexedConditions[0];

    // **Handle Compound Index Query**
    if (Array.isArray(firstCondition.properties)) {
        debugLog("Detected Compound Index Query!", { properties: firstCondition.properties });

        // **Extract values in the correct order**
        const valuesInCorrectOrder = firstCondition.properties.map((_, index) => firstCondition.value[index]);

        debugLog("Compound Index Query - Ordered Properties & Values", {
            properties: firstCondition.properties,
            values: valuesInCorrectOrder
        });

        query = table.where(firstCondition.properties);

        if (firstCondition.operation === QUERY_OPERATIONS.EQUAL) {
            query = query.equals(valuesInCorrectOrder);
        }
        else if (firstCondition.operation === QUERY_OPERATIONS.IN) {
            query = query.anyOf(firstCondition.value);
        }
        else {
            throw new Error(`Unsupported operation for compound indexes: ${firstCondition.operation}`);
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
    }
    else {
        throw new Error("Invalid indexed condition—missing `properties` or `property`.");
    }

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
                query = query.limit(addition.intValue);
                break;
            case QUERY_ADDITIONS.TAKE_LAST:
                query = query.reverse().limit(addition.intValue);
                break;
            case QUERY_ADDITIONS.FIRST:
                return query.first();
            case QUERY_ADDITIONS.LAST:
                return query.last();
            default:
                throw new Error(`Unsupported query addition: ${addition.additionFunction}`);
        }
    }

    return query;
}


/**
 * Optimizes indexed queries by merging `anyOf()` conditions and recognizing compound indexes.
 */
function optimizeIndexedQueries(indexedQueries, compoundIndexQueries) {
    if ((!indexedQueries || indexedQueries.length === 0) && (!compoundIndexQueries || compoundIndexQueries.length === 0)) {
        return { optimizedSingleIndexes: [], optimizedCompoundIndexes: [] };
    }

    debugLog("Optimizing Indexed Queries", { indexedQueries, compoundIndexQueries });

    // Optimize single-index queries normally
    let optimizedSingleIndexes = optimizeIndexedOnlyQueries(indexedQueries);

    // Optimize compound queries, ensuring fallbackSingleIndexes is always an array
    let { optimizedCompoundIndexes = [], fallbackSingleIndexes = [] } = optimizeCompoundIndexedOnlyQueries(compoundIndexQueries);

    // **Merge any single-index fallbacks from compound queries**
    optimizedSingleIndexes.push(...fallbackSingleIndexes);

    if (optimizedSingleIndexes.length === 0 && optimizedCompoundIndexes.length === 0) {
        throw new Error("OptimizeIndexedQueries failed—No indexed queries were produced! Investigate input conditions.");
    }

    debugLog("Final Optimized Queries", { optimizedSingleIndexes, optimizedCompoundIndexes });

    return { optimizedSingleIndexes, optimizedCompoundIndexes };
}



/**
 * Optimizes single-field indexed queries (e.g., `where("field")`).
 */
function optimizeIndexedOnlyQueries(indexedQueries) {
    if (!indexedQueries || indexedQueries.length === 0) return [];

    let optimizedSingleIndexes = [];
    let groupedByProperty = {};

    // Group queries by property name
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

    return optimizedSingleIndexes;
}

/**
 * Optimizes compound-indexed queries (e.g., `where(["field1", "field2"])`).
 * If a compound query cannot be optimized, it falls back to single-index queries.
 */
function optimizeCompoundIndexedOnlyQueries(compoundIndexQueries) {
    if (!compoundIndexQueries || compoundIndexQueries.length === 0) {
        return { optimizedCompoundIndexes: [], fallbackSingleIndexes: [] };
    }

    let optimizedCompoundIndexes = [];
    let fallbackToSingleIndex = []; // Store compound queries that need to be handled as single-index

    for (let compoundQuery of compoundIndexQueries) {
        let conditions = compoundQuery.conditions;
        let properties = compoundQuery.properties;

        let canUseEquals = conditions.every(c => c.operation === QUERY_OPERATIONS.EQUAL);

        if (canUseEquals) {
            // **Use .equals() for compound indexes when possible**
            optimizedCompoundIndexes.push([{
                properties,
                operation: QUERY_OPERATIONS.EQUAL,
                value: conditions.map(c => c.value) // Ordered values
            }]);
        } else {
            // **If the compound query cannot be optimized, pass conditions to single-index processing**
            debugLog("Cannot optimize compound index due to unsupported operations. Falling back to single-index processing.", { compoundQuery });
            fallbackToSingleIndex.push(...conditions);
        }
    }

    // **Run single-index optimization on fallback queries**
    let fallbackSingleIndexes = fallbackToSingleIndex.length > 0
        ? optimizeIndexedOnlyQueries([fallbackToSingleIndex])
        : [];

    return { optimizedCompoundIndexes, fallbackSingleIndexes };
}




/**
 * Executes a cursor-based query using Dexie's `each()` for efficient iteration.
 * This ensures that records are not duplicated if they match multiple OR conditions.
 *
 * @param {Object} table - The Dexie table instance.
 * @param {Array} conditionsArray - Array of OR groups containing AND conditions.
 * @returns {Promise<Array>} - Filtered results based on conditions.
 */
async function runCursorQuery(db, table, conditions, queryAdditions, yieldedPrimaryKeys, compoundKeys) {
    debugLog("Running Cursor Query with Conditions", { conditions, queryAdditions });

    // **Extract metadata (compound keys & sorting properties)**
    let primaryKeyList = await runMetaDataCursorQuery(db, table, conditions, queryAdditions, yieldedPrimaryKeys, compoundKeys);

    // **Apply sorting, take, and skip operations**
    let finalPrimaryKeys = applyCursorQueryAdditions(primaryKeyList, queryAdditions, compoundKeys);

    // **Fetch only the required records from IndexedDB**
    let finalRecords = await fetchRecordsByPrimaryKeys(table, finalPrimaryKeys, compoundKeys);

    debugLog("Final Cursor Query Records Retrieved", { count: finalRecords.length });
    return finalRecords; // Ready for yielding
}

let lastCursorWarningTime = null;

/**
 * Extracts only the necessary metadata for cursor-based queries.
 * Returns an array of objects containing the primary key and only the required properties.
 */
/**
 * Extracts only necessary metadata using a Dexie cursor in a transaction.
 * Returns an array of objects containing the primary key and required properties.
 */
/**
 * Extracts only necessary metadata using a Dexie cursor in a transaction.
 * Returns an array of objects containing the primary key and required properties.
 */
async function runMetaDataCursorQuery(db, table, conditions, queryAdditions, yieldedPrimaryKeys, compoundKeys) {
    debugLog("Extracting Metadata for Cursor Query", { conditions, queryAdditions });

    let requiredProperties = new Set();
    let magicOrder = 0; // Counter to track cursor order

    // **Extract properties used in filtering (conditions)**
    for (const andGroup of conditions) {
        for (const condition of andGroup) {
            if (condition.property) requiredProperties.add(condition.property);
        }
    }

    // **Extract properties needed for sorting**
    for (const addition of queryAdditions) {
        if ((addition.additionFunction === QUERY_ADDITIONS.ORDER_BY || addition.additionFunction === QUERY_ADDITIONS.ORDER_BY_DESCENDING) &&
            addition.property) {
            requiredProperties.add(addition.property);
        }
    }

    // **Always include all compound keys**
    for (const key of compoundKeys) {
        requiredProperties.add(key);
    }

    // Include `_MagicOrderId` in metadata for sorting
    requiredProperties.add("_MagicOrderId");

    debugLog("Properties needed for cursor processing", { requiredProperties: [...requiredProperties] });

    let primaryKeyList = [];
    let lastCursorWarningTime = null; // Throttle warnings

    const optimizedConditions = conditions.map(optimizeConditions);

    // **Transaction-based read for parallel execution**
    await db.transaction('r', table, async () => {
        let cursor = await table.orderBy(compoundKeys[0]).each((record) => {
            let missingProperties = [];

            // **Check for missing required properties (excluding `_MagicOrderId`)**
            for (const prop of requiredProperties) {
                if (prop !== "_MagicOrderId" && record[prop] === undefined) {
                    missingProperties.push(prop);
                }
            }

            if (missingProperties.length > 0) {
                // **Throttle warning to prevent console spam (once every 10 minutes)**
                let now = Date.now();
                if (!lastCursorWarningTime || now - lastCursorWarningTime > 10 * 60 * 1000) {
                    console.warn(`[IndexedDB Cursor Warning] Skipping record due to missing properties: ${missingProperties.join(", ")}`);
                    lastCursorWarningTime = now;
                }
                return; // Skip this record
            }

            // **Extract compound key**
            let recordKey = normalizeCompoundKey(compoundKeys, record);

            // **Skip already yielded records**
            if (hasYieldedKey(yieldedPrimaryKeys, recordKey)) {
                return;
            }

            // **Apply filtering conditions**
            if (optimizedConditions.some(andConditions => andConditions.every(condition => applyCondition(record, condition)))) {
                let sortingProperties = {};

                // **Store only necessary properties**
                for (const prop of requiredProperties) {
                    sortingProperties[prop] = record[prop];
                }

                sortingProperties["_MagicOrderId"] = magicOrder++;

                // **Yield records immediately instead of storing in memory**
                primaryKeyList.push({
                    primaryKey: recordKey,
                    sortingProperties
                });
            }
        });
    });

    debugLog("Primary Key List Collected", { count: primaryKeyList.length });
    return primaryKeyList;
}


function optimizeConditions(conditions) {
    return conditions.map(condition => {
        let optimizedCondition = { ...condition };

        // Precompute case-insensitive values if applicable
        if (!condition.caseSensitive && typeof condition.value === "string") {
            optimizedCondition.value = condition.value.toLowerCase();
        }

        // Create a direct function reference instead of switch case inside the loop
        optimizedCondition.comparisonFunction = getComparisonFunction(condition.operation);

        return optimizedCondition;
    });
}

function getComparisonFunction(operation) {
    const operations = {
        [QUERY_OPERATIONS.EQUAL]: (recordValue, queryValue) => recordValue === queryValue,
        [QUERY_OPERATIONS.NOT_EQUAL]: (recordValue, queryValue) => recordValue !== queryValue,
        [QUERY_OPERATIONS.GREATER_THAN]: (recordValue, queryValue) => recordValue > queryValue,
        [QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => recordValue >= queryValue,
        [QUERY_OPERATIONS.LESS_THAN]: (recordValue, queryValue) => recordValue < queryValue,
        [QUERY_OPERATIONS.LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => recordValue <= queryValue,
        [QUERY_OPERATIONS.STARTS_WITH]: (recordValue, queryValue) =>
            typeof recordValue === "string" && recordValue.startsWith(queryValue),
        [QUERY_OPERATIONS.CONTAINS]: (recordValue, queryValue) =>
            typeof recordValue === "string" && recordValue.includes(queryValue),
        [QUERY_OPERATIONS.NOT_CONTAINS]: (recordValue, queryValue) =>
            typeof recordValue === "string" && !recordValue.includes(queryValue),
        [QUERY_OPERATIONS.IN]: (recordValue, queryValue) =>
            Array.isArray(queryValue) && queryValue.includes(recordValue)
    };

    return operations[operation] || (() => {
        throw new Error(`Unsupported condition: ${operation}`);
    });
}

function applyCondition(record, condition) {
    let recordValue = record[condition.property];

    // Convert to lowercase only if needed (precomputed query value)
    if (!condition.caseSensitive && typeof recordValue === "string") {
        recordValue = recordValue.toLowerCase();
    }

    // Use the precomputed function reference for comparison
    return condition.comparisonFunction(recordValue, condition.value);
}


async function fetchRecordsByPrimaryKeys(table, primaryKeys, compoundKeys, batchSize = 500) {
    if (!primaryKeys || primaryKeys.length === 0) return [];

    debugLog(`Fetching ${primaryKeys.length} final objects in parallel batches of ${batchSize}`, { primaryKeys });

    let batchPromises = [];
    let isCompoundKey = Array.isArray(compoundKeys) && compoundKeys.length > 1;

    for (let i = 0; i < primaryKeys.length; i += batchSize) {
        let batch = primaryKeys.slice(i, i + batchSize);

        if (isCompoundKey) {
            // **Ensure batch is formatted correctly for compound keys**
            let formattedBatch = batch.map(pk =>
                Array.isArray(pk) ? pk : compoundKeys.map(key => pk[key]) // Extract key values in order
            );

            debugLog("Fetching batch with ordered compound key values", { formattedBatch });

            batchPromises.push(table.where(compoundKeys).anyOf(formattedBatch).toArray());
        } else {
            // **Ensure batch contains raw key values for single primary keys**
            let singleKeyBatch = batch.map(pk => (typeof pk === "object" ? Object.values(pk)[0] : pk));

            debugLog("Fetching batch with single primary key", { singleKeyBatch });

            batchPromises.push(table.where(compoundKeys[0]).anyOf(singleKeyBatch).toArray());
        }
    }

    // Execute all batches in parallel and wait for the slowest one
    let batchResults = await Promise.all(batchPromises);

    // Flatten the results into a single array
    return batchResults.flat();
}


function applyCursorQueryAdditions(primaryKeyList, queryAdditions, compoundKeys, flipSkipTakeOrder = true) {
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