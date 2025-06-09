"use strict";
import { partitionQueryConditions } from "./utilities/partitionLinqQueries.js";
import { QUERY_OPERATIONS, QUERY_ADDITIONS } from "./utilities/queryConstants.js";
import { flattenUniversalPredicate } from "./utilities/FlattenFilterNode.js";
import {
    buildIndexMetadata, normalizeCompoundKey,
    hasYieldedKey, addYieldedKey, debugLog
} from "./utilities/utilityHelpers.js";

import { initiateNestedOrFilter } from "./utilities/nestedOrFilterUtilities.js";
import { runCursorQuery } from "./utilities/cursorEngine.js";


export async function magicQueryAsync(db, table, universalSerializedPredicate,
    QueryAdditions, forceCursor = false) {
    debugLog("whereJson called");

    let results = []; // Collect results here

    for await (let record of magicQueryYield(db, table, universalSerializedPredicate,
        QueryAdditions, forceCursor)) {
        results.push(record);
    }

    debugLog("whereJson returning results", { count: results.length, results });

    return results; // Return all results at once
}

export async function* magicQueryYield(db, table, universalSerializedPredicate,
    queryAdditions = [], forceCursor = false) {

    if (!table || !(table instanceof table.constructor)) {
        throw new Error("A valid Dexie table instance must be provided.");
    }

    const stableOrderingRequested = hasStableOrdering(queryAdditions);
    if (stableOrderingRequested) {
        forceCursor = true;
    }

    debugLog('universal serialized predicate');
    debugLog(universalSerializedPredicate);
    const { nestedOrFilterUnclean, isUniversalTrue, isUniversalFalse } = flattenUniversalPredicate(universalSerializedPredicate);

    if (isUniversalFalse === true) {
        debugLog("Universal False, sending back no data");
        return;
    }

    debugLog('flattened serialized predicate');
    debugLog(nestedOrFilterUnclean);

    debugLog("Starting where function", { nestedOrFilterUnclean, queryAdditions });

    let indexCache = buildIndexMetadata(table);
    let primaryKeys = [...indexCache.compoundKeys];

    let yieldedPrimaryKeys = new Set(); // **Structured compound key tracking**

    debugLog("Validated schema & cached indexes", { primaryKeys, indexes: indexCache.indexes });

    let { isFilterEmpty, nestedOrFilter } =
        initiateNestedOrFilter(nestedOrFilterUnclean, queryAdditions, primaryKeys, isUniversalTrue);

    // No need for processing anything, we can just immediately return results.
    if (isFilterEmpty) {
        debugLog("No filtering or query additions. Fetching entire table.");
        let allRecords = await table.toArray();

        while (allRecords.length > 0) {
            let record = allRecords.shift(); // Remove from memory before processing
            yield record;
        }
        return;
    }

    debugLog('Flattened or groups');
    debugLog(nestedOrFilter);

    let { indexedQueries, compoundIndexQueries, cursorConditions } =
        partitionQueryConditions(nestedOrFilter, queryAdditions, indexCache, forceCursor);

    debugLog("Final Indexed Queries vs. Compound Queries vs. Cursor Queries", { indexedQueries, compoundIndexQueries, cursorConditions });

    /*
    run indexed queries first. Running the cursor in parallel 
    will hurt performance drastically instead of helping.
    */
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

function hasStableOrdering(queryAdditions) {
    return queryAdditions?.some(q => q.additionFunction === QUERY_ADDITIONS.STABLE_ORDERING);
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
    const firstCondition = indexedConditions[0];

    const isUniversalFilter = (
        firstCondition &&
        firstCondition.operation === QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL &&
        (firstCondition.value === -Infinity || firstCondition.value === Number.NEGATIVE_INFINITY)
    );

    const orderAddition = queryAdditions.find(q =>
        q.additionFunction === QUERY_ADDITIONS.ORDER_BY ||
        q.additionFunction === QUERY_ADDITIONS.ORDER_BY_DESCENDING
    );

    // === Handle Universal Filter Shortcut (full table scan with orderBy) ===
    if (isUniversalFilter && orderAddition?.property) {
        debugLog("Detected universal filter with orderBy!", { orderBy: orderAddition.property });

        query = table.orderBy(orderAddition.property);
        if (orderAddition.additionFunction === QUERY_ADDITIONS.ORDER_BY_DESCENDING) {
            query = query.reverse();
        }
    }

    // === Handle Compound Index Query ===
    else if (Array.isArray(firstCondition.properties)) {
        debugLog("Detected Compound Index Query!", { properties: firstCondition.properties });

        const valuesInCorrectOrder = firstCondition.properties.map((_, i) => firstCondition.value[i]);

        query = table.where(firstCondition.properties);

        if (firstCondition.operation === QUERY_OPERATIONS.EQUAL) {
            query = query.equals(valuesInCorrectOrder);
        } else if (firstCondition.operation === QUERY_OPERATIONS.IN) {
            query = query.anyOf(firstCondition.value);
        } else {
            throw new Error(`Unsupported operation for compound indexes: ${firstCondition.operation}`);
        }
    }

    // === Handle Single Indexed Query ===
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
            case "between":
                if (Array.isArray(firstCondition.value) && firstCondition.value.length === 2) {
                    const [lower, upper] = firstCondition.value;
                    const includeLower = firstCondition.includeLower !== false;
                    const includeUpper = firstCondition.includeUpper !== false;
                    query = table.where(firstCondition.property).between(lower, upper, includeLower, includeUpper);
                } else {
                    throw new Error("Invalid 'between' value format. Expected [min, max]");
                }
                break;
            default:
                throw new Error(`Unsupported indexed query operation: ${firstCondition.operation}`);
        }
    } else {
        throw new Error("Invalid indexed condition—missing `properties` or `property`.");
    }

    // === Apply Query Additions (take, skip, first, etc.) ===
    if (requiresQueryAdditions(queryAdditions)) {
        for (const addition of queryAdditions) {
            switch (addition.additionFunction) {
                case QUERY_ADDITIONS.ORDER_BY:
                case QUERY_ADDITIONS.ORDER_BY_DESCENDING:
                    // Already handled above (only valid without .where())
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
                case QUERY_ADDITIONS.STABLE_ORDERING:
                    break; // do nothing
                default:
                    throw new Error(`Unsupported query addition: ${addition.additionFunction}`);
            }
        }
    }

    return query;
}


function requiresQueryAdditions(queryAdditions = []) {
    if (!queryAdditions || queryAdditions.length === 0) {
        return false; // No query additions at all, skip processing
    }

    if (queryAdditions.length === 1) {
        const singleAddition = queryAdditions[0].additionFunction;
        if (singleAddition === QUERY_ADDITIONS.ORDER_BY || singleAddition === QUERY_ADDITIONS.ORDER_BY_DESCENDING) {
            return false; // Only an order by, which we don't need
        }
    }

    return true; // Query additions required
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
                const minCondition = conditions.find(c => c.operation.includes("Greater"));
                const maxCondition = conditions.find(c => c.operation.includes("Less"));

                optimizedSingleIndexes.push([{
                    property,
                    operation: "between",
                    value: [minCondition.value, maxCondition.value],
                    includeLower: minCondition.operation === QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL,
                    includeUpper: maxCondition.operation === QUERY_OPERATIONS.LESS_THAN_OR_EQUAL
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


