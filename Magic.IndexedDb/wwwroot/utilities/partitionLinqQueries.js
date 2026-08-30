"use strict";

// Import constants
import { QUERY_ADDITIONS } from "./queryConstants.js";
import { validateQueryAdditions, validateQueryCombinations, isSupportedIndexedOperation } from "./linqValidation.js";
import { debugLog, traceQueryPlannerStage } from "./utilityHelpers.js";

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
export function partitionQueryConditions(nestedOrFilter, queryAdditions, indexCache, forceCursor) {
    let requiresCursor = validateQueryAdditions(queryAdditions, indexCache)
        || validateQueryCombinations(nestedOrFilter) || forceCursor;

    traceQueryPlannerStage("partition-decision", {
        requiresCursor,
        forceCursor: forceCursor === true,
        queryAdditions: (queryAdditions || []).map(addition => addition.additionFunction)
    });

    debugLog("Determined if query requires cursor", { requiresCursor });

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
                    traceQueryPlannerStage("branch-classified", {
                        strategy: "cursor",
                        reason: "query-level-cursor-requirement",
                        inputConditionCount: andGroup.conditions.length,
                        operations: andGroup.conditions.map(condition => condition.operation),
                        properties: andGroup.conditions.map(condition => condition.property)
                    });
                }
            }
        }
        tracePartitionSummary(indexedQueries, compoundIndexQueries, cursorConditions);
        return { indexedQueries: [], compoundIndexQueries: [], cursorConditions };
    }

    for (const orGroup of nestedOrFilter.orGroups || []) {
        if (!orGroup.andGroups || orGroup.andGroups.length === 0) continue;

        for (const andGroup of orGroup.andGroups) {
            if (!andGroup.conditions || andGroup.conditions.length === 0) continue;

            let needsCursor = false;
            let singleFieldConditions = [];

            const schema = indexCache;

            // **Step 1: Detect if this is a compound query**
            let compoundQuery = detectCompoundQuery(andGroup.conditions, indexCache);

            if (compoundQuery) {
                const residualConditionCount = compoundQuery.residualConditions.length;

                traceQueryPlannerStage("branch-classified", {
                    strategy: "compound-index",
                    properties: compoundQuery.properties,
                    inputConditionCount: andGroup.conditions.length,
                    consumedConditionCount: compoundQuery.conditions.length,
                    residualConditionCount,
                    inputProperties: andGroup.conditions.map(condition => condition.property),
                    consumedProperties: compoundQuery.conditions.map(condition => condition.property)
                });

                if (residualConditionCount > 0) {
                    // A physical index is only a candidate producer. Until the compound executor
                    // has a residual-filter stage, execute the complete logical branch with the
                    // cursor instead of silently dropping predicates outside the compound key.
                    cursorConditions.push(compoundQuery.allConditions);
                } else {
                    compoundIndexQueries.push(compoundQuery);
                }
                continue;
            }

            // **Step 2: Process as a single-field indexed or cursor query**
            for (const condition of andGroup.conditions) {
                if (!condition || typeof condition !== "object" || !condition.operation) {
                    debugLog("Skipping invalid condition", { condition });
                    continue;
                }

                // A component of a compound primary key is not a standalone IndexedDB index.
                const isPrimaryKey = schema.primaryKeyIndexes?.has(condition.property) === true;
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
                traceQueryPlannerStage("branch-classified", {
                    strategy: "cursor",
                    reason: "condition-not-index-compatible",
                    inputConditionCount: andGroup.conditions.length,
                    operations: andGroup.conditions.map(condition => condition.operation),
                    properties: andGroup.conditions.map(condition => condition.property)
                });
            } else {
                // Keep the complete AND branch together. Physical optimization may choose one
                // index as the candidate producer, but the remaining conditions stay residual
                // predicates of this same branch instead of becoming independent OR queries.
                indexedQueries.push(singleFieldConditions);
                traceQueryPlannerStage("branch-classified", {
                    strategy: "single-index",
                    inputConditionCount: singleFieldConditions.length,
                    operations: singleFieldConditions.map(condition => condition.operation),
                    properties: singleFieldConditions.map(condition => condition.property)
                });
            }
        }
    }

    /**
     * If pagination/terminal additions exist and more than one physical branch would execute,
     * reconverge the original logical branches to the cursor so pagination is applied globally.
     */
    const hasTakeOrSkipOrFirstOrLast = queryAdditions.some(addition =>
        [QUERY_ADDITIONS.TAKE, QUERY_ADDITIONS.SKIP, QUERY_ADDITIONS.TAKE_LAST,
        QUERY_ADDITIONS.LAST, QUERY_ADDITIONS.FIRST].includes(addition.additionFunction)
    );

    if (hasTakeOrSkipOrFirstOrLast && (indexedQueries.length + compoundIndexQueries.length) > 1) {
        debugLog("Multiple indexed/compound queries detected with TAKE/SKIP, forcing all to cursor.");

        const regularIndexedEntryShape = indexedQueries.map(query => ({
            isArray: Array.isArray(query),
            hasConditionsProperty: Array.isArray(query?.conditions),
            conditionCount: Array.isArray(query) ? query.length : null
        }));

        cursorConditions = [
            ...cursorConditions,
            ...indexedQueries,
            ...compoundIndexQueries.map(q => q.allConditions ?? q.conditions)
        ];

        traceQueryPlannerStage("pagination-reconvergence", {
            indexedQueryCountBefore: indexedQueries.length,
            compoundIndexQueryCountBefore: compoundIndexQueries.length,
            regularIndexedEntryShape,
            cursorConditionCountAfter: cursorConditions.length,
            undefinedCursorConditionCountAfter: cursorConditions.filter(condition => condition === undefined).length
        });

        indexedQueries = [];
        compoundIndexQueries = [];
    }

    debugLog("Partitioned Queries", { indexedQueries, compoundIndexQueries, cursorConditions });
    tracePartitionSummary(indexedQueries, compoundIndexQueries, cursorConditions);

    return { indexedQueries, compoundIndexQueries, cursorConditions };
}

function tracePartitionSummary(indexedQueries, compoundIndexQueries, cursorConditions) {
    traceQueryPlannerStage("partition-summary", {
        indexedQueryCount: indexedQueries.length,
        indexedConditionsPerQuery: indexedQueries.map(query => Array.isArray(query) ? query.length : null),
        compoundIndexQueryCount: compoundIndexQueries.length,
        cursorConditionCount: cursorConditions.length,
        undefinedCursorConditionCount: cursorConditions.filter(condition => condition === undefined).length
    });
}

function detectCompoundQuery(andConditions, indexCache) {
    debugLog("Checking if AND conditions match a compound index", { andConditions });

    const schema = indexCache;

    for (const fieldSet of schema.compoundIndexes.values()) {
        let matchedFields = new Set();

        for (const cond of andConditions) {
            if (fieldSet.has(cond.property)) {
                matchedFields.add(cond.property);
            }
        }

        if (matchedFields.size === fieldSet.size) {
            let sortedConditions = [...andConditions]
                .filter(cond => fieldSet.has(cond.property))
                .sort((a, b) => [...fieldSet].indexOf(a.property) - [...fieldSet].indexOf(b.property));

            const residualConditions = andConditions.filter(cond => !fieldSet.has(cond.property));

            debugLog("Detected valid compound query", {
                properties: [...fieldSet],
                sortedConditions,
                residualConditions
            });

            return {
                properties: [...fieldSet],
                conditions: sortedConditions,
                residualConditions,
                allConditions: [...andConditions]
            };
        }
    }

    debugLog("No matching compound index found");
    return null;
}
