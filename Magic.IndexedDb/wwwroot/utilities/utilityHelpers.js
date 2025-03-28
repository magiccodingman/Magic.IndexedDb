"use strict";

const DEBUG_MODE = false; // Set to false before release

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
    let recordKey = primaryKeys.map(pk => String(record[pk])).join("|");
    return recordKey;
}

/**
 * Checks if a compound key has already been yielded.
 */
export function hasYieldedKey(yieldedPrimaryKeys, recordKey) {
    return yieldedPrimaryKeys.has(recordKey);
}

/**
 * Marks a compound key as yielded.
 */
export function addYieldedKey(yieldedPrimaryKeys, recordKey) {
    yieldedPrimaryKeys.add(recordKey);
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