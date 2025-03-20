"use strict";

// Import constants
import { QUERY_ADDITIONS } from "./queryConstants.js";
import { validateQueryAdditions, validateQueryCombinations, isSupportedIndexedOperation } from "./linqValidation.js";
import { debugLog } from "./utilityHelpers.js";

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

            const schema = indexCache;
            const primaryKeys = Array.from(schema.compoundKeys); // Always an array

            // **Step 1: Detect if this is a compound query**
            let compoundQuery = detectCompoundQuery(andGroup.conditions, indexCache);

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

                // **Primary Key Check Must Support Compound Keys**
                const isPrimaryKey = primaryKeys.includes(condition.property);
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

function detectCompoundQuery(andConditions, indexCache) {
    debugLog("Checking if AND conditions match a compound index", { andConditions });

    const schema = indexCache;

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