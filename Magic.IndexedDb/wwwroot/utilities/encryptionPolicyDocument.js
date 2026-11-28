// The legacy format remains stable. Managed vaults only use version 2 after
// rotation has authenticated the first complete generation history.
const label = "Magic.IndexedDb/policy";

export function validLineage(value, keyId) {
    return value && Object.getPrototypeOf(value) === Object.prototype &&
        Object.keys(value).length === 2 &&
        Array.isArray(value.generations) && value.generations.length >= 2 &&
        value.generations.every(id => typeof id === "string" && id.length > 0) &&
        new Set(value.generations).size === value.generations.length &&
        value.generations.at(-1) === keyId &&
        value.completed && Object.getPrototypeOf(value.completed) === Object.prototype &&
        Object.keys(value.completed).length === 3 &&
        typeof value.completed.rotationId === "string" && value.completed.rotationId.length > 0 &&
        [value.completed.converted, value.completed.verified].every(count => Number.isSafeInteger(count) && count >= 0);
}

export function serializePolicyDocument(fingerprint, lineage = null) {
    return JSON.stringify(lineage === null ? [label, 1, fingerprint] : [label, 2, fingerprint, lineage]);
}

export function parsePolicyDocument(value, keyId) {
    let parsed;
    try { parsed = JSON.parse(value); } catch { return null; }
    if (!Array.isArray(parsed) || parsed[0] !== label || typeof parsed[2] !== "string") return null;
    if (parsed[1] === 1 && parsed.length === 3) return { fingerprint: parsed[2], lineage: null };
    if (parsed[1] === 2 && parsed.length === 4 && validLineage(parsed[3], keyId)) {
        return { fingerprint: parsed[2], lineage: parsed[3] };
    }
    return null;
}
