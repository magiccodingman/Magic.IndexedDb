"use strict";
import { debugLog } from "./utilityHelpers.js";
import {
    resolveNodeType,
    resolveLogicalOperator
} from "./predicateEnumHelpers.js";

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

    const result = {
        nestedOrFilterUnclean: {
            orGroups
        },
        isUniversalTrue: detection.isUniversalTrue,
        isUniversalFalse: detection.isUniversalFalse
    };

    debugLog("Flattened predicate result", result);
    return result;
}

function flattenNodeToAndGroups(node) {
    const nodeType = resolveNodeType(node.nodeType);

    if (!nodeType) {
        debugLog("Skipping node with null/invalid nodeType", node);
        return [];
    }

    if (nodeType === "Condition") {
        if (!node.condition) {
            debugLog("Skipping condition node with null condition", node);
            return [];
        }
        return [[node.condition]];
    }

    if (nodeType === "Logical") {
        const operator = resolveLogicalOperator(node.operator);
        const { children } = node;

        if (!operator || !Array.isArray(children)) {
            debugLog("Skipping logical node with null operator or children", node);
            return [];
        }

        if (operator === "And") {
            let result = [[]];
            for (const child of children) {
                const childFlattened = flattenNodeToAndGroups(child);
                const newResult = [];

                for (const resGroup of result) {
                    for (const childGroup of childFlattened) {
                        newResult.push([...resGroup, ...childGroup]);
                    }
                }

                result = newResult;
            }
            return result;
        }

        if (operator === "Or") {
            let result = [];
            for (const child of children) {
                const childFlattened = flattenNodeToAndGroups(child);
                result = result.concat(childFlattened);
            }
            return result;
        }

        debugLog("Unknown operator encountered during flattening", operator);
        return [];
    }

    debugLog("Unknown node type encountered during flattening", nodeType);
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
