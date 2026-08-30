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
 * Converts primary-key values into an unambiguous structured identity for de-duplication.
 * Delimiter-joining is unsafe for compound string keys (for example ["a|b", "c"]
 * and ["a", "b|c"]), so each key component is type-tagged before serialization.
 */
export function normalizeCompoundKey(primaryKeys, record) {
    return JSON.stringify(primaryKeys.map(pk => normalizeKeyPart(record[pk])));
}

function normalizeKeyPart(value) {
    if (value === null) return ["null", null];
    if (value === undefined) return ["undefined", null];

    if (Object.prototype.toString.call(value) === "[object Date]") {
        return ["date", value.getTime()];
    }

    if (value instanceof ArrayBuffer) {
        return ["binary", Array.from(new Uint8Array(value))];
    }

    if (ArrayBuffer.isView(value)) {
        return [
            "binary",
            Array.from(new Uint8Array(value.buffer, value.byteOffset, value.byteLength))
        ];
    }

    if (Array.isArray(value)) {
        return ["array", value.map(normalizeKeyPart)];
    }

    if (typeof value === "number") {
        // IndexedDB treats -0 and +0 as the same numeric key.
        return ["number", Object.is(value, -0) ? 0 : value];
    }

    return [typeof value, value];
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

    const indexMetadata = {
        indexes: new Set(),
        // These are the fields needed to reconstruct the store's primary key. They are
        // deliberately NOT evidence that each field is independently queryable.
        compoundKeys: new Set(primaryKeyInfo.keys),
        primaryKeyIsCompound: primaryKeyInfo.isCompound,
        primaryKeyIndexes: new Set(primaryKeyInfo.isCompound ? [] : primaryKeyInfo.keys),
        uniqueKeys: new Set(),
        compoundIndexes: new Map(),
    };

    // A compound primary key is queryable as the complete compound key, but none of
    // its individual components becomes a standalone IndexedDB index.
    if (primaryKeyInfo.isCompound) {
        indexMetadata.compoundIndexes.set(
            `__primary__:${primaryKeyInfo.keys.join(",")}`,
            new Set(primaryKeyInfo.keys)
        );
    }

    for (const index of schema.indexes) {
        if (typeof index.keyPath === "string") {
            indexMetadata.indexes.add(index.keyPath);
            if (index.unique) {
                indexMetadata.uniqueKeys.add(index.keyPath);
            }
            continue;
        }

        if (Array.isArray(index.keyPath)) {
            const compoundKeySet = new Set(index.keyPath);
            indexMetadata.compoundIndexes.set(index.keyPath.join(","), compoundKeySet);
            // Do not add compound-index components to indexes/uniqueKeys. IndexedDB
            // exposes the compound key path, not imaginary per-component indexes.
        }
    }

    return indexMetadata;
}
