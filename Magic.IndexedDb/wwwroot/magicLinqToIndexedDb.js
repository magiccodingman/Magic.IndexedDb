"use strict";
import { partitionQueryConditions } from "./utilities/partitionLinqQueries.js";
import { QUERY_OPERATIONS, QUERY_ADDITIONS } from "./utilities/queryConstants.js";
import { flattenUniversalPredicate } from "./utilities/FlattenFilterNode.js";
import {
    buildIndexMetadata, normalizeCompoundKey,
    hasYieldedKey, addYieldedKey, debugLog, traceQueryPlannerStage
} from "./utilities/utilityHelpers.js";

import { initiateNestedOrFilter } from "./utilities/nestedOrFilterUtilities.js";
import { runCursorQuery } from "./utilities/cursorEngine.js";


export async function magicQueryAsync(db, table, universalSerializedPredicate,
    QueryAdditions, forceCursor = false) {
    debugLog("whereJson called");

    let results = [];

    for await (let record of magicQueryYield(db, table, universalSerializedPredicate,
        QueryAdditions, forceCursor)) {
        results.push(record);
    }

    debugLog("whereJson returning results", { count: results.length, results });
    return results;
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

    let yieldedPrimaryKeys = new Set();

    debugLog("Validated schema & cached indexes", { primaryKeys, indexes: indexCache.indexes });

    let { isFilterEmpty, nestedOrFilter } =
        initiateNestedOrFilter(nestedOrFilterUnclean, queryAdditions, primaryKeys, isUniversalTrue);

    if (isFilterEmpty) {
        debugLog("No filtering or query additions. Fetching entire table.");
        let allRecords = await table.toArray();

        while (allRecords.length > 0) {
            let record = allRecords.shift();
            yield record;
        }
        return;
    }

    debugLog('Flattened or groups');
    debugLog(nestedOrFilter);

    let { indexedQueries, compoundIndexQueries, cursorConditions } =
        partitionQueryConditions(nestedOrFilter, queryAdditions, indexCache, forceCursor);

    debugLog("Final Indexed Queries vs. Compound Queries vs. Cursor Queries", { indexedQueries, compoundIndexQueries, cursorConditions });

    if (indexedQueries.length > 0 || compoundIndexQueries.length > 0) {
        let { optimizedSingleIndexes, optimizedCompoundIndexes } = optimizeIndexedQueries(indexedQueries, compoundIndexQueries);
        debugLog("Optimized Indexed Queries", { optimizedSingleIndexes, optimizedCompoundIndexes });

        traceQueryPlannerStage("indexed-physical-optimization", {
            inputIndexedQueryCount: indexedQueries.length,
            inputIndexedConditionsPerQuery: indexedQueries.map(query => Array.isArray(query) ? query.length : null),
            inputCompoundQueryCount: compoundIndexQueries.length,
            outputSingleIndexQueryCount: optimizedSingleIndexes.length,
            outputCompoundIndexQueryCount: optimizedCompoundIndexes.length,
            outputSingleIndexes: summarizePhysicalQueries(optimizedSingleIndexes),
            outputCompoundIndexes: summarizePhysicalQueries(optimizedCompoundIndexes)
        });

        let allOptimizedQueries = [...optimizedSingleIndexes, ...optimizedCompoundIndexes];

        traceQueryPlannerStage("indexed-execution", {
            queryCount: allOptimizedQueries.length,
            queries: summarizePhysicalQueries(allOptimizedQueries)
        });

        let results = await runIndexedQueries(db, table, allOptimizedQueries,
            queryAdditions, primaryKeys, yieldedPrimaryKeys);

        traceQueryPlannerStage("indexed-execution-result", {
            resultCount: results.length
        });

        while (results.length > 0) {
            let record = results.shift();
            yield record;
        }
    }

    if (Array.isArray(cursorConditions) && cursorConditions.length > 0) {
        traceQueryPlannerStage("cursor-execution", {
            conditionSetCount: cursorConditions.length,
            undefinedConditionSetCount: cursorConditions.filter(condition => condition === undefined).length,
            queryAdditions: (queryAdditions || []).map(addition => addition.additionFunction)
        });

        let cursorResults = await runCursorQuery(db, table, cursorConditions, queryAdditions, yieldedPrimaryKeys, primaryKeys);
        debugLog("Cursor Query Results Count", { count: cursorResults.length });

        traceQueryPlannerStage("cursor-execution-result", {
            resultCount: cursorResults.length
        });

        while (cursorResults.length > 0) {
            let record = cursorResults.shift();
            let recordKey = normalizeCompoundKey(primaryKeys, record);

            if (!hasYieldedKey(yieldedPrimaryKeys, recordKey)) {
                addYieldedKey(yieldedPrimaryKeys, recordKey);
                yield record;
            }
        }
    }
}

function hasStableOrdering(queryAdditions) {
    return queryAdditions?.some(q => q.additionFunction === QUERY_ADDITIONS.STABLE_ORDERING);
}

function summarizePhysicalQueries(queries) {
    return (queries || []).map(query =>
        (query || []).map(condition => ({
            property: condition?.property ?? null,
            properties: Array.isArray(condition?.properties) ? condition.properties : null,
            operation: condition?.operation ?? null,
            valueKind: Array.isArray(condition?.value) ? "array" : typeof condition?.value,
            valueCount: Array.isArray(condition?.value) ? condition.value.length : null
        })));
}

async function runIndexedQueries(db, table, universalQueries,
    queryAdditions, primaryKeys, yieldedPrimaryKeys) {
    if (universalQueries.length === 0) {
        debugLog("No indexed conditions provided, returning entire table.");
        return await table.toArray();
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
                if (q instanceof Promise) {
                    let record = await q;

                    if (record) {
                        let recordKey = normalizeCompoundKey(primaryKeys, record);
                        if (!hasYieldedKey(yieldedPrimaryKeys, recordKey)) {
                            addYieldedKey(yieldedPrimaryKeys, recordKey);
                            finalResults.push(record);
                        }
                    }
                }
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
 * Executes one logical indexed branch. The first condition is the physical index
 * candidate; any remaining conditions are residual predicates of the same AND branch.
 */
function runIndexedQuery(table, indexedConditions, queryAdditions = []) {
    debugLog("Executing runIndexedQuery", { indexedConditions, queryAdditions });

    if (!Array.isArray(indexedConditions) || indexedConditions.length === 0) {
        throw new Error("Indexed query branch must contain at least one condition.");
    }

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

    if (isUniversalFilter && orderAddition?.property) {
        debugLog("Detected universal filter with orderBy!", { orderBy: orderAddition.property });

        query = table.orderBy(orderAddition.property);
        if (orderAddition.additionFunction === QUERY_ADDITIONS.ORDER_BY_DESCENDING) {
            query = query.reverse();
        }
    }
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
    else if (firstCondition.property) {
        debugLog("Detected Single-Index Query!", { property: firstCondition.property });
        const where = table.where(firstCondition.property);

        switch (firstCondition.operation) {
            case QUERY_OPERATIONS.EQUAL:
                query = firstCondition.isString === true && firstCondition.caseSensitive === false
                    ? where.equalsIgnoreCase(firstCondition.value)
                    : where.equals(firstCondition.value);
                break;
            case QUERY_OPERATIONS.IN:
                query = where.anyOf(firstCondition.value);
                break;
            case QUERY_OPERATIONS.GREATER_THAN:
                query = where.above(firstCondition.value);
                break;
            case QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL:
                query = where.aboveOrEqual(firstCondition.value);
                break;
            case QUERY_OPERATIONS.LESS_THAN:
                query = where.below(firstCondition.value);
                break;
            case QUERY_OPERATIONS.LESS_THAN_OR_EQUAL:
                query = where.belowOrEqual(firstCondition.value);
                break;
            case QUERY_OPERATIONS.STARTS_WITH:
                query = firstCondition.isString === true && firstCondition.caseSensitive === false
                    ? where.startsWithIgnoreCase(firstCondition.value)
                    : where.startsWith(firstCondition.value);
                break;
            case "between":
                if (Array.isArray(firstCondition.value) && firstCondition.value.length === 2) {
                    const [lower, upper] = firstCondition.value;
                    const includeLower = firstCondition.includeLower !== false;
                    const includeUpper = firstCondition.includeUpper !== false;
                    query = where.between(lower, upper, includeLower, includeUpper);
                } else {
                    throw new Error("Invalid 'between' value format. Expected [min, max]");
                }
                break;
            default:
                throw new Error(`Unsupported indexed query operation: ${firstCondition.operation}`);
        }
    } else {
        throw new Error("Invalid indexed condition--missing `properties` or `property`.");
    }

    // Do not turn an AND branch into several independent indexed queries. Use the
    // selected index to produce candidates, then enforce every residual condition.
    const residualConditions = indexedConditions.slice(1);
    if (residualConditions.length > 0) {
        query = query.filter(record =>
            residualConditions.every(condition => matchesIndexedCondition(record, condition)));
    }

    if (requiresQueryAdditions(queryAdditions)) {
        for (const addition of queryAdditions) {
            switch (addition.additionFunction) {
                case QUERY_ADDITIONS.ORDER_BY:
                case QUERY_ADDITIONS.ORDER_BY_DESCENDING:
                    break;
                case QUERY_ADDITIONS.SKIP:
                    query = query.offset(addition.intValue);
                    break;
                case QUERY_ADDITIONS.TAKE:
                    query = query.limit(addition.intValue);
                    break;
                case QUERY_ADDITIONS.TAKE_LAST:
                    query = query.reverse().limit(addition.intValue).reverse();
                    break;
                case QUERY_ADDITIONS.FIRST:
                    return query.first();
                case QUERY_ADDITIONS.LAST:
                    return query.last();
                case QUERY_ADDITIONS.STABLE_ORDERING:
                    break;
                default:
                    throw new Error(`Unsupported query addition: ${addition.additionFunction}`);
            }
        }
    }

    return query;
}

function matchesIndexedCondition(record, condition) {
    let recordValue = record[condition.property];
    let queryValue = condition.value;

    if (condition.isString === true && condition.caseSensitive === false &&
        typeof recordValue === "string" && typeof queryValue === "string") {
        recordValue = recordValue.toLowerCase();
        queryValue = queryValue.toLowerCase();
    }

    switch (condition.operation) {
        case QUERY_OPERATIONS.EQUAL:
            return recordValue === queryValue;
        case QUERY_OPERATIONS.IN:
            return Array.isArray(queryValue) && queryValue.includes(recordValue);
        case QUERY_OPERATIONS.GREATER_THAN:
            return recordValue > queryValue;
        case QUERY_OPERATIONS.GREATER_THAN_OR_EQUAL:
            return recordValue >= queryValue;
        case QUERY_OPERATIONS.LESS_THAN:
            return recordValue < queryValue;
        case QUERY_OPERATIONS.LESS_THAN_OR_EQUAL:
            return recordValue <= queryValue;
        case QUERY_OPERATIONS.STARTS_WITH:
            return typeof recordValue === "string" &&
                typeof queryValue === "string" &&
                recordValue.startsWith(queryValue);
        default:
            throw new Error(`Unsupported indexed residual operation: ${condition.operation}`);
    }
}

function requiresQueryAdditions(queryAdditions = []) {
    if (!queryAdditions || queryAdditions.length === 0) {
        return false;
    }

    if (queryAdditions.length === 1) {
        const singleAddition = queryAdditions[0].additionFunction;
        if (singleAddition === QUERY_ADDITIONS.ORDER_BY || singleAddition === QUERY_ADDITIONS.ORDER_BY_DESCENDING) {
            return false;
        }
    }

    return true;
}

/**
 * Preserve each DNF branch. Global property grouping loses whether conditions were
 * joined by AND within one branch or OR across separate branches.
 */
function optimizeIndexedQueries(indexedQueries, compoundIndexQueries) {
    if ((!indexedQueries || indexedQueries.length === 0) && (!compoundIndexQueries || compoundIndexQueries.length === 0)) {
        return { optimizedSingleIndexes: [], optimizedCompoundIndexes: [] };
    }

    debugLog("Optimizing Indexed Queries", { indexedQueries, compoundIndexQueries });

    let optimizedSingleIndexes = optimizeIndexedOnlyQueries(indexedQueries);
    let { optimizedCompoundIndexes = [], fallbackSingleIndexes = [] } = optimizeCompoundIndexedOnlyQueries(compoundIndexQueries);

    optimizedSingleIndexes.push(...fallbackSingleIndexes);

    if (optimizedSingleIndexes.length === 0 && optimizedCompoundIndexes.length === 0) {
        throw new Error("OptimizeIndexedQueries failed--No indexed queries were produced! Investigate input conditions.");
    }

    debugLog("Final Optimized Queries", { optimizedSingleIndexes, optimizedCompoundIndexes });
    return { optimizedSingleIndexes, optimizedCompoundIndexes };
}

function optimizeIndexedOnlyQueries(indexedQueries) {
    if (!indexedQueries || indexedQueries.length === 0) return [];

    // A query entry is one AND branch. Do not regroup entries by property: that turns
    // independent branches into a different boolean expression (and was the source of
    // the range-OR, multi-prefix, and independent-index AND defects).
    return indexedQueries
        .filter(query => Array.isArray(query) && query.length > 0)
        .map(query => query.map(condition => ({ ...condition })));
}

function optimizeCompoundIndexedOnlyQueries(compoundIndexQueries) {
    if (!compoundIndexQueries || compoundIndexQueries.length === 0) {
        return { optimizedCompoundIndexes: [], fallbackSingleIndexes: [] };
    }

    let optimizedCompoundIndexes = [];
    let fallbackSingleIndexes = [];

    for (let compoundQuery of compoundIndexQueries) {
        let conditions = compoundQuery.conditions;
        let properties = compoundQuery.properties;

        let canUseEquals = conditions.every(c => c.operation === QUERY_OPERATIONS.EQUAL);

        if (canUseEquals) {
            optimizedCompoundIndexes.push([{
                properties,
                operation: QUERY_OPERATIONS.EQUAL,
                value: conditions.map(c => c.value)
            }]);
        } else {
            debugLog("Cannot optimize compound index due to unsupported operations. Preserving branch as single-index candidates.", { compoundQuery });
            fallbackSingleIndexes.push(
                (compoundQuery.allConditions ?? conditions).map(condition => ({ ...condition }))
            );
        }
    }

    return { optimizedCompoundIndexes, fallbackSingleIndexes };
}
