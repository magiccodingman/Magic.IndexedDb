"use strict";
import { debugLog } from "./utilityHelpers.js";
import {
    resolveNodeType,
    resolveLogicalOperator
} from "./predicateEnumHelpers.js";
import { QUERY_OPERATIONS } from "./queryConstants.js";
import { areConditionsCompatible } from "./areConditionsCompatible.js";
import { advancedOptimizeNestedOrFilter } from "./optimizeConditions.js";
import { optimizeOrGroupStructure } from "./optimizeOrGroupStructure.js";
/**
 * Flattens a Universal Predicate Tree into a nestedOrFilter structure.
 * This creates `orGroups -> andGroups -> conditions[]`
 * from a tree of nested `And`/`Or` logical nodes.
 */
export function flattenUniversalPredicate(rootNode) {
    debugLog("Flattening universal predicate", rootNode);

    if (!rootNode) {
        return {
            nestedOrFilterUnclean: { orGroups: [] },
            isUniversalTrue: false,
            isUniversalFalse: false
        };
    }

    const detection = detectUniversalTruth(rootNode);
    if (detection.isUniversalTrue && detection.isUniversalFalse) {
        throw new Error("Invalid predicate: cannot be both universally true and false.");
    }

    const andGroups = flattenNodeToAndGroups(rootNode);

    const orGroups = andGroups.map(andConditions => ({
        andGroups: [
            { conditions: andConditions }
        ]
    }));

    let orGroupsOptimized = optimizeOrGroupStructure(advancedOptimizeNestedOrFilter(orGroups));
    const result = {
        nestedOrFilterUnclean: {
            orGroups: orGroupsOptimized
        },
        isUniversalTrue: detection.isUniversalTrue,
        isUniversalFalse: detection.isUniversalFalse
    };

    debugLog("Flattened predicate result", result);
    return result;
}

function flattenNodeToAndGroups(node) {
    const nodeType = resolveNodeType(node.nodeType);
    if (!nodeType) return [];

    if (nodeType === "Condition") {
        if (!node.condition) return [];
        const normalized = normalizeCondition(node.condition);
        return [[normalized]];
    }

    if (nodeType === "Logical") {
        const operator = resolveLogicalOperator(node.operator);
        const { children } = node;

        if (!operator || !Array.isArray(children)) return [];

        if (operator === "And") {
            let result = [[]];

            for (const child of children) {
                const childGroups = flattenNodeToAndGroups(child);
                const newResult = [];

                for (const groupA of result) {
                    for (const groupB of childGroups) {
                        if (areConditionsCompatible(groupA, groupB)) {
                            newResult.push([...groupA, ...groupB]);
                        } else {
                            debugLog("Skipping incompatible group merge", { groupA, groupB });
                        }
                    }
                }

                result = newResult;
            }

            return result;
        }

        if (operator === "Or") {
            let result = [];
            for (const child of children) {
                result = result.concat(flattenNodeToAndGroups(child));
            }
            return result;
        }

        return [];
    }

    return [];
}

/**
 * Checks for a constant truth or falsehood node.
 */
function detectUniversalTruth(node) {
    const nodeType = resolveNodeType(node.nodeType);
    if (nodeType !== "Condition" || !node.condition) return {
        isUniversalTrue: false,
        isUniversalFalse: false
    };

    const { property, value } = node.condition;
    if (property !== "__constant") return {
        isUniversalTrue: false,
        isUniversalFalse: false
    };

    return {
        isUniversalTrue: value === true,
        isUniversalFalse: value === false
    };
}


/**
 * Normalizes a condition before execution.
 * Handles special logic like null-equality conversion, case handling, etc.
 */
function normalizeCondition(condition) {
    const normalized = { ...condition };

    if (
        (condition.operation === QUERY_OPERATIONS.EQUAL || condition.operation === QUERY_OPERATIONS.NOT_EQUAL) &&
        (condition.value === null || condition.value === undefined)
    ) {
        normalized.operation = condition.operation === QUERY_OPERATIONS.EQUAL
            ? QUERY_OPERATIONS.IS_NULL
            : QUERY_OPERATIONS.IS_NOT_NULL;

        // Leave value in place to pass validation
        normalized.value = null;
    }

    return normalized;
}

