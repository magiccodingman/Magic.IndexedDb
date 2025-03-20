"use strict";

// Import constants
import { QUERY_OPERATIONS, QUERY_ADDITIONS, QUERY_COMBINATION_RULES, QUERY_ADDITION_RULES } from "./queryConstants.js";
import { debugLog } from "./utilityHelpers.js";

export function validateQueryAdditions(queryAdditions, indexCache) {
    queryAdditions = queryAdditions || []; // Ensure it's always an array
    let seenAdditions = new Set();
    let requiresCursor = false;

    const schema = indexCache;
    const primaryKeys = schema ? Array.from(schema.compoundKeys) : [];

    for (const addition of queryAdditions) {
        let validCombos = QUERY_ADDITION_RULES[addition.additionFunction];

        if (!validCombos) {
            console.error(`Unsupported query addition: ${addition.additionFunction}`);
            console.error(`Available keys in QUERY_ADDITION_RULES:`, Object.keys(QUERY_ADDITION_RULES));
            throw new Error(`Unsupported query addition: ${addition.additionFunction} (valid additions: ${Object.keys(QUERY_ADDITION_RULES).join(", ")})`);
        }

        // **Ensure ORDER_BY targets an indexed property**
        if (addition.additionFunction === QUERY_ADDITIONS.ORDER_BY || addition.additionFunction === QUERY_ADDITIONS.ORDER_BY_DESCENDING) {
            const isIndexed = schema?.indexes.has(addition.property) || primaryKeys.includes(addition.property);
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

export function validateQueryCombinations(nestedOrFilter) {
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


export function isValidFilterObject(obj) {
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

export function isValidQueryAdditions(arr) {
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


/**
 *  Determines if an indexed operation is supported.
 */
export function isSupportedIndexedOperation(conditions) {
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