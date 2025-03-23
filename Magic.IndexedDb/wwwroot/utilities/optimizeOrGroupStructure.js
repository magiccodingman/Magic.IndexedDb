"use strict";
import { debugLog } from "./utilityHelpers.js";

/**
 * Fully rebuilt OR group optimizer for truth-preserving reduction.
 */
export function optimizeOrGroupStructure(nestedOrFilter) {

    // Support both { orGroups: [...] } and raw OR group arrays [ ... ]
    const orGroupArray = Array.isArray(nestedOrFilter?.orGroups)
        ? nestedOrFilter.orGroups
        : Array.isArray(nestedOrFilter)
            ? nestedOrFilter
            : null;

    if (!orGroupArray) return nestedOrFilter;

    // Extract AND groups into maps
    let groupMaps = [];
    for (const orGroup of orGroupArray) {
        for (const andGroup of orGroup.andGroups || []) {
            if (!Array.isArray(andGroup.conditions)) continue;
            const map = buildPropertyMap(andGroup.conditions);
            groupMaps.push(map);
        }
    }

    // Repeat merging passes
    let merged = true;
    while (merged) {
        merged = false;
        outer: for (let i = 0; i < groupMaps.length; i++) {
            for (let j = i + 1; j < groupMaps.length; j++) {
                const mergedMap = attemptSmartMerge(groupMaps[i], groupMaps[j]);
                if (mergedMap) {
                    groupMaps[i] = mergedMap;
                    groupMaps.splice(j, 1);
                    merged = true;
                    break outer;
                }
            }
        }
    }

    // Convert maps back to OR group format
    const optimizedGroups = groupMaps.map(m => ({
        andGroups: [{ conditions: mapToConditionsArray(m) }]
    }));
    return optimizedGroups;
}

function mapToConditionsArray(propMap) {
    const results = [];
    for (const [property, condArray] of propMap.entries()) {
        results.push(...condArray);
    }
    return results;
}


function buildPropertyMap(conditions) {
    const map = new Map();
    for (const cond of conditions) {
        const key = cond.property;
        if (!map.has(key)) map.set(key, []);
        map.get(key).push(cleanCondition(cond));
    }
    return map;
}

function cleanCondition(cond) {
    return {
        property: cond.property,
        operation: cond.operation,
        value: cond.value,
        isString: cond.isString || false,
        caseSensitive: cond.caseSensitive || false
    };
}

function flattenPropertyMap(propMap) {
    const result = [];
    for (const [property, conds] of propMap) {
        result.push(...conds);
    }
    return result;
}

function attemptSmartMerge(mapA, mapB) {
    const allProps = new Set([...mapA.keys(), ...mapB.keys()]);
    const merged = new Map();

    for (const prop of allProps) {
        const aConds = mapA.get(prop) || [];
        const bConds = mapB.get(prop) || [];

        if (areConditionArraysEqual(aConds, bConds)) {
            merged.set(prop, aConds);
        } else {
            const unified = unifyConditions(prop, aConds, bConds);
            if (!unified) return null;
            merged.set(prop, unified);
        }
    }
    return merged;
}

function unifyConditions(property, condsA, condsB) {
    // If any condition differs, don't try to merge
    if (!areConditionArraysEqual(condsA, condsB)) {
        return null;
    }

    // They're identical, return one of them
    return condsA;
}


function areConditionArraysEqual(a, b) {
    if (a.length !== b.length) return false;
    const bCopy = [...b];
    for (const condA of a) {
        const idx = bCopy.findIndex(condB => isConditionEqual(condA, condB));
        if (idx === -1) return false;
        bCopy.splice(idx, 1);
    }
    return bCopy.length === 0;
}

function isConditionEqual(c1, c2) {
    return (
        c1.property === c2.property &&
        c1.operation === c2.operation &&
        JSON.stringify(c1.value) === JSON.stringify(c2.value) &&
        (c1.caseSensitive || false) === (c2.caseSensitive || false) &&
        (c1.isString || false) === (c2.isString || false)
    );
}