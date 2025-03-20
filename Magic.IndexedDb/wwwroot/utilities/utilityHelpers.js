"use strict";

const DEBUG_MODE = true; // Set to false before release

export function debugLog(...args) {
    if (DEBUG_MODE) {
        console.log("[DEBUG]", ...args);
    }
}

/**
 * Converts a compound key into a structured format.
 * Ensures keys are **always stored in the correct order**.
 */
export function normalizeCompoundKey(primaryKeys, record) {
    return Object.fromEntries(primaryKeys.map(pk => [pk, record[pk]]));
}

/**
 * Checks if a compound key has already been yielded.
 */
export function hasYieldedKey(yieldedPrimaryKeys, recordKey) {
    let currentMap = yieldedPrimaryKeys;

    for (let key of Object.keys(recordKey).sort()) {
        if (!currentMap.has(key)) return false;
        currentMap = currentMap.get(key);
        if (!currentMap.has(recordKey[key])) return false;
        currentMap = currentMap.get(recordKey[key]);
    }

    return currentMap.has("__end__");
}

/**
 * Marks a compound key as yielded.
 */
export function addYieldedKey(yieldedPrimaryKeys, recordKey) {
    let currentMap = yieldedPrimaryKeys;

    for (let key of Object.keys(recordKey).sort()) {
        if (!currentMap.has(key)) currentMap.set(key, new Map());
        currentMap = currentMap.get(key);
        if (!currentMap.has(recordKey[key])) currentMap.set(recordKey[key], new Map());
        currentMap = currentMap.get(recordKey[key]);
    }

    currentMap.set("__end__", true);
}



export function cleanNestedOrFilter(filter) {
    if (!filter || !Array.isArray(filter.orGroups)) return null;

    let cleanedOrGroups = filter.orGroups.map(orGroup => {
        if (!Array.isArray(orGroup.andGroups)) return null;

        // Remove empty AND groups
        let cleanedAndGroups = orGroup.andGroups.filter(andGroup =>
            Array.isArray(andGroup.conditions) && andGroup.conditions.length > 0
        );

        return cleanedAndGroups.length > 0 ? { andGroups: cleanedAndGroups } : null;
    }).filter(orGroup => orGroup !== null); // Remove any fully empty OR groups

    return cleanedOrGroups.length > 0 ? { orGroups: cleanedOrGroups } : null;
}

function getPrimaryKeys(table) {
    const primaryKey = table.schema.primKey; // Always fresh

    if (Array.isArray(primaryKey.keyPath)) {
        return { isCompound: true, keys: primaryKey.keyPath };
    } else {
        return { isCompound: false, keys: [primaryKey.keyPath] };
    }
}

export function buildIndexMetadata(table) {
    const schema = table.schema;
    const primaryKeyInfo = getPrimaryKeys(table);

    let indexMetadata = {
        indexes: new Set(),
        compoundKeys: new Set(primaryKeyInfo.keys), // Store all primary keys
        uniqueKeys: new Set(),
        compoundIndexes: new Map(),
    };

    for (const index of schema.indexes) {
        if (typeof index.keyPath === "string") {
            indexMetadata.indexes.add(index.keyPath);
        }

        if (index.unique) {
            indexMetadata.uniqueKeys.add(index.keyPath);
        }

        if (Array.isArray(index.keyPath)) {
            const compoundKeySet = new Set(index.keyPath);
            indexMetadata.compoundIndexes.set(index.keyPath.join(","), compoundKeySet);

            for (const field of index.keyPath) {
                indexMetadata.indexes.add(field);
            }
        }
    }

    return indexMetadata;
}