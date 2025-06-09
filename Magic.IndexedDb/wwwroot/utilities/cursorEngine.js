import {
    normalizeCompoundKey,
    hasYieldedKey, debugLog
} from "./utilityHelpers.js";
import { QUERY_OPERATIONS, QUERY_ADDITIONS } from "./queryConstants.js";
import { rebuildCursorConditionsToPredicateTree } from "./rebuildNestedPredicate.js";

/**
 * Executes a cursor-based query using Dexie's `each()` for efficient iteration.
 * This ensures that records are not duplicated if they match multiple OR conditions.
 *
 * @param {Object} table - The Dexie table instance.
 * @param {Array} conditionsArray - Array of OR groups containing AND conditions.
 * @returns {Promise<Array>} - Filtered results based on conditions.
 */
export async function runCursorQuery(db, table, conditions, queryAdditions, yieldedPrimaryKeys, compoundKeys) {

    const structuredPredicateTree = rebuildCursorConditionsToPredicateTree(conditions);

    debugLog("Running Cursor Query with Conditions", { structuredPredicateTree, queryAdditions });

    const requiresMetaProcessing = queryAdditions.some(a =>
        [QUERY_ADDITIONS.TAKE, QUERY_ADDITIONS.SKIP, QUERY_ADDITIONS.FIRST, QUERY_ADDITIONS.LAST, QUERY_ADDITIONS.TAKE_LAST].includes(a.additionFunction)
    );

    if (requiresMetaProcessing) {

        let stableOrdering = hasStableOrdering(queryAdditions);

        let indexOrderProps = [];

        if (!stableOrdering) {
            indexOrderProps = detectIndexOrderProperties(structuredPredicateTree, table);
        }
        else {
            debugLog("Stable Ordering detected. Disabling any ordering by indexed queries.");
        }

        // **Metadata Path: Extract primary keys and sorting properties**
        let primaryKeyList = await runMetaDataCursorQuery(db, table, structuredPredicateTree, queryAdditions, yieldedPrimaryKeys, compoundKeys, indexOrderProps);

        // **Apply sorting, take, and skip operations**
        let finalPrimaryKeys = applyCursorQueryAdditions(primaryKeyList, queryAdditions, compoundKeys, true, indexOrderProps);

        // **Fetch only the required records from IndexedDB**
        let finalRecords = await fetchRecordsByPrimaryKeys(db, table, finalPrimaryKeys, compoundKeys);

        debugLog("Final Cursor Query Records Retrieved", { count: finalRecords.length });
        return finalRecords; // Ready for yielding
    } else {
        // **Direct Retrieval Path: Skip metadata processing & fetch full records immediately**
        return await runDirectCursorQuery(db, table, structuredPredicateTree, yieldedPrimaryKeys, compoundKeys);
    }
}

function hasStableOrdering(queryAdditions) {
    return queryAdditions?.some(q => q.additionFunction === QUERY_ADDITIONS.STABLE_ORDERING);
}


function detectIndexOrderProperties(predicateTree, table) {
    const indexedProps = new Set();

    // Step 1: Get actual indexed fields from Dexie table schema
    const dexieIndexKeys = Object.keys(table.schema.idxByName || {});

    // This gives you:
    // - For single indexes: ["Email", "Age"]
    // - For compound indexes: ["[FirstName+LastName]", "[LastName+Age]"], etc.

    // Step 2: Expand compound indexes
    const normalizedIndexProps = new Set();

    for (const idx of dexieIndexKeys) {
        if (idx.startsWith('[')) {
            const parts = idx.replace(/[\[\]]/g, "").split("+").map(x => x.trim());
            for (const p of parts) normalizedIndexProps.add(p);
        } else {
            normalizedIndexProps.add(idx);
        }
    }

    // Step 3: Walk the predicate tree
    walkPredicateTree(predicateTree, node => {
        if (node.nodeType === "condition") {
            const prop = node.condition?.property;
            if (normalizedIndexProps.has(prop)) {
                indexedProps.add(prop);
            }
        }
    });

    return [...indexedProps];
}

function walkPredicateTree(node, visitFn) {
    if (!node)
        return;

    if (node.nodeType === "condition") {
        visitFn(node); // Visit condition nodes directly
    } else if (node.nodeType === "logical" && Array.isArray(node.children)) {
        for (const child of node.children) {
            walkPredicateTree(child, visitFn);
        }
    }
}


let lastCursorWarningTime = null;

/**
 * Generalized cursor processing function for both metadata extraction and direct record retrieval.
 * @param {Function} recordHandler - Callback function to process each record.
 */
async function processCursorRecords(db, table, predicateTree, yieldedPrimaryKeys, compoundKeys, recordHandler) {
    debugLog("Processing Cursor Records");

    const now = Date.now();
    let shouldLogWarning = !lastCursorWarningTime || now - lastCursorWarningTime > 10 * 60 * 1000;

    const requiredPropertiesFiltered = new Set();

    // Only collect properties if we actually have a filter tree with children
    const hasConditions =
        predicateTree &&
        (
            predicateTree.nodeType === "condition" ||
            (predicateTree.children && predicateTree.children.length > 0)
        );

    if (hasConditions) {
        collectPropertiesFromTree(predicateTree, requiredPropertiesFiltered);
    }

    await db.transaction('r', table, async () => {
        await table.orderBy(compoundKeys[0]).each((record) => {
            // Still apply property checks *only* if we have any to check
            if (requiredPropertiesFiltered.size > 0) {
                for (const prop of requiredPropertiesFiltered) {
                    if (record[prop] === undefined) {
                        if (shouldLogWarning) {
                            console.warn(`[IndexedDB Cursor Warning] Skipping record due to missing property: ${prop}`);
                            lastCursorWarningTime = now;
                            shouldLogWarning = false;
                        }
                        return;
                    }
                }
            }

            const recordKey = normalizeCompoundKey(compoundKeys, record);
            if (hasYieldedKey(yieldedPrimaryKeys, recordKey)) {
                return;
            }

            // Only evaluate if we actually have predicate logic
            if (hasConditions && !evaluatePredicateTree(predicateTree, record))
                return;

            recordHandler(record, recordKey);
        });
    });
}


function collectPropertiesFromTree(node, propertySet) {
    if (node.nodeType === "condition") {
        propertySet.add(node.condition.property);
        return;
    }
    for (const child of node.children ?? []) {
        collectPropertiesFromTree(child, propertySet);
    }
}

function evaluatePredicateTree(node, record) {
    if (node.nodeType === "condition") {
        if (!node.optimized) {
            node.optimized = optimizeSingleCondition(node.condition);
        }
        return applyCondition(record, node.optimized);
    }

    const results = (node.children ?? []).map(child => evaluatePredicateTree(child, record));
    return node.operator === "And"
        ? results.every(r => r)
        : results.some(r => r);
}

function optimizeSingleCondition(condition) {
    if (condition.value === -Infinity || condition.value === Infinity) {
        return condition; // Already a no-op filter, skip transformation
    }

    const optimized = { ...condition };

    // Lowercase normalization for string values if not case-sensitive
    if (!condition.caseSensitive && typeof condition.value === "string") {
        optimized.value = condition.value.toLowerCase();
    }

    optimized.comparisonFunction = getComparisonFunction(condition.operation);
    return optimized;
}




/**
 * Directly retrieves records that match the conditions without metadata processing.
 */
async function runDirectCursorQuery(db, table, conditions, yieldedPrimaryKeys, compoundKeys) {
    debugLog("Running Direct Cursor Query");

    // **Estimate table size to preallocate memory**
    let estimatedSize = await table.count();
    if (estimatedSize === 0) {
        debugLog("No records found in the table. Skipping direct cursor query.");
        return [];
    }

    let records = new Array(estimatedSize); // **Preallocate**
    let resultIndex = 0;

    await processCursorRecords(db, table, conditions, yieldedPrimaryKeys, compoundKeys, (record) => {
        records[resultIndex++] = record; // **Store record using index assignment**

        // **Dynamically resize if needed**
        if (resultIndex >= records.length) {
            records.length *= 2; // **Double array size**
        }
    });

    debugLog("Direct Cursor Query Records Retrieved", { count: resultIndex });

    return records.slice(0, resultIndex); // **Trim unused slots**
}


/**
 * Extracts only necessary metadata using a Dexie cursor in a transaction.
 */
async function runMetaDataCursorQuery(db, table, conditions, queryAdditions, yieldedPrimaryKeys, compoundKeys, detectedIndexOrderProperties = []) {
    debugLog("Extracting Metadata for Cursor Query", { conditions, queryAdditions });

    let requiredProperties = new Set();
    let magicOrder = 0;

    if (conditions?.nodeType === "logical" && !conditions.children) {
        // No conditions — grab everything
        debugLog("Detected no-op predicate. All records will be evaluated.");
    } else {
        collectPropertiesFromTree(conditions, requiredProperties);
    }


    for (const addition of queryAdditions) {
        if ((addition.additionFunction === QUERY_ADDITIONS.ORDER_BY
            || addition.additionFunction === QUERY_ADDITIONS.ORDER_BY_DESCENDING) &&
            addition.property) {
            requiredProperties.add(addition.property);
        }
    }

    for (const key of compoundKeys) {
        requiredProperties.add(key);
    }

    // Include all indexable props that affect ordering
    for (const prop of detectedIndexOrderProperties) {
        requiredProperties.add(prop);
    }


    requiredProperties.add("_MagicOrderId");

    let estimatedSize = await table.count();
    if (estimatedSize === 0) {
        debugLog("No records found in the table. Skipping cursor query.");
        return [];
    }

    let primaryKeyList = new Array(estimatedSize);
    let resultIndex = 0;

    await processCursorRecords(db, table, conditions, yieldedPrimaryKeys, compoundKeys, (record, recordKey) => {
        let sortingProperties = {};

        for (const prop of requiredProperties) {
            sortingProperties[prop] = record[prop];
        }
        sortingProperties["_MagicOrderId"] = magicOrder++;

        primaryKeyList[resultIndex++] = {
            primaryKey: recordKey,
            sortingProperties: { ...sortingProperties }
        };

        if (resultIndex >= primaryKeyList.length) {
            primaryKeyList.length *= 2;
        }
    });

    debugLog("Primary Key List Collected", { count: resultIndex });
    return primaryKeyList.slice(0, resultIndex);
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


        [QUERY_OPERATIONS.CONTAINS]: (recordValue, queryValue) => {
            if (typeof recordValue === "string") {
                return recordValue.includes(queryValue);
            }
            if (Array.isArray(recordValue)) {
                return recordValue.includes(queryValue);
            }
            if (Array.isArray(queryValue)) {
                return queryValue.includes(recordValue);
            }
            return false;
        },


        [QUERY_OPERATIONS.NOT_CONTAINS]: (recordValue, queryValue) => {
            if (typeof recordValue === "string") {
                return !recordValue.includes(queryValue);
            }
            if (Array.isArray(recordValue)) {
                return !recordValue.includes(queryValue);
            }
            if (Array.isArray(queryValue)) {
                return !queryValue.includes(recordValue);
            }
            return true;
        },


        [QUERY_OPERATIONS.IN]: (recordValue, queryValue) =>
            Array.isArray(queryValue) && queryValue.includes(recordValue),



        // ------ MONTH OPERATIONS ------
        [QUERY_OPERATIONS.MONTH_EQUAL]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) === queryValue;
        },

        [QUERY_OPERATIONS.NOT_MONTH_EQUAL]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) !== queryValue;
        },

        [QUERY_OPERATIONS.MONTH_GREATER_THAN]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) > queryValue;
        },

        [QUERY_OPERATIONS.MONTH_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) >= queryValue;
        },

        [QUERY_OPERATIONS.MONTH_LESS_THAN]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) < queryValue;
        },

        [QUERY_OPERATIONS.MONTH_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) <= queryValue;
        },

        // ------ DAY OPERATIONS ------
        [QUERY_OPERATIONS.DAY_EQUAL]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() === queryValue;
        },

        [QUERY_OPERATIONS.NOT_DAY_EQUAL]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() !== queryValue;
        },

        [QUERY_OPERATIONS.DAY_GREATER_THAN]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() > queryValue;
        },

        [QUERY_OPERATIONS.DAY_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() >= queryValue;
        },

        [QUERY_OPERATIONS.DAY_LESS_THAN]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() < queryValue;
        },

        [QUERY_OPERATIONS.DAY_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() <= queryValue;
        },


        // ------ DAY OF WEEK OPERATIONS ------
        [QUERY_OPERATIONS.DAY_OF_WEEK_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() === queryValue;
        },

        [QUERY_OPERATIONS.NOT_DAY_OF_WEEK_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() !== queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_WEEK_GREATER_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() > queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_WEEK_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() >= queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_WEEK_LESS_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() < queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_WEEK_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() <= queryValue;
        },


        // ------ YEAR OPERATIONS ------
        [QUERY_OPERATIONS.YEAR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() === queryValue;
        },

        [QUERY_OPERATIONS.NOT_YEAR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() !== queryValue;
        },

        [QUERY_OPERATIONS.YEAR_GREATER_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() > queryValue;
        },

        [QUERY_OPERATIONS.YEAR_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() >= queryValue;
        },

        [QUERY_OPERATIONS.YEAR_LESS_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() < queryValue;
        },

        [QUERY_OPERATIONS.YEAR_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() <= queryValue;
        },

        // ------ DAY OF YEAR OPERATIONS ------
        [QUERY_OPERATIONS.DAY_OF_YEAR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear === queryValue;
        },

        [QUERY_OPERATIONS.NOT_DAY_OF_YEAR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear !== queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_YEAR_GREATER_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear > queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_YEAR_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear >= queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_YEAR_LESS_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear < queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_YEAR_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            if (!(recordValue instanceof Date)) recordValue = new Date(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear <= queryValue;
        },



        [QUERY_OPERATIONS.ENDS_WITH]: (recordValue, queryValue) =>
            typeof recordValue === "string" && recordValue.endsWith(queryValue),

        [QUERY_OPERATIONS.NOT_ENDS_WITH]: (recordValue, queryValue) =>
            typeof recordValue === "string" && !recordValue.endsWith(queryValue),

        [QUERY_OPERATIONS.NOT_STARTS_WITH]: (recordValue, queryValue) =>
            typeof recordValue === "string" && !recordValue.startsWith(queryValue),


        [QUERY_OPERATIONS.NOT_LENGTH_EQUAL]: (recordValue, queryValue) =>
            (typeof recordValue === "string" || Array.isArray(recordValue)) && recordValue.length !== queryValue,

        [QUERY_OPERATIONS.LENGTH_EQUAL]: (recordValue, queryValue) =>
            (typeof recordValue === "string" || Array.isArray(recordValue)) && recordValue.length === queryValue,

        [QUERY_OPERATIONS.LENGTH_GREATER_THAN]: (recordValue, queryValue) =>
            (typeof recordValue === "string" || Array.isArray(recordValue)) && recordValue.length > queryValue,

        [QUERY_OPERATIONS.LENGTH_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) =>
            (typeof recordValue === "string" || Array.isArray(recordValue)) && recordValue.length >= queryValue,

        [QUERY_OPERATIONS.LENGTH_LESS_THAN]: (recordValue, queryValue) =>
            (typeof recordValue === "string" || Array.isArray(recordValue)) && recordValue.length < queryValue,

        [QUERY_OPERATIONS.LENGTH_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) =>
            (typeof recordValue === "string" || Array.isArray(recordValue)) && recordValue.length <= queryValue,

        [QUERY_OPERATIONS.TYPEOF_NUMBER]: (value) => typeof value === "number",
        [QUERY_OPERATIONS.TYPEOF_STRING]: (value) => typeof value === "string",
        [QUERY_OPERATIONS.TYPEOF_DATE]: (value) => value instanceof Date || (!isNaN(Date.parse(value))),
        [QUERY_OPERATIONS.TYPEOF_ARRAY]: (value) => Array.isArray(value),
        [QUERY_OPERATIONS.TYPEOF_OBJECT]: (value) =>
            typeof value === "object" && value !== null && !Array.isArray(value) && !(value instanceof Date),
        [QUERY_OPERATIONS.TYPEOF_BLOB]: (value) => typeof Blob !== "undefined" && value instanceof Blob,
        [QUERY_OPERATIONS.TYPEOF_ARRAYBUFFER]: (value) => value instanceof ArrayBuffer || ArrayBuffer.isView(value),
        [QUERY_OPERATIONS.TYPEOF_FILE]: (value) => typeof File !== "undefined" && value instanceof File,


        [QUERY_OPERATIONS.IS_NULL]: (value) => value === null || value === undefined,
        [QUERY_OPERATIONS.IS_NOT_NULL]: (value) => value !== null && value !== undefined,

        [QUERY_OPERATIONS.NOT_TYPEOF_NUMBER]: (value) => typeof value !== "number",
        [QUERY_OPERATIONS.NOT_TYPEOF_STRING]: (value) => typeof value !== "string",
        [QUERY_OPERATIONS.NOT_TYPEOF_DATE]: (value) => !(value instanceof Date) && isNaN(Date.parse(value)),
        [QUERY_OPERATIONS.NOT_TYPEOF_ARRAY]: (value) => !Array.isArray(value),
        [QUERY_OPERATIONS.NOT_TYPEOF_OBJECT]: (value) =>
            !(typeof value === "object" && value !== null && !Array.isArray(value) && !(value instanceof Date)),
        [QUERY_OPERATIONS.NOT_TYPEOF_BLOB]: (value) => typeof Blob === "undefined" || !(value instanceof Blob),
        [QUERY_OPERATIONS.NOT_TYPEOF_ARRAYBUFFER]: (value) =>
            !(value instanceof ArrayBuffer || ArrayBuffer.isView(value)),
        [QUERY_OPERATIONS.NOT_TYPEOF_FILE]: (value) => typeof File === "undefined" || !(value instanceof File),

    };

    return operations[operation] || (() => {
        throw new Error(`Unsupported condition: ${operation}`);
    });
}

function applyCondition(record, condition) {
    let recordValue = record[condition.property];

    if (!condition.caseSensitive && typeof recordValue === "string") {
        recordValue = recordValue.toLowerCase();
    }

    const unaryOps = [
        QUERY_OPERATIONS.TYPEOF_NUMBER,
        QUERY_OPERATIONS.TYPEOF_STRING,
        QUERY_OPERATIONS.TYPEOF_DATE,
        QUERY_OPERATIONS.TYPEOF_ARRAY,
        QUERY_OPERATIONS.TYPEOF_OBJECT,
        QUERY_OPERATIONS.TYPEOF_BLOB,
        QUERY_OPERATIONS.TYPEOF_ARRAYBUFFER,
        QUERY_OPERATIONS.TYPEOF_FILE,
        QUERY_OPERATIONS.IS_NULL,
        QUERY_OPERATIONS.IS_NOT_NULL
    ];

    if (unaryOps.includes(condition.operation)) {
        return condition.comparisonFunction(recordValue);
    }

    return condition.comparisonFunction(recordValue, condition.value);
}


async function fetchRecordsByPrimaryKeys(db, table, primaryKeys, compoundKeys, batchSize = 500, maxConcurrentBatches = 5) {
    if (!primaryKeys || primaryKeys.length === 0) return [];

    debugLog(`Fetching ${primaryKeys.length} final objects in parallel batches of ${batchSize}`, { primaryKeys });

    let isCompoundKey = Array.isArray(compoundKeys) && compoundKeys.length > 1;

    // Normalize single-key inputs to ensure flat values
    const normalizeBatch = (batch) => {
        return isCompoundKey
            ? batch.map(pk => Array.isArray(pk) ? pk : compoundKeys.map(key => pk[key]))
            : batch.map(pk => Array.isArray(pk) ? pk[0] : pk);
    };

    // **Tier 1: Small datasets (< 1500)  Single Fetch**
    if (primaryKeys.length < 1500) {
        return await db.transaction('r', table, async () => {
            let formattedBatch = normalizeBatch(primaryKeys);
            return table.where(isCompoundKey ? compoundKeys : compoundKeys[0])
                .anyOf(formattedBatch)
                .toArray();
        });
    }

    // **Tier 2: Medium Datasets (< Large Threshold)  Fire All Batches In Parallel**
    if (primaryKeys.length < batchSize * maxConcurrentBatches * 3) {
        let batchPromises = [];
        await db.transaction('r', table, async () => {
            for (let i = 0; i < primaryKeys.length; i += batchSize) {
                let batch = primaryKeys.slice(i, i + batchSize);
                let formattedBatch = normalizeBatch(batch);
                batchPromises.push(
                    table.where(isCompoundKey ? compoundKeys : compoundKeys[0])
                        .anyOf(formattedBatch)
                        .toArray()
                );
            }
        });
        let batchResults = await Promise.all(batchPromises);
        return batchResults.flat();
    }

    // **Tier 3: Massive Datasets - Controlled Concurrency, Shrinking `anyOf()` for faster lookups**
    return await db.transaction('r', table, async () => {
        let remainingKeys = [...primaryKeys];
        let foundKeys = new Set();
        let results = [];

        // **Queue only maxConcurrentBatches at a time**
        let activePromises = new Set();

        async function processNextBatch() {
            if (remainingKeys.length === 0) return;

            let batch = remainingKeys.splice(0, batchSize);
            let formattedBatch = normalizeBatch(batch);

            // **Split the query if it's too large**
            if (formattedBatch.length > 1000) {
                let mid = Math.floor(formattedBatch.length / 2);
                let firstHalf = formattedBatch.slice(0, mid);
                let secondHalf = formattedBatch.slice(mid);

                let firstQuery = table.where(isCompoundKey ? compoundKeys : compoundKeys[0])
                    .anyOf(firstHalf)
                    .toArray();

                let secondQuery = table.where(isCompoundKey ? compoundKeys : compoundKeys[0])
                    .anyOf(secondHalf)
                    .toArray();

                let promise = Promise.all([firstQuery, secondQuery]).then(([firstResults, secondResults]) => {
                    let batchResults = [...firstResults, ...secondResults];
                    results.push(...batchResults);
                    batchResults.forEach(record => foundKeys.add(normalizeCompoundKey(compoundKeys, record)));
                    activePromises.delete(promise);
                    processNextBatch();
                });

                activePromises.add(promise);
            } else {
                let promise = table.where(isCompoundKey ? compoundKeys : compoundKeys[0])
                    .anyOf(formattedBatch)
                    .toArray()
                    .then(batchResults => {
                        results.push(...batchResults);
                        batchResults.forEach(record => foundKeys.add(normalizeCompoundKey(compoundKeys, record)));
                        activePromises.delete(promise);
                        processNextBatch();
                    });

                activePromises.add(promise);
            }

            if (activePromises.size < maxConcurrentBatches) {
                processNextBatch();
            }
        }

        // **Start initial batches**
        for (let i = 0; i < maxConcurrentBatches; i++) {
            processNextBatch();
        }

        // **Wait for all batches to complete**
        await Promise.all(activePromises);
        return results;
    });
}


function applyCursorQueryAdditions(
    primaryKeyList,
    queryAdditions,
    compoundKeys,
    flipSkipTakeOrder = true,
    detectedIndexOrderProperties = []
) {
    if (!queryAdditions || queryAdditions.length === 0) {
        return primaryKeyList.map(item =>
            compoundKeys.map(key => item.sortingProperties[key])
        );
    }
    // waca
    debugLog("Applying cursor query additions in strict given order", {
        queryAdditions,
        detectedIndexOrderProperties
    });

    let additions = [...queryAdditions]; // Avoid modifying original
    let needsReverse = false;

    // Step 0: Always apply detectedIndexOrderProperties first
    if (detectedIndexOrderProperties?.length > 0) {
        primaryKeyList.sort((a, b) => {
            for (let prop of detectedIndexOrderProperties) {
                const aVal = a.sortingProperties[prop];
                const bVal = b.sortingProperties[prop];
                if (aVal !== bVal) return aVal > bVal ? 1 : -1;
            }
            // Always fallback to internal row ordering
            return a.sortingProperties["_MagicOrderId"] - b.sortingProperties["_MagicOrderId"];
        });
    }

    // Flip TAKE + SKIP if needed (for consistent cursor behavior)
    if (flipSkipTakeOrder) {
        let takeIndex = -1, skipIndex = -1;
        for (let i = 0; i < additions.length; i++) {
            if (additions[i].additionFunction === QUERY_ADDITIONS.TAKE) takeIndex = i;
            if (additions[i].additionFunction === QUERY_ADDITIONS.SKIP) skipIndex = i;
        }

        if (takeIndex !== -1 && skipIndex !== -1 && takeIndex < skipIndex) {
            debugLog("Flipping TAKE and SKIP order for cursor consistency");
            [additions[takeIndex], additions[skipIndex]] = [additions[skipIndex], additions[takeIndex]];
        }
    }

    // Step 1: Apply all query additions in declared order
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

                    // Fallback to row order
                    return a.sortingProperties["_MagicOrderId"] - b.sortingProperties["_MagicOrderId"];
                });
                break;

            case QUERY_ADDITIONS.SKIP:
                primaryKeyList = primaryKeyList.slice(addition.intValue);
                break;

            case QUERY_ADDITIONS.TAKE:
                primaryKeyList.length = Math.min(primaryKeyList.length, addition.intValue);
                break;

            case QUERY_ADDITIONS.TAKE_LAST:
                needsReverse = true;
                primaryKeyList = primaryKeyList.slice(-addition.intValue);
                break;

            case QUERY_ADDITIONS.FIRST:
                primaryKeyList.length = primaryKeyList.length > 0 ? 1 : 0;
                break;

            case QUERY_ADDITIONS.LAST:
                primaryKeyList = primaryKeyList.length > 0 ? [primaryKeyList[primaryKeyList.length - 1]] : [];
                break;

            case QUERY_ADDITIONS.STABLE_ORDERING:                
                break; // skip this

            default:
                throw new Error(`Unsupported query addition: ${addition.additionFunction}`);
        }
    }

    if (needsReverse) {
        primaryKeyList.reverse();
    }

    debugLog("Final Ordered Primary Key List", primaryKeyList);

    return primaryKeyList.map(item =>
        compoundKeys.map(key => item.sortingProperties[key])
    );
}
