"use strict";
import { debugLog } from "./utilityHelpers.js";

/**
 * Performs only truth-preserving DNF reductions over
 * { orGroups: [{ andGroups: [{ conditions: [...] }] }] }.
 *
 * The previous implementation attempted to synthesize meta `__or__` conditions and
 * removed the wrong side of an absorption pair. At this layer, correctness is more
 * valuable than speculative compression: canonicalize, deduplicate, and apply the
 * DNF absorption law A || (A && B) == A.
 */
export function advancedOptimizeNestedOrFilter(nestedOrFilter) {
    if (!nestedOrFilter || !Array.isArray(nestedOrFilter.orGroups)) {
        return nestedOrFilter;
    }

    let allAndGroups = [];
    for (const orGroup of nestedOrFilter.orGroups) {
        for (const andGroup of orGroup.andGroups || []) {
            if (!Array.isArray(andGroup.conditions)) continue;
            allAndGroups.push(canonicalizeAndGroup(andGroup.conditions));
        }
    }

    allAndGroups = removeExactDuplicates(allAndGroups);
    allAndGroups = removeRedundantSupersets(allAndGroups);

    const newOrGroups = allAndGroups.map(conditions => ({
        andGroups: [{ conditions }]
    }));

    debugLog(
        `Advanced optimization complete. Original OR groups: ${nestedOrFilter.orGroups.length}, ` +
        `Final OR groups: ${newOrGroups.length}`
    );

    return { orGroups: newOrGroups };
}

function canonicalizeAndGroup(conditions) {
    const sorted = conditions
        .map(condition => ({ ...condition }))
        .sort((a, b) => conditionKey(a).localeCompare(conditionKey(b)));

    const unique = [];
    let previousKey = null;
    for (const condition of sorted) {
        const key = conditionKey(condition);
        if (key !== previousKey) {
            unique.push(condition);
            previousKey = key;
        }
    }

    return unique;
}

function removeExactDuplicates(groups) {
    const seen = new Set();
    const result = [];

    for (const group of groups) {
        const key = groupKey(group);
        if (seen.has(key)) continue;
        seen.add(key);
        result.push(group);
    }

    return result;
}

function removeRedundantSupersets(groups) {
    return groups.filter((candidate, candidateIndex) =>
        !groups.some((other, otherIndex) =>
            otherIndex !== candidateIndex &&
            other.length < candidate.length &&
            isSuperset(candidate, other)));
}

function isSuperset(candidate, subset) {
    const candidateKeys = new Set(candidate.map(conditionKey));
    return subset.every(condition => candidateKeys.has(conditionKey(condition)));
}

function groupKey(group) {
    return JSON.stringify(group.map(conditionKey));
}

function conditionKey(condition) {
    return JSON.stringify([
        condition.property,
        condition.operation,
        condition.value,
        condition.isString ?? false,
        condition.caseSensitive ?? false
    ]);
}
