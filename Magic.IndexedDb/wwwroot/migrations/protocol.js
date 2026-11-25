// The public, language-independent migration contract. No Blazor/.NET dependency.
export const protocolVersion = 1;
export const metadataStore = "__magic_migrations__";
export const capabilities = Object.freeze(["atomic", "resumable", "legacyAdoption", "customTransforms"]);
const dangerousMembers = new Set(["__proto__", "prototype", "constructor"]);
const dataOperations = new Set(["renameField", "setMissing", "deleteField", "transform"]);

export class MigrationError extends Error {
    constructor(code, message, details = {}, retryable = false, cause) {
        super(`${code}: ${message}`, { cause });
        this.name = "MigrationError";
        this.code = code;
        this.details = details;
        this.retryable = retryable;
        this.documentationUrl = "https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/migrations.md#diagnostics";
    }
}

export function requireCondition(condition, code, message, details) {
    if (!condition) throw new MigrationError(code, message, details);
}

export function canonicalJson(value) {
    if (value === null || typeof value !== "object") {
        const encoded = JSON.stringify(value);
        requireCondition(encoded !== undefined && !(typeof value === "number" && !Number.isFinite(value)),
            "MAGDB_INVALID_DOCUMENT", "Documents must contain finite JSON values.");
        return encoded;
    }
    if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
    return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(",")}}`;
}

export async function sha256(value) {
    const bytes = typeof value === "string" ? new TextEncoder().encode(value) : value;
    const hash = await crypto.subtle.digest("SHA-256", bytes);
    return `sha256:${Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, "0")).join("")}`;
}

export function validateProtocol(document) {
    requireCondition(document?.protocolVersion === protocolVersion, "MAGDB_PROTOCOL_UNSUPPORTED", "Unsupported migration protocol version.");
    requireCondition(Array.isArray(document.requiredCapabilities ?? []), "MAGDB_INVALID_DOCUMENT", "requiredCapabilities must be an array.");
    for (const capability of document.requiredCapabilities ?? []) {
        requireCondition(capabilities.includes(capability), "MAGDB_CAPABILITY_UNSUPPORTED", "The engine does not implement a required capability.", { capability });
    }
}

function validName(value) {
    return typeof value === "string" && value.length > 0 && !value.startsWith("__magic_") && !dangerousMembers.has(value);
}

function validKeyPath(value, nullable = true) {
    if (nullable && value === null) return true;
    if (typeof value === "string") return value.length > 0 && value.split(".").every(part => part && !dangerousMembers.has(part));
    return Array.isArray(value) && value.length > 0 && value.every(part => typeof part === "string" && validKeyPath(part, false));
}

export function validateSchema(schema) {
    validateProtocol(schema);
    requireCondition(typeof schema.schemaId === "string" && /^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(schema.schemaId),
        "MAGDB_SCHEMA_INVALID", "A stable portable schemaId is required.");
    requireCondition(["legacy", "lossless-v1"].includes(schema.numericFormat), "MAGDB_CODEC_UNSUPPORTED", "Unknown numeric storage format.");
    requireCondition(Array.isArray(schema.stores), "MAGDB_SCHEMA_INVALID", "stores must be an array.");
    const names = new Set();
    for (const store of schema.stores) {
        requireCondition(validName(store.name) && !names.has(store.name), "MAGDB_SCHEMA_INVALID", "Invalid, duplicate, or reserved store name.");
        names.add(store.name);
        for (const property of ["encryptedFields", "exposedFields"]) {
            const members = store[property];
            requireCondition(members == null || Array.isArray(members) && members.every(member => typeof member === "string"),
                "MAGDB_SCHEMA_INVALID", "Protection metadata must be null or an array of persisted field names.");
        }
        requireCondition(validKeyPath(store.keyPath) && typeof store.autoIncrement === "boolean" &&
            !(store.autoIncrement && Array.isArray(store.keyPath)), "MAGDB_SCHEMA_INVALID", "Invalid store key configuration.");
        requireCondition(Array.isArray(store.indexes) && Array.isArray(store.fields), "MAGDB_SCHEMA_INVALID", "Store indexes and fields must be arrays.");
        const indexes = new Set();
        for (const index of store.indexes) {
            requireCondition(validName(index.name) && !indexes.has(index.name) && validKeyPath(index.keyPath, false) &&
                typeof index.unique === "boolean" && typeof index.multiEntry === "boolean" &&
                !(index.multiEntry && Array.isArray(index.keyPath)), "MAGDB_SCHEMA_INVALID", "Invalid or duplicate index.");
            indexes.add(index.name);
        }
        const fields = new Set();
        for (const field of store.fields) {
            requireCondition(validName(field.name) && !fields.has(field.name) && typeof field.type === "string" &&
                typeof field.codec === "string" && typeof field.nullable === "boolean", "MAGDB_SCHEMA_INVALID", "Invalid or duplicate persisted field.");
            fields.add(field.name);
        }
    }
    return schema;
}

/** All schema boundaries in the plain runner must preserve storage protections. */
export function requirePlainSchema(schema) {
    validateSchema(schema);
    requireCondition(!schema.stores.some(store => store.encryptedFields?.length > 0 || store.exposedFields != null),
        "MAGDB_PROTECTED_ADAPTER_REQUIRED", "Protected migrations must run through an authenticated storage adapter.");
    return schema;
}

export function physicalStores(schema) {
    return schema.stores.map(store => ({
        name: store.name, keyPath: store.keyPath, autoIncrement: store.autoIncrement,
        indexes: store.indexes.map(index => ({ name: index.name, keyPath: index.keyPath,
            unique: index.unique, multiEntry: index.multiEntry })).sort((a, b) => compareNames(a.name, b.name))
    })).sort((a, b) => compareNames(a.name, b.name));
}

export function compareNames(a, b) { return a < b ? -1 : a > b ? 1 : 0; }
export function samePhysical(actual, schema) { return canonicalJson(actual) === canonicalJson(physicalStores(schema)); }

const digestPattern = /^sha256:[0-9a-f]{64}$/;
const identifierPattern = /^[A-Za-z0-9][A-Za-z0-9._-]*$/;

function exactFields(value, fields) {
    return value && typeof value === "object" && !Array.isArray(value) &&
        Object.keys(value).length === fields.length && fields.every(field => Object.hasOwn(value, field));
}

export function validateRecovery(document, binding, edge, from, to, migration) {
    const valid = exactFields(document, ["protocolVersion", "id", "schemaId", "source", "target", "operation", "module"]);
    requireCondition(valid && document.protocolVersion === 2 && document.id === binding.id && document.schemaId === from.schema.schemaId,
        "MAGDB_RECOVERY_MISMATCH", "Recovery document identity does not match the approved binding.");
    const { source, target, operation, module } = document;
    requireCondition(exactFields(source, ["revision", "schemaHash", "migrationId", "migrationHash"]) &&
        source.revision === edge.fromRevision && source.schemaHash === from.hash &&
        source.migrationId === edge.id && source.migrationHash === edge.sha256 &&
        exactFields(target, ["revision", "schemaHash"]) && target.revision === edge.toRevision && target.schemaHash === to.hash &&
        exactFields(operation, ["index", "store"]) && operation.index === 0,
        "MAGDB_RECOVERY_MISMATCH", "Recovery is bound to a different migration or model.");
    requireCondition(migration.mode === "resumable" && migration.operations.length === 1 &&
        migration.operations[0].op === "transform" && migration.operations[0].store === operation.store,
        "MAGDB_RECOVERY_UNSUPPORTED", "Recovery requires one resumable custom transform on the bound store.");
    requireCondition(exactFields(module, ["path", "sha256"]) && /^[A-Za-z0-9][A-Za-z0-9._-]*\.m?js$/.test(module.path),
        "MAGDB_RECOVERY_INVALID", "Recovery requires a sibling JavaScript module.");
    validateAsset(module);
    validateRecoveryModels(from.schema, to.schema, operation.store);
}

function validateRecoveryModels(from, to, store) {
    // New/removed fields may be transformed. Existing field codec changes and
    // unrelated store contracts require an adapter outside this capability.
    function compareFields(previous, next) {
        for (const field of previous ?? []) {
            const target = next?.find(member => member.name === field.name);
            if (!target) continue;
            const { properties, ...descriptor } = field;
            const { properties: nextProperties, ...nextDescriptor } = target;
            requireCondition(canonicalJson(descriptor) === canonicalJson(nextDescriptor),
                "MAGDB_RECOVERY_UNSUPPORTED", "Recovery cannot change an existing field representation.");
            compareFields(properties, nextProperties);
        }
    }
    for (const previous of from.stores) {
        const next = to.stores.find(candidate => candidate.name === previous.name);
        if (previous.name !== store) requireCondition(canonicalJson(previous) === canonicalJson(next),
            "MAGDB_RECOVERY_UNSUPPORTED", "Recovery cannot change unrelated store contracts.");
        else {
            requireCondition((previous.recordCodec ?? null) === (next.recordCodec ?? null),
                "MAGDB_RECOVERY_UNSUPPORTED", "Recovery cannot change the store codec.");
            compareFields(previous.fields, next.fields);
        }
    }
}

export function validateManifest(manifest, schemaId) {
    if (manifest?.protocolVersion === 2) {
        requireCondition(Array.isArray(manifest.recoveries) && manifest.recoveries.length > 0 &&
            Array.isArray(manifest.requiredCapabilities) && manifest.requiredCapabilities.includes("resumableRepairV1"), "MAGDB_MANIFEST_INVALID", "Recovery-aware manifests require approved bindings.");
        validateProtocol({ ...manifest, protocolVersion: 1,
            requiredCapabilities: Array.isArray(manifest.requiredCapabilities)
                ? manifest.requiredCapabilities.filter(capability => capability !== "resumableRepairV1") : null });
    } else {
        validateProtocol(manifest);
        requireCondition(!Object.hasOwn(manifest, "recoveries"), "MAGDB_MANIFEST_INVALID", "Recovery bindings require manifest protocol 2.");
    }
    requireCondition(manifest.schemaId === schemaId, "MAGDB_SCHEMA_ID_MISMATCH", "The manifest belongs to another logical database.");
    requireCondition(Number.isSafeInteger(manifest.targetRevision) && manifest.targetRevision > 0 &&
        Array.isArray(manifest.schemas) && Array.isArray(manifest.migrations) && Array.isArray(manifest.baselines ?? []),
    "MAGDB_MANIFEST_INVALID", "Invalid manifest collections or target revision.");
    const revisions = new Set();
    for (const asset of manifest.schemas) {
        validateAsset(asset);
        requireCondition(Number.isSafeInteger(asset.revision) && asset.revision > 0 && asset.revision <= manifest.targetRevision && !revisions.has(asset.revision),
            "MAGDB_MANIFEST_INVALID", "Duplicate or invalid schema revision.");
        revisions.add(asset.revision);
    }
    requireCondition(revisions.has(manifest.targetRevision), "MAGDB_MANIFEST_INVALID", "Target schema asset is missing.");
    requireCondition(revisions.size === manifest.targetRevision && manifest.migrations.length === manifest.targetRevision - 1,
        "MAGDB_HISTORY_INVALID", "The manifest must retain every schema and adjacent migration from revision one through its target.");
    const ids = new Set(), starts = new Set();
    for (const migration of manifest.migrations) {
        validateAsset(migration);
        requireCondition(typeof migration.id === "string" && migration.id && !ids.has(migration.id) &&
            Number.isSafeInteger(migration.fromRevision) && migration.fromRevision > 0 && migration.fromRevision < manifest.targetRevision &&
            migration.toRevision === migration.fromRevision + 1 &&
            revisions.has(migration.fromRevision) && revisions.has(migration.toRevision) && !starts.has(migration.fromRevision),
        "MAGDB_HISTORY_INVALID", "Migration history contains a gap, duplicate, or fork.");
        ids.add(migration.id); starts.add(migration.fromRevision);
    }
    const recoveryIds = new Set(), recoveryEdges = new Set();
    for (const binding of manifest.recoveries ?? []) {
        requireCondition(exactFields(binding, ["id", "fromRevision", "path", "sha256"]) &&
            typeof binding.id === "string" && identifierPattern.test(binding.id) && starts.has(binding.fromRevision) &&
            !recoveryIds.has(binding.id) && !recoveryEdges.has(binding.fromRevision),
            "MAGDB_MANIFEST_INVALID", "Recovery bindings require unique identities and one approval per edge.");
        validateAsset(binding);
        recoveryIds.add(binding.id); recoveryEdges.add(binding.fromRevision);
    }
    for (const baseline of manifest.baselines ?? []) {
        requireCondition(typeof baseline.id === "string" && baseline.id && revisions.has(baseline.revision),
            "MAGDB_BASELINE_INVALID", "Invalid baseline revision or identity.");
        validateAsset(baseline.schema);
    }
}

/** A journal retains the contiguous suffix applied after its bootstrap/adoption. */
export function validateCompletedHistory(history, revision, manifest = null) {
    requireCondition(Number.isSafeInteger(revision) && revision > 0 && Array.isArray(history) && history.length <= revision - 1,
        "MAGDB_STATE_INVALID", "The completed migration history exceeds its committed revision.");
    const ids = new Set();
    for (let index = 0; index < history.length; index++) {
        const entry = history[index];
        requireCondition(typeof entry?.id === "string" && entry.id && !ids.has(entry.id) && /^sha256:[0-9a-f]{64}$/.test(entry.sha256),
            "MAGDB_STATE_INVALID", "Completed migration identities must be valid and unique.");
        ids.add(entry.id);
        const recovered = entry.outcome === "recovered";
        requireCondition(recovered ? exactFields(entry, ["id", "sha256", "outcome", "recovery"]) &&
            exactFields(entry.recovery, ["id", "sha256"]) && typeof entry.recovery.id === "string" &&
            identifierPattern.test(entry.recovery.id) && digestPattern.test(entry.recovery.sha256)
            : exactFields(entry, ["id", "sha256"]), "MAGDB_STATE_INVALID", "Invalid migration completion outcome.");
        if (manifest) {
            const fromRevision = revision - history.length + index;
            const reference = manifest.migrations.find(item => item.fromRevision === fromRevision);
            requireCondition(reference?.toRevision === fromRevision + 1 && reference.id === entry.id && reference.sha256 === entry.sha256,
                "MAGDB_HISTORY_REWRITTEN", "A completed migration is missing, changed, or attached to a different revision.");
            if (recovered) requireCondition(manifest.recoveries?.some(binding => binding.fromRevision === fromRevision &&
                binding.id === entry.recovery.id && binding.sha256 === entry.recovery.sha256),
                "MAGDB_RECOVERY_BINDING_REQUIRED", "The manifest must retain the approved recovery binding.");
        }
    }
}

export function validateAsset(asset) {
    requireCondition(asset && typeof asset.path === "string" && asset.path.length > 0 &&
        /^sha256:[0-9a-f]{64}$/.test(asset.sha256), "MAGDB_ASSET_INVALID", "An asset requires a path and SHA-256 digest.");
}

export function validateMigration(document, reference, from, to) {
    requirePlainSchema(from);
    requirePlainSchema(to);
    validateProtocol(document);
    requireCondition(document.schemaId === from.schemaId && document.schemaId === to.schemaId &&
        document.id === reference.id && document.fromRevision === reference.fromRevision && document.toRevision === reference.toRevision,
    "MAGDB_HISTORY_INVALID", "Migration identity does not match its manifest.");
    requireCondition(["atomic", "resumable"].includes(document.mode) && Array.isArray(document.operations),
        "MAGDB_MIGRATION_INVALID", "Migration mode or operations are invalid.");
    for (const operation of document.operations) {
        requireCondition(operation && typeof operation.op === "string", "MAGDB_OPERATION_INVALID", "Invalid operation.");
        requireCondition(operation.op !== "manual", "MAGDB_UNRESOLVED_MIGRATION", "Resolve manual migration decisions before publishing.");
        requireCondition(["createStore", "deleteStore", "createIndex", "deleteIndex", "renameStore", ...dataOperations].includes(operation.op),
            "MAGDB_OPERATION_UNSUPPORTED", "Unknown migration operation.", { operation: operation.op });
        requireCondition(document.mode !== "resumable" || dataOperations.has(operation.op), "MAGDB_OPERATION_UNSUPPORTED",
            "Resumable protocol v1 supports data transforms with stable primary keys; split structural changes into atomic migrations.");
        if (operation.op !== "createStore") requireCondition(validName(operation.store), "MAGDB_OPERATION_INVALID", "An operation requires a non-reserved store.");
        if (["deleteStore", "deleteField"].includes(operation.op)) requireCondition(operation.allowDataLoss === true,
            "MAGDB_DESTRUCTIVE_OPERATION", "Deleting persisted data requires explicit allowDataLoss.");
        if (operation.op === "renameStore") requireCondition(validName(operation.to), "MAGDB_OPERATION_INVALID", "Invalid destination store.");
        for (const key of operation.op === "renameField" ? ["from", "to"] : ["setMissing", "deleteField"].includes(operation.op) ? ["path"] : []) {
            requireCondition(validName(operation[key]) && !operation[key].includes("."), "MAGDB_OPERATION_INVALID", "Built-in field operations require safe top-level fields.");
        }
        if (operation.op === "setMissing") requireCondition(Object.hasOwn(operation, "value"), "MAGDB_OPERATION_INVALID", "setMissing requires an explicit value.");
        if (operation.op === "transform") validateAsset({ path: operation.module, sha256: operation.sha256 });
    }
    // Prove the structural plan reaches the target BEFORE touching data.
    const stores = new Map(physicalStores(from).map(store => [store.name, structuredClone(store)]));
    for (const operation of document.operations) {
        const store = stores.get(operation.store);
        if (operation.op === "createStore") {
            requirePlainSchema({ ...from, stores: [operation.definition] });
            requireCondition(!stores.has(operation.definition.name), "MAGDB_OPERATION_INVALID", "Store already exists.");
            stores.set(operation.definition.name, physicalStores({ stores: [operation.definition] })[0]);
        } else {
            requireCondition(store, "MAGDB_OPERATION_INVALID", "Operation refers to an unknown store.");
            if (operation.op === "deleteStore") stores.delete(operation.store);
            if (operation.op === "renameStore") {
                requireCondition(!stores.has(operation.to), "MAGDB_OPERATION_INVALID", "Destination store already exists.");
                stores.delete(operation.store); store.name = operation.to; stores.set(store.name, store);
            }
            if (operation.op === "createIndex") {
                validateSchema({ ...from, stores: [{ ...store, fields: [], indexes: [operation.index] }] });
                requireCondition(!store.indexes.some(index => index.name === operation.index.name), "MAGDB_OPERATION_INVALID", "Index already exists.");
                store.indexes.push(operation.index);
            }
            if (operation.op === "deleteIndex") {
                requireCondition(store.indexes.some(index => index.name === operation.name), "MAGDB_OPERATION_INVALID", "Index does not exist.");
                store.indexes = store.indexes.filter(index => index.name !== operation.name);
            }
        }
    }
    requireCondition(canonicalJson(physicalStores({ stores: [...stores.values()] })) === canonicalJson(physicalStores(to)),
        "MAGDB_TARGET_MISMATCH", "The declared operations do not produce the target physical schema.");
    requireCondition(from.numericFormat === to.numericFormat, "MAGDB_CODEC_TRANSITION_UNSUPPORTED",
        "A numeric codec transition requires a dedicated storage adapter; raw record transforms cannot change the codec.");
}
