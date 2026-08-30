import {
    normalizeCompoundKey,
    hasYieldedKey, debugLog
} from "./utilityHelpers.js";
import { QUERY_OPERATIONS, QUERY_ADDITIONS } from "./queryConstants.js";
import { rebuildCursorConditionsToPredicateTree } from "./rebuildNestedPredicate.js";

/**
 * Executes a cursor-based query using Dexie's `each()` for efficient iteration.
 * This ensures that records are not duplicated if they match multiple OR conditions.
 */
export async function runCursorQuery(db, table, conditions, queryAdditions, yieldedPrimaryKeys, compoundKeys) {

    const structuredPredicateTree = rebuildCursorConditionsToPredicateTree(conditions);

    debugLog("Running Cursor Query with Conditions", { structuredPredicateTree, queryAdditions });

    const requiresMetaProcessing = queryAdditions.some(a =>
        [
            QUERY_ADDITIONS.ORDER_BY,
            QUERY_ADDITIONS.ORDER_BY_DESCENDING,
            QUERY_ADDITIONS.TAKE,
            QUERY_ADDITIONS.SKIP,
            QUERY_ADDITIONS.FIRST,
            QUERY_ADDITIONS.LAST,
            QUERY_ADDITIONS.TAKE_LAST
        ].includes(a.additionFunction)
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

        let primaryKeyList = await runMetaDataCursorQuery(db, table, structuredPredicateTree, queryAdditions, yieldedPrimaryKeys, compoundKeys, indexOrderProps);
        let finalPrimaryKeys = applyCursorQueryAdditions(primaryKeyList, queryAdditions, compoundKeys, true, indexOrderProps);
        let finalRecords = await fetchRecordsByPrimaryKeys(db, table, finalPrimaryKeys, compoundKeys);

        debugLog("Final Cursor Query Records Retrieved", { count: finalRecords.length });
        return finalRecords;
    } else {
        return await runDirectCursorQuery(db, table, structuredPredicateTree, yieldedPrimaryKeys, compoundKeys);
    }
}

function hasStableOrdering(queryAdditions) {
    return queryAdditions?.some(q => q.additionFunction === QUERY_ADDITIONS.STABLE_ORDERING);
}


function detectIndexOrderProperties(predicateTree, table) {
    const indexedProps = new Set();

    // Only true standalone indexes can define single-property index ordering. A field
    // that merely participates in [A+B] is not an IndexedDB index named A or B.
    const normalizedIndexProps = new Set();
    for (const index of table.schema.indexes || []) {
        if (typeof index.keyPath === "string") {
            normalizedIndexProps.add(index.keyPath);
        }
    }

    const primaryKeyPath = table.schema.primKey?.keyPath;
    if (typeof primaryKeyPath === "string") {
        normalizedIndexProps.add(primaryKeyPath);
    }

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
        visitFn(node);
    } else if (node.nodeType === "logical" && Array.isArray(node.children)) {
        for (const child of node.children) {
            walkPredicateTree(child, visitFn);
        }
    }
}


let lastCursorWarningTime = null;

async function processCursorRecords(db, table, predicateTree, yieldedPrimaryKeys, compoundKeys, recordHandler) {
    debugLog("Processing Cursor Records");

    const now = Date.now();
    let shouldLogWarning = !lastCursorWarningTime || now - lastCursorWarningTime > 10 * 60 * 1000;

    const requiredPropertiesFiltered = new Set();

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
        // toCollection() traverses the store by its actual primary key, including a
        // compound primary key. orderBy(compoundKeys[0]) incorrectly assumes the first
        // component of a compound key is also a standalone index.
        await table.toCollection().each((record) => {
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

            if (hasConditions && !evaluatePredicateTree(predicateTree, record))
                return;

            recordHandler(record, recordKey);
        });
    });
}


function collectPropertiesFromTree(node, propertySet) {
    if (node.nodeType === "condition") {
        if (node.condition?.property !== "__constant") {
            propertySet.add(node.condition.property);
        }
        return;
    }
    for (const child of node.children ?? []) {
        collectPropertiesFromTree(child, propertySet);
    }
}

function evaluatePredicateTree(node, record) {
    if (node.nodeType === "condition") {
        if (node.condition?.property === "__constant") {
            return node.condition.value === true;
        }

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
        return condition;
    }

    const optimized = { ...condition };

    if (condition.isString && !condition.caseSensitive && typeof condition.value === "string") {
        optimized.value = condition.value.toLowerCase();
    }

    optimized.comparisonFunction = getComparisonFunction(condition.operation);
    return optimized;
}


async function runDirectCursorQuery(db, table, conditions, yieldedPrimaryKeys, compoundKeys) {
    debugLog("Running Direct Cursor Query");

    let estimatedSize = await table.count();
    if (estimatedSize === 0) {
        debugLog("No records found in the table. Skipping direct cursor query.");
        return [];
    }

    let records = new Array(estimatedSize);
    let resultIndex = 0;

    await processCursorRecords(db, table, conditions, yieldedPrimaryKeys, compoundKeys, (record) => {
        records[resultIndex++] = record;

        if (resultIndex >= records.length) {
            records.length *= 2;
        }
    });

    debugLog("Direct Cursor Query Records Retrieved", { count: resultIndex });

    return records.slice(0, resultIndex);
}


async function runMetaDataCursorQuery(db, table, conditions, queryAdditions, yieldedPrimaryKeys, compoundKeys, detectedIndexOrderProperties = []) {
    debugLog("Extracting Metadata for Cursor Query", { conditions, queryAdditions });

    let requiredProperties = new Set();
    let magicOrder = 0;

    if (conditions?.nodeType === "logical" && !conditions.children) {
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

function normalizeDate(value) {
    if (value === null || value === undefined) {
        return new Date(Number.NaN);
    }

    if (Object.prototype.toString.call(value) === "[object Date]") {
        return new Date(value.getTime());
    }

    return new Date(value);
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

        [QUERY_OPERATIONS.MONTH_EQUAL]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) === queryValue;
        },

        [QUERY_OPERATIONS.NOT_MONTH_EQUAL]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) !== queryValue;
        },

        [QUERY_OPERATIONS.MONTH_GREATER_THAN]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) > queryValue;
        },

        [QUERY_OPERATIONS.MONTH_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) >= queryValue;
        },

        [QUERY_OPERATIONS.MONTH_LESS_THAN]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) < queryValue;
        },

        [QUERY_OPERATIONS.MONTH_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && (recordValue.getMonth() + 1) <= queryValue;
        },

        [QUERY_OPERATIONS.DAY_EQUAL]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() === queryValue;
        },

        [QUERY_OPERATIONS.NOT_DAY_EQUAL]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() !== queryValue;
        },

        [QUERY_OPERATIONS.DAY_GREATER_THAN]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() > queryValue;
        },

        [QUERY_OPERATIONS.DAY_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() >= queryValue;
        },

        [QUERY_OPERATIONS.DAY_LESS_THAN]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() < queryValue;
        },

        [QUERY_OPERATIONS.DAY_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDate() <= queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_WEEK_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() === queryValue;
        },

        [QUERY_OPERATIONS.NOT_DAY_OF_WEEK_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() !== queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_WEEK_GREATER_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() > queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_WEEK_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() >= queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_WEEK_LESS_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() < queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_WEEK_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getDay() <= queryValue;
        },

        [QUERY_OPERATIONS.YEAR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() === queryValue;
        },

        [QUERY_OPERATIONS.NOT_YEAR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() !== queryValue;
        },

        [QUERY_OPERATIONS.YEAR_GREATER_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() > queryValue;
        },

        [QUERY_OPERATIONS.YEAR_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() >= queryValue;
        },

        [QUERY_OPERATIONS.YEAR_LESS_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() < queryValue;
        },

        [QUERY_OPERATIONS.YEAR_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            return !isNaN(recordValue) && recordValue.getFullYear() <= queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_YEAR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear === queryValue;
        },

        [QUERY_OPERATIONS.NOT_DAY_OF_YEAR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear !== queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_YEAR_GREATER_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear > queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_YEAR_GREATER_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear >= queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_YEAR_LESS_THAN]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
            if (isNaN(recordValue)) return false;
            const start = new Date(recordValue.getFullYear(), 0, 0);
            const diff = recordValue - start + ((start.getTimezoneOffset() - recordValue.getTimezoneOffset()) * 60000);
            const dayOfYear = Math.floor(diff / 86400000);
            return dayOfYear < queryValue;
        },

        [QUERY_OPERATIONS.DAY_OF_YEAR_LESS_THAN_OR_EQUAL]: (recordValue, queryValue) => {
            if (recordValue === null || recordValue === undefined) return false;
            recordValue = normalizeDate(recordValue);
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

    if (condition.isString && !condition.caseSensitive && typeof recordValue === "string") {
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

    const normalizeBatch = (batch) => {
        return isCompoundKey
            ? batch.map(pk => Array.isArray(pk) ? pk : compoundKeys.map(key => pk[key]))
            : batch.map(pk => Array.isArray(pk) ? pk[0] : pk);
    };

    const keyForRecord = record => isCompoundKey
        ? compoundKeys.map(key => record[key])
        : record[compoundKeys[0]];

    const requestedOrder = new Map(
        normalizeBatch(primaryKeys).map((key, index) => [JSON.stringify(key), index])
    );

    const restoreRequestedOrder = records => records.sort((left, right) =>
        requestedOrder.get(JSON.stringify(keyForRecord(left))) -
        requestedOrder.get(JSON.stringify(keyForRecord(right)))
    );

    if (primaryKeys.length < 1500) {
        const records = await db.transaction('r', table, async () => {
            let formattedBatch = normalizeBatch(primaryKeys);
            return table.where(isCompoundKey ? compoundKeys : compoundKeys[0])
                .anyOf(formattedBatch)
                .toArray();
        });
        return restoreRequestedOrder(records);
    }

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
        return restoreRequestedOrder(batchResults.flat());
    }

    const records = await db.transaction('r', table, async () => {
        let remainingKeys = [...primaryKeys];
        let foundKeys = new Set();
        let results = [];
        let activePromises = new Set();

        async function processNextBatch() {
            if (remainingKeys.length === 0) return;

            let batch = remainingKeys.splice(0, batchSize);
            let formattedBatch = normalizeBatch(batch);

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

        for (let i = 0; i < maxConcurrentBatches; i++) {
            processNextBatch();
        }

        await Promise.all(activePromises);
        return results;
    });
    return restoreRequestedOrder(records);
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

    debugLog("Applying cursor query additions in strict given order", {
        queryAdditions,
        detectedIndexOrderProperties
    });

    let additions = [...queryAdditions];
    if (detectedIndexOrderProperties?.length > 0) {
        primaryKeyList.sort((a, b) => {
            for (let prop of detectedIndexOrderProperties) {
                const aVal = a.sortingProperties[prop];
                const bVal = b.sortingProperties[prop];
                if (aVal !== bVal) return aVal > bVal ? 1 : -1;
            }
            return a.sortingProperties["_MagicOrderId"] - b.sortingProperties["_MagicOrderId"];
        });
    }

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
                primaryKeyList.length = Math.min(primaryKeyList.length, addition.intValue);
                break;

            case QUERY_ADDITIONS.TAKE_LAST:
                primaryKeyList = primaryKeyList.slice(-addition.intValue);
                break;

            case QUERY_ADDITIONS.FIRST:
                primaryKeyList.length = primaryKeyList.length > 0 ? 1 : 0;
                break;

            case QUERY_ADDITIONS.LAST:
                primaryKeyList = primaryKeyList.length > 0 ? [primaryKeyList[primaryKeyList.length - 1]] : [];
                break;

            case QUERY_ADDITIONS.STABLE_ORDERING:
                break;

            default:
                throw new Error(`Unsupported query addition: ${addition.additionFunction}`);
        }
    }

    debugLog("Final Ordered Primary Key List", primaryKeyList);

    return primaryKeyList.map(item =>
        compoundKeys.map(key => item.sortingProperties[key])
    );
}
