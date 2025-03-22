"use strict";
import { debugLog } from "./utilityHelpers.js";
/**
 * This advanced optimization attempts to:
 *   1) Merge groups that share common conditions into a single group with nested OR sub-conditions.
 *   2) Deduplicate or unify partial ranges on the same property.
 *   3) Remove subsets if any appear.
 *
 * @param {object} nestedOrFilter - The object with shape { orGroups: [{ andGroups: [{ conditions: [...] }] }] }.
 * @returns {object} Optimized structure with fewer orGroups.
 */
export function advancedOptimizeNestedOrFilter(nestedOrFilter) {
    if (!nestedOrFilter || !Array.isArray(nestedOrFilter.orGroups)) {
        return nestedOrFilter;
    }

    // 1) Extract all AND groups
    let allAndGroups = [];
    for (const orGroup of nestedOrFilter.orGroups) {
        for (const andGroup of orGroup.andGroups || []) {
            if (!Array.isArray(andGroup.conditions)) continue;
            let conditionsCopy = andGroup.conditions.map(c => ({ ...c }));
            // Sort & deduplicate (optional)
            conditionsCopy = canonicalizeAndGroup(conditionsCopy);
            allAndGroups.push(conditionsCopy);
        }
    }

    // 2) Remove exact duplicates & subsets
    allAndGroups = removeExactDuplicates(allAndGroups);
    allAndGroups = removeSubsets(allAndGroups);

    // 3) **Merge common prefixes** => Key trick
    allAndGroups = mergeCommonPrefixes(allAndGroups);

    // 4) Possibly remove duplicates/subsets again
    allAndGroups = removeExactDuplicates(allAndGroups);
    allAndGroups = removeSubsets(allAndGroups);

    // 5) Rebuild
    const newOrGroups = rebuildOrGroups(allAndGroups);

    debugLog(
        `Advanced optimization complete. Original OR groups: ${nestedOrFilter.orGroups.length}, 
         Final OR groups: ${newOrGroups.length}`
    );

    return {
        orGroups: newOrGroups
    };
}

/** 
 * Rebuild orGroups from our final allAndGroups.
 */
function rebuildOrGroups(allAndGroups) {
    return allAndGroups.map(conditions => ({
        andGroups: [
            {
                conditions
            }
        ]
    }));
}

/**
 * Merges AND groups that have identical conditions except for ONE property, where we can unify them
 * by turning that property’s differing conditions into nested OR sub-conditions.
 *
 * Example:
 *   G1: [ TestInt=9, Name="Luna", Age<30 ]
 *   G2: [ TestInt=9, Name="Luna", Age>50 ]
 *   G3: [ TestInt=9, Name="Luna", Age=35 ]
 *
 * Merge into: [ TestInt=9, Name="Luna", Age = ( <30 OR >50 OR == 35 ) ]
 *
 * This is a big step up from exact-match merges. We'll do an O(N^2) pass merging pairs until no more merges are possible.
 */
function mergeCommonPrefixes(allAndGroups) {
    let mergedSomething = true;

    while (mergedSomething) {
        mergedSomething = false;

        outLoop:
        for (let i = 0; i < allAndGroups.length; i++) {
            for (let j = i + 1; j < allAndGroups.length; j++) {
                if (attemptMerge(allAndGroups, i, j)) {
                    // We merged i & j into i. Remove j from array
                    allAndGroups.splice(j, 1);
                    mergedSomething = true;
                    break outLoop; // restart outer loop
                }
            }
        }
    }

    return allAndGroups;
}

/**
 * Tries to merge group i & j by checking how many properties differ.
 * If all but one property are the same, unify the differing property into nested OR.
 *
 * @returns {boolean} true if merged, false if not
 */
function attemptMerge(all, i, j) {
    let G1 = all[i];
    let G2 = all[j];

    // Convert each AND group into a map of property -> arrayOfOps for that property
    let map1 = groupByProperty(G1);
    let map2 = groupByProperty(G2);

    // Check how many properties differ in map membership
    let allKeys = new Set([...map1.keys(), ...map2.keys()]);
    let differingProps = [];

    for (const key of allKeys) {
        const ops1 = map1.get(key) || [];
        const ops2 = map2.get(key) || [];

        if (!equalConditionSets(ops1, ops2)) {
            differingProps.push(key);
        }
        if (differingProps.length > 1) {
            // More than 1 property differs => no simple prefix merge
            return false;
        }
    }

    if (differingProps.length === 0) {
        // They are exactly the same group => skip (we have a remove-duplicates step already)
        return false;
    }

    // If exactly 1 property differs, unify them
    let diffProp = differingProps[0];
    let combinedOps = unifyOpsForProperty(map1.get(diffProp) || [], map2.get(diffProp) || []);

    // If we can't unify that property’s conditions, bail
    if (!combinedOps) {
        return false;
    }

    // Build the merged group: same for all other properties
    let mergedMap = new Map();
    for (const key of allKeys) {
        if (key === diffProp) {
            mergedMap.set(key, combinedOps);
        } else {
            // identical sets, pick from either map
            mergedMap.set(key, map1.get(key) || map2.get(key));
        }
    }

    // Convert mergedMap back to conditions array
    let mergedConditions = mapToConditionsArray(mergedMap);

    // Re-canonicalize & store
    mergedConditions = canonicalizeAndGroup(mergedConditions);

    all[i] = mergedConditions;
    return true;
}

/**
 * unifyOpsForProperty() tries to unify e.g. [Age <30], [Age>50], [Age=35] into a nested OR for Age.
 *
 * We'll produce a single "meta-condition" e.g. { property: "Age", operation: "__or__", value: [ {op:<, val:30}, {op:>, val:50}, {op:==, val:35} ] }
 *
 * If these sets are partially contradictory, we might fail or store them anyway. 
 * For now, let's just do a straightforward union, ignoring advanced contradiction checking.
 *
 * @returns {array} e.g. [ { property: "Age", operation: "__or__", value: [...] } ] 
 *                  or null if we can't unify
 */
function unifyOpsForProperty(ops1, ops2) {
    // We create a union of all conditions in ops1 and ops2
    // but store them as a single OR object
    const union = [...ops1, ...ops2];

    // If union is empty, means no conditions => effectively pass everything
    // That might or might not be desired. We'll just unify them as an empty set => means no restriction
    // so let's do that:
    if (union.length === 0) {
        return [];
    }

    // We do no advanced contradiction check here. Just jam them into an OR value
    return [
        {
            property: union[0].property,
            operation: "__or__",
            // we store an array of { op, value } so we can interpret it in the final evaluator
            value: union.map(o => ({ operation: o.operation, value: o.value }))
        }
    ];
}

/**
 * Convert an AND group (array of conditions) into property->arrayOfConditionObjects
 */
function groupByProperty(andGroup) {
    const map = new Map();
    for (const cond of andGroup) {
        if (!map.has(cond.property)) {
            map.set(cond.property, []);
        }
        map.get(cond.property).push(cond);
    }
    // stable sort each property array?
    for (const [k, v] of map) {
        v.sort((a, b) => {
            const opCmp = a.operation.localeCompare(b.operation);
            if (opCmp !== 0) return opCmp;
            const valA = JSON.stringify(a.value);
            const valB = JSON.stringify(b.value);
            return valA.localeCompare(valB);
        });
    }
    return map;
}

/**
 * Compare two arrays of conditions (for the same property) to see if they are identical sets (ignoring order).
 */
function equalConditionSets(arr1, arr2) {
    if (arr1.length !== arr2.length) return false;
    let seen = [...arr2];
    for (const c1 of arr1) {
        // find a match in seen
        let idx = seen.findIndex(s => areConditionsEqualForSameProp(c1, s));
        if (idx === -1) return false;
        seen.splice(idx, 1);
    }
    return (seen.length === 0);
}

/**
 * Convert property->arrayOfCond back into a single AND group array
 */
function mapToConditionsArray(map) {
    const results = [];
    for (const [prop, conds] of map) {
        results.push(...conds);
    }
    return results;
}

/**
 * Check if two conditions (of the same property) are identical
 */
function areConditionsEqualForSameProp(c1, c2) {
    if (c1.operation !== c2.operation) return false;
    return JSON.stringify(c1.value) === JSON.stringify(c2.value);
}

/**
 * Canonicalize a single AND group by sorting & removing duplicates
 */
function canonicalizeAndGroup(conditions) {
    conditions.sort((a, b) => {
        if (a.property !== b.property) {
            return a.property.localeCompare(b.property);
        }
        const opCmp = a.operation.localeCompare(b.operation);
        if (opCmp !== 0) return opCmp;
        const valA = JSON.stringify(a.value);
        const valB = JSON.stringify(b.value);
        return valA.localeCompare(valB);
    });
    // remove consecutive duplicates
    const unique = [];
    for (let i = 0; i < conditions.length; i++) {
        if (i === 0 || !areConditionsEqualForSameProp(conditions[i], conditions[i - 1])) {
            unique.push(conditions[i]);
        }
    }
    return unique;
}

/**
 * Remove exact duplicates of AND groups
 */
function removeExactDuplicates(allGroups) {
    const seen = new Set();
    const result = [];
    for (const group of allGroups) {
        const key = group.map(c => `${c.property}:${c.operation}:${JSON.stringify(c.value)}`).join("|");
        if (!seen.has(key)) {
            seen.add(key);
            result.push(group);
        }
    }
    return result;
}

/**
 * Remove subset groups. 
 * For advanced merges, we only do a quick check: if groupA has every condition in groupB
 * (and no "OR" block), B is redundant. 
 * This won't catch the newly introduced __or__ conditions, so you'd have to expand logic for that if you want.
 */
function removeSubsets(allGroups) {
    const toRemove = new Set();
    for (let i = 0; i < allGroups.length; i++) {
        if (toRemove.has(i)) continue;
        for (let j = i + 1; j < allGroups.length; j++) {
            if (toRemove.has(j)) continue;
            if (isSuperset(allGroups[i], allGroups[j])) {
                toRemove.add(j);
            } else if (isSuperset(allGroups[j], allGroups[i])) {
                toRemove.add(i);
                break;
            }
        }
    }
    return allGroups.filter((_, idx) => !toRemove.has(idx));
}

function isSuperset(gA, gB) {
    // naive: if every cond in B is found in A 
    // ignoring any __or__ expansions for now
    return gB.every(bCond =>
        gA.some(aCond => bCond.property === aCond.property
            && bCond.operation === aCond.operation
            && JSON.stringify(bCond.value) === JSON.stringify(aCond.value))
    );
}
