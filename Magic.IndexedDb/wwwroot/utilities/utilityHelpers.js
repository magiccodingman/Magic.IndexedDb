"use strict";

let debugMode = false;
let plannerTraceCounter = 0;
let lastQueryPlannerTrace = null;

export function setDebugMode(enabled) {
    debugMode = enabled === true;
}

export function debugLog(...args) {
    if (debugMode) {
        console.log("[DEBUG]", ...args);
    }
}

/**
 * Starts a structured query-planner trace when Magic IndexedDB debug mode is enabled.
 * The trace is intentionally diagnostic-only and must never influence planning decisions.
 */
export function beginQueryPlannerTrace(details = {}) {
    if (!debugMode) {
        return null;
    }

    lastQueryPlannerTrace = {
        version: 1,
        traceId: ++plannerTraceCounter,
        stages: []
    };

    traceQueryPlannerStage("query-start", details);
    return lastQueryPlannerTrace;
}

/**
 * Appends a JSON-serializable planner stage to the current debug trace.
 */
export function traceQueryPlannerStage(stage, details = {}) {
    if (!debugMode || !lastQueryPlannerTrace) {
        return;
    }

    lastQueryPlannerTrace.stages.push({
        sequence: lastQueryPlannerTrace.stages.length,
        stage,
        details
    });
}

export function getLastQueryPlannerTrace() {
    return lastQueryPlannerTrace;
}

export function clearQueryPlannerTrace() {
    lastQueryPlannerTrace = null;
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
