import { MigrationError, metadataStore, canonicalJson, sha256, requirePlainSchema, requireProtectedSchema, hasProtection, validateManifest, validateCompletedHistory,
    validateMigration, validateRecovery, requireCondition, physicalStores, samePhysical, compareNames } from "./protocol.js";
import { isProtectedMigrationAdapter, equalEnvelope } from "./protectedMigrationAdapter.js";
import { encryptionMetadataStore, encryptionMetadataKey } from "../utilities/encryptionPolicy.js";

export { MigrationError };
const stateKey = "state";
const numericStore = "__magic_numeric_policy";
const protectedStateKey = "migration-state";
const observers = new Set();
const moduleCache = new Map();

export function subscribeMigrationEvents(listener) {
    if (typeof listener !== "function") throw new TypeError("A migration observer must be a function.");
    observers.add(listener);
    return () => observers.delete(listener);
}

function notify(options, phase, details = {}) {
    const event = Object.freeze({ protocolVersion: 1, name: options.name, schemaId: options.schema?.schemaId,
        phase, ...details });
    for (const observer of new Set([...observers, options.onEvent].filter(Boolean))) {
        try { Promise.resolve(observer(event)).catch(() => {}); } catch { /* Observers never control commits. */ }
    }
}

export { notify as notifyManagedLifecycle };

function abortCheck(signal) {
    if (signal?.aborted) throw new MigrationError("MAGDB_CANCELLED", "Migration was cancelled; committed checkpoints are retained.", {}, true);
}

function awaitExtension(value, signal) {
    const pending = Promise.resolve(value);
    if (!signal) return pending;
    return new Promise((resolve, reject) => {
        const cleanup = () => signal.removeEventListener("abort", cancel);
        const cancel = () => {
            cleanup();
            reject(new MigrationError("MAGDB_CANCELLED", "Migration cancelled while awaiting custom code; committed checkpoints are retained.", {}, true));
        };
        signal.addEventListener("abort", cancel, { once: true });
        // Observe eventual rejection even after cancellation. Custom code keeps
        // running unless it cooperates with context.signal, but its late result
        // cannot resume this migration or write a data/checkpoint transaction.
        pending.then(result => { cleanup(); resolve(result); }, error => { cleanup(); reject(error); });
        if (signal.aborted) cancel();
    });
}

function requestResult(request) {
    return new Promise((resolve, reject) => {
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

function transactionDone(transaction) {
    return new Promise((resolve, reject) => {
        transaction.oncomplete = resolve;
        transaction.onabort = () => reject(transaction.error ?? new MigrationError("MAGDB_TRANSACTION_ABORTED", "Migration transaction aborted.", {}, true));
        transaction.onerror = () => {}; // onabort is the definitive outcome.
    });
}

function attachConnection(db) {
    db.onversionchange = () => db.close();
    return db;
}

function openExisting(name, signal) {
    abortCheck(signal);
    return new Promise((resolve, reject) => {
        let absent = false;
        const request = indexedDB.open(name);
        const cleanup = () => signal?.removeEventListener("abort", cancel);
        const cancel = () => {
            cleanup();
            reject(new MigrationError("MAGDB_CANCELLED", "Migration cancelled while waiting for IndexedDB.", {}, true));
        };
        signal?.addEventListener("abort", cancel, { once: true });
        request.onupgradeneeded = () => { absent = true; request.transaction.abort(); };
        request.onerror = () => {
            cleanup();
            if (absent) resolve(null);
            else reject(request.error);
        };
        request.onsuccess = () => {
            cleanup();
            if (signal?.aborted) request.result.close();
            else resolve(attachConnection(request.result));
        };
    });
}

function nativeStores(db, transaction) {
    return [...db.objectStoreNames].filter(name => name !== metadataStore && name !== numericStore && name !== encryptionMetadataStore)
        .map(name => {
            const store = transaction.objectStore(name);
            return { name, keyPath: store.keyPath, autoIncrement: store.autoIncrement,
                indexes: [...store.indexNames].map(indexName => {
                    const index = store.index(indexName);
                    return { name: index.name, keyPath: index.keyPath, unique: index.unique, multiEntry: index.multiEntry };
                }).sort((a, b) => compareNames(a.name, b.name)) };
        }).sort((a, b) => compareNames(a.name, b.name));
}

export async function inspectOpen(db, rotationKey = null) {
    if (!db.objectStoreNames.length) return { name: db.name, nativeVersion: db.version, stores: [], state: null, numericPolicy: null };
    const transaction = db.transaction([...db.objectStoreNames], "readonly");
    const done = transactionDone(transaction);
    const stores = nativeStores(db, transaction);
    const protection = db.objectStoreNames.contains(encryptionMetadataStore) ? transaction.objectStore(encryptionMetadataStore) : null;
    if (protection) requireCondition(protection.keyPath === null && !protection.autoIncrement && !protection.indexNames.length,
        "MAGDB_PROTECTION_INVALID", "The authenticated metadata store has an unsupported layout.");
    const [state, numericPolicy, protectionPolicy, protectionState, rotation] = await Promise.all([
        db.objectStoreNames.contains(metadataStore) ? requestResult(transaction.objectStore(metadataStore).get(stateKey)) : null,
        db.objectStoreNames.contains(numericStore) ? requestResult(transaction.objectStore(numericStore).get("policy")) : null,
        protection ? requestResult(protection.get(encryptionMetadataKey)) : null,
        protection ? requestResult(protection.get(protectedStateKey)) : null,
        protection && rotationKey ? requestResult(protection.get(rotationKey)) : null
    ]);
    await done;
    return { name: db.name, nativeVersion: db.version, stores, state: state ?? null, numericPolicy: numericPolicy ?? null,
        hasNumericMetadata: db.objectStoreNames.contains(numericStore),
        hasProtectionMetadata: Boolean(protection), protectionPolicy: protectionPolicy ?? null, protectionState: protectionState ?? null,
        ...(rotationKey ? { rotation } : {}) };
}

async function inspectDatabaseInternal(name, signal, rotationKey = null) {
    const db = await openExisting(name, signal);
    if (!db) return null;
    try { return await inspectOpen(db, rotationKey); } finally { db.close(); }
}

export async function inspectKeyRoutingHints(name, rotationKey) {
    const snapshot = await inspectDatabaseInternal(name, undefined, rotationKey);
    return snapshot?.rotation ? { active: true, keyId: snapshot.rotation.state?.sourceKeyId,
        targetKeyId: snapshot.rotation.state?.targetKeyId }
        : snapshot?.protectionPolicy?.keyId ? { active: false, keyId: snapshot.protectionPolicy.keyId } : null;
}

/** Inspect metadata only. Durable record keys are never exposed in diagnostics. */
export async function inspectDatabase(name) {
    const snapshot = await inspectDatabaseInternal(name);
    if (snapshot) {
        snapshot.authenticated = snapshot.hasProtectionMetadata;
        delete snapshot.hasNumericMetadata;
        delete snapshot.hasProtectionMetadata;
        delete snapshot.protectionPolicy;
        delete snapshot.protectionState;
    }
    if (snapshot?.state?.active) {
        const { id, sha256, operationIndex, processedRecords, lastKey } = snapshot.state.active;
        snapshot.state.active = { id, sha256, operationIndex, processedRecords, hasCheckpoint: lastKey != null };
    }
    if (snapshot?.state?.recovery) {
        const { id, sha256, phase, repaired, verified, lastKey } = snapshot.state.recovery;
        snapshot.state.recovery = { id, sha256, phase, repaired, verified, hasCheckpoint: lastKey != null };
    }
    return snapshot;
}

function createStore(db, definition) {
    const store = db.createObjectStore(definition.name, { keyPath: definition.keyPath, autoIncrement: definition.autoIncrement });
    for (const index of definition.indexes) store.createIndex(index.name, index.keyPath, { unique: index.unique, multiEntry: index.multiEntry });
    return store;
}

function writeNumericPolicy(db, transaction, schema) {
    if (schema.numericFormat !== "lossless-v1") return;
    const store = db.objectStoreNames.contains(numericStore) ? transaction.objectStore(numericStore) : db.createObjectStore(numericStore);
    store.put({ version: 1, mode: "lossless", schema: physicalStores(schema) }, "policy");
}

export function assertNumericPolicy(snapshot, schema, legacyProtected = false) {
    if (schema.numericFormat === "lossless-v1") {
        // The released protected adapter authenticated its numeric mode in the
        // legacy sentinel without provisioning a separate numeric policy store.
        if (legacyProtected && !snapshot.hasNumericMetadata) return;
        // Historical protected Lossless databases bind this mode in their
        // encrypted sentinel instead. Adoption authenticates it before adding
        // the managed numeric policy in the same upgrade as its journal.
        if (!snapshot.state && snapshot.protectionPolicy && !snapshot.numericPolicy) return;
        requireCondition(canonicalJson(snapshot.numericPolicy) === canonicalJson({ version: 1, mode: "lossless", schema: physicalStores(schema) }),
            "MAGDB_CODEC_POLICY_MISMATCH", "The existing numeric policy does not match the supported starting model.");
    } else requireCondition(!snapshot.numericPolicy, "MAGDB_CODEC_POLICY_MISMATCH", "A numeric database cannot be opened as legacy JSON.");
}

function upgrade(options, version, apply) {
    abortCheck(options.signal);
    requireCondition(Number.isSafeInteger(version) && version > 0, "MAGDB_NATIVE_VERSION_UNSUPPORTED",
        "The next native IndexedDB version cannot be represented safely.");
    return new Promise((resolve, reject) => {
        let failure, transaction, settled = false;
        const request = indexedDB.open(options.name, version);
        const cancel = () => {
            failure ??= new MigrationError("MAGDB_CANCELLED", "Migration cancelled.", {}, true);
            if (transaction) { try { transaction.abort(); } catch {} }
            // Native open requests cannot be cancelled while blocked. Settle the
            // caller now, but retain the request handlers so a later upgrade is
            // aborted and a late connection is closed without mutating storage.
            if (!settled) { settled = true; cleanup(); reject(failure); }
        };
        const cleanup = () => options.signal?.removeEventListener("abort", cancel);
        options.signal?.addEventListener("abort", cancel, { once: true });
        request.onblocked = () => notify(options, "blocked", { code: "MAGDB_BLOCKED", retryable: true });
        request.onupgradeneeded = event => {
            transaction = request.transaction;
            if (failure || options.signal?.aborted) { cancel(); return; }
            try { apply(request.result, transaction, event.oldVersion, error => {
                failure = error;
                try { transaction.abort(); } catch {}
            }); } catch (error) { failure = error; transaction.abort(); }
        };
        request.onerror = () => { cleanup(); if (!settled) { settled = true; reject(failure ?? request.error); } };
        request.onsuccess = () => {
            cleanup();
            const db = attachConnection(request.result);
            if (settled) { db.close(); return; }
            settled = true;
            if (failure || options.signal?.aborted) { db.close(); reject(failure ?? new MigrationError("MAGDB_CANCELLED", "Migration cancelled.", {}, true)); }
            else resolve(db);
        };
    });
}

async function fetchBytes(url, signal, code, message) {
    abortCheck(signal);
    let response;
    try {
        response = await fetch(url, { signal, cache: "no-cache", credentials: "same-origin" });
    } catch (cause) {
        abortCheck(signal);
        throw new MigrationError(code, message, {}, true, cause);
    }
    if (!response.ok) throw new MigrationError(code, message, { status: response.status }, true);
    try {
        return await response.arrayBuffer();
    } catch (cause) {
        abortCheck(signal);
        throw new MigrationError(code, message, {}, true, cause);
    }
}

async function fetchAsset(reference, base, signal) {
    abortCheck(signal);
    const url = new URL(reference.path, base);
    requireCondition(url.origin === new URL(base).origin && ["http:", "https:"].includes(url.protocol),
        "MAGDB_ASSET_ORIGIN", "Migration assets must be hosted on the manifest origin.");
    const bytes = await fetchBytes(url, signal, "MAGDB_ASSET_UNAVAILABLE", "A required migration asset could not be loaded.");
    requireCondition(await sha256(bytes) === reference.sha256, "MAGDB_ASSET_HASH_MISMATCH", "Migration asset bytes differ from the manifest.");
    return { bytes, url: url.href };
}

function decodeJson(bytes) {
    try { return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes)); }
    catch (cause) { throw new MigrationError("MAGDB_ASSET_INVALID", "A migration asset is not valid UTF-8 JSON.", {}, false, cause); }
}

async function jsonAsset(reference, base, signal) {
    const { bytes, url } = await fetchAsset(reference, base, signal);
    return { document: decodeJson(bytes), url };
}

async function loadTransform(operation, base, signal) {
    const module = await loadModule({ path: operation.module, sha256: operation.sha256 }, base, signal);
    const transform = module[operation.exportName ?? "transform"];
    requireCondition(typeof transform === "function", "MAGDB_HOOK_INVALID", "Custom migration module does not export its declared transform.");
    return transform;
}

async function loadModule(reference, base, signal) {
    const { bytes } = await fetchAsset(reference, base, signal);
    let module = moduleCache.get(reference.sha256);
    if (!module) {
        // Import the verified bytes, not a second potentially different HTTP response.
        // Custom modules are self-contained ES modules; CSP must permit blob: scripts.
        const blob = URL.createObjectURL(new Blob([bytes], { type: "text/javascript" }));
        try { module = await awaitExtension(import(blob), signal); moduleCache.set(reference.sha256, module); }
        finally { URL.revokeObjectURL(blob); }
    }
    return module;
}

function hookFailure(cause) {
    return new MigrationError("MAGDB_HOOK_FAILED",
        "A migration transform failed. Developer review is required before another attempt.");
}

function transformRecord(operation, record, hook, context) {
    requireCondition(record && typeof record === "object" && !Array.isArray(record), "MAGDB_RECORD_INVALID", "A field transform requires an object record.");
    if (operation.op === "transform") {
        const input = structuredClone(record);
        try {
            const result = hook(input, Object.freeze(context));
            if (result && typeof result.then === "function")
                return Promise.resolve(result).catch(cause => { throw hookFailure(cause); });
            return result;
        } catch (cause) { throw hookFailure(cause); }
    }
    const value = structuredClone(record);
    if (operation.op === "setMissing" && !Object.hasOwn(value, operation.path)) value[operation.path] = structuredClone(operation.value);
    if (operation.op === "renameField" && Object.hasOwn(value, operation.from)) {
        requireCondition(!Object.hasOwn(value, operation.to), "MAGDB_FIELD_CONFLICT", "Renaming a field would overwrite an existing value.");
        value[operation.to] = value[operation.from]; delete value[operation.from];
    }
    if (operation.op === "deleteField") delete value[operation.path];
    return value;
}

function applyAtomic(db, transaction, document, hooks, finish, fail) {
    let position = 0;
    const next = () => {
        try {
            if (position === document.operations.length) { finish(); return; }
            const index = position++;
            const operation = document.operations[index];
            switch (operation.op) {
                case "createStore": createStore(db, operation.definition); next(); return;
                case "deleteStore": db.deleteObjectStore(operation.store); next(); return;
                case "renameStore": transaction.objectStore(operation.store).name = operation.to; next(); return;
                case "createIndex": {
                    const i = operation.index;
                    transaction.objectStore(operation.store).createIndex(i.name, i.keyPath, { unique: i.unique, multiEntry: i.multiEntry }); next(); return;
                }
                case "deleteIndex": transaction.objectStore(operation.store).deleteIndex(operation.name); next(); return;
                default: {
                    const request = transaction.objectStore(operation.store).openCursor();
                    request.onsuccess = () => {
                        try {
                            const cursor = request.result;
                            if (!cursor) { next(); return; }
                            const result = transformRecord(operation, cursor.value, hooks.get(index), { schemaId: document.schemaId,
                                migrationId: document.id, store: operation.store, primaryKey: cursor.primaryKey });
                            if (result && typeof result.then === "function") {
                                // An async hook may already be rejected. Consume
                                // that rejection while aborting the transaction.
                                Promise.resolve(result).catch(() => {});
                                throw new MigrationError("MAGDB_ASYNC_ATOMIC_HOOK", "Atomic transforms must be synchronous; use resumable mode for asynchronous work.");
                            }
                            requireCondition(result && typeof result === "object" && !Array.isArray(result),
                                "MAGDB_RECORD_INVALID", "A transform must return an object record.");
                            cursor.update(result);
                            cursor.continue();
                        } catch (error) { fail(error); }
                    };
                }
            }
        } catch (error) { fail(error); }
    };
    next();
}

function readyState(schemaId, revision, schemaHash, history = [], protocolVersion = 1) {
    return { protocolVersion, schemaId, revision, schemaHash, status: "ready", history, active: null };
}

function assertState(snapshot, schemaId) {
    const state = snapshot.state;
    requireCondition([1, 2, 3].includes(state?.protocolVersion) && state.schemaId === schemaId && Number.isSafeInteger(state.revision) &&
        state.revision > 0 && /^sha256:[0-9a-f]{64}$/.test(state.schemaHash) &&
        ["ready", "migrating", "recovering"].includes(state.status),
    "MAGDB_STATE_INVALID", "The managed database journal is invalid or belongs to another schema.");
    validateCompletedHistory(state.history, state.revision);
    const recovered = state.history.some(entry => entry.outcome === "recovered");
    requireCondition((state.protocolVersion === 3 || state.protocolVersion === (recovered || state.status === "recovering" ? 2 : 1)) &&
        (state.status === "recovering") === Object.hasOwn(state, "recovery"),
        "MAGDB_STATE_INVALID", "Recovery requires a recovery-aware journal.");
    if (state.status === "recovering") {
        const { recovery } = state;
        requireCondition(recovery && typeof recovery.id === "string" && /^sha256:[0-9a-f]{64}$/.test(recovery.sha256) &&
            ["repair", "verify"].includes(recovery.phase) && validProgress(recovery.repaired) &&
            validProgress(recovery.verified) && recovery.verified <= recovery.repaired &&
            (recovery.phase === "verify" || recovery.verified === 0) && validCheckpointKey(recovery),
            "MAGDB_STATE_INVALID", "Invalid recovery progress.");
    }
    if (state.status === "ready") requireCondition(state.active === null,
        "MAGDB_STATE_INVALID", "A ready journal cannot contain an unfinished checkpoint.");
    else {
        const active = state.active;
        requireCondition(active && typeof active.id === "string" && active.id && /^sha256:[0-9a-f]{64}$/.test(active.sha256) &&
            Number.isSafeInteger(active.operationIndex) && active.operationIndex >= 0 &&
            Number.isSafeInteger(active.processedRecords) && active.processedRecords >= 0 &&
            Object.hasOwn(active, "lastKey"), "MAGDB_STATE_INVALID", "The migration checkpoint is malformed.");
        requireCondition(validCheckpointKey(active), "MAGDB_STATE_INVALID", "The migration checkpoint key is invalid.");
    }
}

function validProgress(count) { return Number.isSafeInteger(count) && count >= 0; }

function validCheckpointKey(progress) {
    if (!Object.hasOwn(progress, "lastKey")) return false;
    if (progress.lastKey === null) return true;
    try { return indexedDB.cmp(progress.lastKey, progress.lastKey) === 0; } catch { return false; }
}

function sameState(left, right) {
    if (!left || !right) return false;
    const { active: leftActive, recovery: leftRecovery, ...leftHeader } = left;
    const { active: rightActive, recovery: rightRecovery, ...rightHeader } = right;
    return canonicalJson(leftHeader) === canonicalJson(rightHeader) &&
        sameCheckpoint(leftActive, rightActive) && sameCheckpoint(leftRecovery, rightRecovery);
}

function sameCheckpoint(left, right) {
    if (!left || !right) return left === right;
    const { lastKey: leftKey, ...leftProgress } = left;
    const { lastKey: rightKey, ...rightProgress } = right;
    if (canonicalJson(leftProgress) !== canonicalJson(rightProgress)) return false;
    if (leftKey === null || rightKey === null) return leftKey === rightKey;
    try { return indexedDB.cmp(leftKey, rightKey) === 0; } catch { return false; }
}

async function readBatch(db, storeName, lastKey, batchSize) {
    const transaction = db.transaction(storeName, "readonly");
    const done = transactionDone(transaction);
    const rows = [];
    const request = transaction.objectStore(storeName).openCursor(lastKey == null ? null : IDBKeyRange.lowerBound(lastKey, true));
    request.onsuccess = () => {
        const cursor = request.result;
        if (cursor && rows.length < batchSize) { rows.push({ key: cursor.primaryKey, value: cursor.value }); if (rows.length < batchSize) cursor.continue(); }
    };
    await done;
    return rows;
}

function keyOf(value, path) {
    if (Array.isArray(path)) return path.map(part => keyOf(value, part));
    return path.split(".").reduce((result, part) => result?.[part], value);
}

function checkAuthorization(options) {
    options.protection?.assertCurrent();
    abortCheck(options.signal);
}

function fenceBinding(state) {
    return { schemaId: state.schemaId, revision: state.revision, schemaHash: state.schemaHash,
        migrationId: state.active.id, migrationHash: state.active.sha256 };
}

function sameEnvelope(left, right) { return left == null && right == null || equalEnvelope(left, right); }

function assertPinnedSnapshot(options, snapshot, expectedState) {
    requireCondition(sameState(snapshot.state, expectedState),
        "MAGDB_CONCURRENT_CHANGE", "The migration journal changed after preflight.");
    if (options.protection)
        requireCondition(sameEnvelope(snapshot.protectionPolicy, options.protectionSnapshot.policy) &&
            sameEnvelope(snapshot.protectionState, options.protectionSnapshot.seal),
            "MAGDB_PROTECTION_STATE_INVALID", "The authenticated policy or journal proof changed after preflight.");
}

async function authenticateSnapshot(options, snapshot, schema) {
    if (!options.protection) {
        requireCondition(!snapshot?.hasProtectionMetadata && snapshot?.state?.protocolVersion !== 3,
            "MAGDB_AUTHORIZATION_REQUIRED", "This database requires authenticated migration authorization.");
        return;
    }
    checkAuthorization(options);
    requireCondition(!snapshot?.protectionState || snapshot?.state?.protocolVersion === 3,
        "MAGDB_PROTECTION_STATE_INVALID", "An authenticated journal proof cannot be adopted as legacy progress.");
    if (snapshot?.state?.protocolVersion === 3) {
        requireCondition(snapshot.protectionPolicy && snapshot.protectionState,
            "MAGDB_PROTECTION_STATE_INVALID", "The authenticated journal is missing its policy or state proof.");
        if (snapshot.state.status === "ready") await options.protection.verifyPolicy(snapshot.protectionPolicy, schema);
        else await options.protection.authenticateFence(snapshot.protectionPolicy, fenceBinding(snapshot.state));
        await options.protection.verifyState(snapshot.protectionState, snapshot.state);
    } else if (snapshot?.protectionPolicy) {
        requireCondition(!snapshot.state || snapshot.state.status === "ready", "MAGDB_PROTECTION_STATE_INVALID",
            "An unauthenticated interrupted journal cannot be adopted as protected progress.");
        await options.protection.verifyPolicy(snapshot.protectionPolicy, schema, { legacyOnly: true });
    } else {
        requireCondition(!snapshot?.hasProtectionMetadata, "MAGDB_PROTECTION_STATE_INVALID",
            "Authenticated metadata is missing its policy; initialization cannot replace a damaged identity.");
        requireCondition(options.initialize === true && !hasProtection(schema) || !snapshot && options.initialize === true,
            "MAGDB_AUTHORIZATION_REQUIRED", "Explicit initialization is required to establish an authenticated database; protected existing data requires its original policy.");
        requireCondition(!snapshot?.state || snapshot.state.status === "ready", "MAGDB_PROTECTION_STATE_INVALID",
            "Finish an unauthenticated migration before establishing protection.");
    }
    options.protectionSnapshot = { policy: snapshot?.protectionPolicy ?? null, seal: snapshot?.protectionState ?? null };
    checkAuthorization(options);
}

async function prepareProtection(options, state, schema, replacePolicy = false) {
    if (!options.protection) return null;
    checkAuthorization(options);
    const policy = replacePolicy ? state.status === "ready"
        ? await options.protection.createPolicy(schema)
        : await options.protection.createFence(fenceBinding(state)) : options.protectionSnapshot.policy;
    const seal = await options.protection.sealState(state);
    checkAuthorization(options);
    return { policy, seal };
}

function writeProtection(db, tx, prepared) {
    if (!prepared) return;
    const table = db.objectStoreNames.contains(encryptionMetadataStore) ? tx.objectStore(encryptionMetadataStore)
        : db.createObjectStore(encryptionMetadataStore);
    table.put(prepared.policy, encryptionMetadataKey);
    table.put(prepared.seal, protectedStateKey);
}

// IndexedDB requests keep this transaction active. All Web Crypto work has
// already completed; only exact-envelope comparisons and native writes occur here.
function guardStoragePolicy(options, db, tx, schema, apply, fail, legacyProtected = false) {
    const expected = options.protectionSnapshot;
    const hasNumericMetadata = db.objectStoreNames.contains(numericStore);
    const numeric = hasNumericMetadata ? tx.objectStore(numericStore).get("policy") : null;
    const table = options.protection && db.objectStoreNames.contains(encryptionMetadataStore) ? tx.objectStore(encryptionMetadataStore) : null;
    const policy = table?.get(encryptionMetadataKey), seal = table?.get(protectedStateKey);
    const requests = [numeric, policy, seal].filter(Boolean);
    let applied = false;
    const ready = () => {
        if (applied || requests.some(request => request.readyState !== "done")) return;
        applied = true;
        try {
            checkAuthorization(options);
            assertNumericPolicy({ numericPolicy: numeric?.result ?? null, hasNumericMetadata }, schema, legacyProtected);
            if (options.protection)
                requireCondition(sameEnvelope(policy?.result, expected?.policy) && sameEnvelope(seal?.result, expected?.seal),
                    "MAGDB_PROTECTION_STATE_INVALID", "The authenticated policy or journal changed before commit.");
            apply();
        } catch (error) { fail(error); }
    };
    for (const request of requests) request.onsuccess = ready;
    ready();
}

async function resumeMigration(options, db, snapshot, step) {
    const { document, reference, hooks, from, to, toHash } = step;
    let state = snapshot.state;
    while (state.active.operationIndex < document.operations.length) {
        checkAuthorization(options);
        const index = state.active.operationIndex;
        const operation = document.operations[index];
        const rows = await readBatch(db, operation.store, state.active.lastKey, options.batchSize ?? 256);
        const results = [];
        const keyPath = from.stores.find(store => store.name === operation.store).keyPath;
        for (const row of rows) {
            checkAuthorization(options);
            const logical = options.protection ? await options.protection.openRecord(row.value, from, operation.store, row.key) : row.value;
            const result = structuredClone(await awaitExtension(transformRecord(operation, logical, hooks.get(index), { schemaId: document.schemaId,
                migrationId: document.id, store: operation.store, primaryKey: structuredClone(row.key), signal: options.signal }), options.signal));
            requireCondition(result && typeof result === "object" && !Array.isArray(result),
                "MAGDB_RECORD_INVALID", "A transform must return an object record.");
            if (keyPath !== null) requireCondition(equalNativeKey(keyOf(result, keyPath), row.key),
                "MAGDB_KEY_CHANGE_UNSUPPORTED", "Resumable transforms must preserve primary keys; a staging migration is required.");
            // Only ciphertext (or explicitly approved public target values) is
            // carried into the transaction. Web Crypto never runs inside it.
            results.push(options.protection ? await options.protection.sealRecord(result, to, operation.store, row.key) : result);
        }
        const updated = structuredClone(state);
        if (rows.length) {
            updated.active.lastKey = rows.at(-1).key;
            updated.active.processedRecords += rows.length;
        } else { updated.active.operationIndex++; updated.active.lastKey = null; }
        const prepared = await prepareProtection(options, updated, from);
        await recoveryTransaction(options, db, state, from, (tx, metadata) => {
            const store = tx.objectStore(operation.store);
            for (let i = 0; i < rows.length; i++) keyPath === null ? store.put(results[i], rows[i].key) : store.put(results[i]);
            metadata.put(updated, stateKey);
        }, prepared);
        state = updated;
        notify(options, "checkpoint", { migrationId: document.id, revision: state.revision,
            targetRevision: document.toRevision, processedRecords: state.active.processedRecords });
    }
    const history = [...state.history, { id: document.id, sha256: reference.sha256 }];
    const completed = readyState(document.schemaId, document.toRevision, toHash, history, state.protocolVersion);
    const prepared = await prepareProtection(options, completed, to, true);
    await recoveryTransaction(options, db, state, to, (tx, metadata) => {
        requireCondition(state.active.operationIndex === document.operations.length, "MAGDB_CONCURRENT_CHANGE", "Migration finalization lost its checkpoint.");
        metadata.put(completed, stateKey);
        writeNumericPolicy(db, tx, to);
    }, prepared);
    return completed;
}

/** Opening is a readiness boundary: resolution occurs only after the journal commits. */
export function ensureDatabase(options) { return runManaged(options, false); }

/** Explicit recovery commits only its original edge; normal opening handles later revisions. */
export function recoverDatabase(options) { return runManaged(options, true); }

async function runManaged(options, recovering) {
    options ??= {};
    try {
        requireCondition(typeof options.name === "string" && options.name.length > 0, "MAGDB_NAME_REQUIRED", "A physical database name is required.");
        // Pin semantic inputs before any await (including lock acquisition). User
        // callbacks and subsequent configuration edits must not change a planned
        // transition after its schema hash has been calculated.
        try {
            options = { ...options, schema: structuredClone(options.schema),
                ...(options.manifest ? { manifest: structuredClone(options.manifest) } : {}),
                ...(recovering ? { recovery: structuredClone(options.recovery) } : {}) };
        } catch (cause) {
            throw new MigrationError("MAGDB_INVALID_DOCUMENT", "Migration inputs must be serializable data.", {}, false, cause);
        }
        if (options.protection !== undefined) {
            requireCondition(isProtectedMigrationAdapter(options.protection) && options.protection.name === options.name,
                "MAGDB_AUTHORIZATION_REQUIRED", "Use an authenticated migration adapter for this exact physical database.");
            options.protectionSnapshot = null;
            if (options.protection.signal) options.signal = options.signal
                ? AbortSignal.any([options.signal, options.protection.signal]) : options.protection.signal;
            requireProtectedSchema(options.schema);
            checkAuthorization(options);
        } else {
            requirePlainSchema(options.schema);
            requireCondition(!options.schema.requiredCapabilities?.includes("authenticatedMigrationsV1"),
                "MAGDB_AUTHORIZATION_REQUIRED", "The declared database lifecycle requires authenticated authorization.");
            requireCondition(!options.name.startsWith("__magic_protected__:"), "MAGDB_PROTECTED_ADAPTER_REQUIRED",
                "Protected migrations must run through an authenticated storage adapter.");
        }
        requireCondition(options.batchSize == null || Number.isSafeInteger(options.batchSize) && options.batchSize > 0 && options.batchSize <= 10000,
            "MAGDB_BATCH_INVALID", "batchSize must be between 1 and 10000.");
        requireCondition(navigator.locks?.request, "MAGDB_LOCK_UNAVAILABLE", "Managed migrations require the Web Locks API in a secure context.");
        return await navigator.locks.request(`magic-indexeddb:migrate:${options.name}`, { mode: "exclusive", signal: options.signal },
            () => ensureLocked(options, recovering));
    } catch (cause) {
        const error = options.protection?.signal?.aborted ? new MigrationError("MAGDB_AUTHORIZATION_LOST", "Migration authorization was revoked; committed progress is retained.", {}, true)
            : cause instanceof MigrationError ? cause
            : cause?.name === "QuotaExceededError" ? new MigrationError("MAGDB_QUOTA_EXCEEDED", "Free storage before retrying the same package.", {}, true, cause)
            : options.signal?.aborted ? new MigrationError("MAGDB_CANCELLED", "Migration cancelled; committed checkpoints are retained.", {}, true, cause)
            : new MigrationError("MAGDB_MIGRATION_FAILED", "Migration did not complete. Inspect its state and diagnose the failure before retrying.", {}, false, cause);
        // Platform exceptions (including structuredClone failures) can include
        // values returned by application code. Protected diagnostics expose only
        // the fixed public diagnostic, never that exception chain.
        if (options.protection) delete error.cause;
        const authorizationPhase = { MAGDB_AUTHORIZATION_REQUIRED: "authorizationRequired", MAGDB_AUTHORIZATION_REJECTED: "authorizationRejected",
            MAGDB_AUTHORIZATION_LOST: "authorizationLost" }[error.code];
        notify(options, authorizationPhase ?? "failed", { code: error.code, retryable: authorizationPhase ? true : error.retryable, documentationUrl: error.documentationUrl });
        throw error;
    }
}

async function loadPinnedManifest(options) {
    let manifest;
    const base = new URL(options.manifestUrl ?? ".", globalThis.location.href).href;
    requireCondition(new URL(base).origin === globalThis.location.origin && ["http:", "https:"].includes(new URL(base).protocol),
        "MAGDB_ASSET_ORIGIN", "The application-pinned manifest must use the application's HTTP(S) origin.");
    if (options.manifestUrl) {
        const bytes = await fetchBytes(base, options.signal, "MAGDB_MANIFEST_UNAVAILABLE", "The application-pinned manifest is unavailable.");
        manifest = decodeJson(bytes);
    } else if (options.manifest) manifest = structuredClone(options.manifest);
    else manifest = null;

    return { manifest, base };
}

export async function verifyRotationDeployment(options, revision) {
    const { manifest } = await loadPinnedManifest(options);
    if (manifest) validateManifest(manifest, options.schema.schemaId);
    requireCondition((manifest?.targetRevision ?? 1) === revision,
        "MAGIC_ROTATION_DEPLOYMENT_CHANGED", "Resume rotation from the deployment that admitted it before upgrading this vault.");
}

async function ensureLocked(options, recovering) {
    notify(options, "preflight");
    abortCheck(options.signal);
    const expectedHash = await sha256(canonicalJson(options.schema));
    const { manifest, base } = await loadPinnedManifest(options);

    const models = new Map();
    async function model(revision) {
        if (models.has(revision)) return models.get(revision);
        const reference = manifest.schemas.find(entry => entry.revision === revision);
        requireCondition(reference, "MAGDB_HISTORY_INVALID", "Required schema revision is missing.");
        const { document } = await jsonAsset(reference, base, options.signal);
        (options.protection ? requireProtectedSchema : requirePlainSchema)(document);
        requireCondition(document.schemaId === options.schema.schemaId, "MAGDB_SCHEMA_ID_MISMATCH", "Schema asset belongs to another database.");
        const resolved = { schema: document, hash: await sha256(canonicalJson(document)) };
        models.set(revision, resolved);
        return resolved;
    }
    if (manifest) {
        validateManifest(manifest, options.schema.schemaId);
        requireCondition(!options.protection || manifest.requiredCapabilities?.includes("authenticatedMigrationsV1"),
            "MAGDB_CAPABILITY_REQUIRED", "An authenticated deployment must declare authenticatedMigrationsV1.");
        requireCondition(options.protection || !manifest.requiredCapabilities?.includes("authenticatedMigrationsV1"),
            "MAGDB_AUTHORIZATION_REQUIRED", "This deployment requires authenticated migration authorization.");
        const target = await model(manifest.targetRevision);
        requireCondition(target.hash === expectedHash, "MAGDB_MODEL_DRIFT", "The application's resolved model differs from its published target schema.");
    }
    const targetRevision = manifest?.targetRevision ?? 1;
    let snapshot = await inspectDatabaseInternal(options.name, options.signal);
    if (recovering) return recoverLocked(options, snapshot, manifest, base, model);
    let baseline;
    if (snapshot && !snapshot.state) {
        requireCondition(manifest, "MAGDB_ADOPTION_REQUIRED", "Existing unmanaged databases require an explicit validated adoption baseline.");
        for (const candidate of manifest.baselines ?? []) {
            const old = (await jsonAsset(candidate.schema, base, options.signal)).document;
            (options.protection ? requireProtectedSchema : requirePlainSchema)(old);
            requireCondition(old.schemaId === options.schema.schemaId, "MAGDB_BASELINE_INVALID", "Baseline schema identity does not match.");
            if (samePhysical(snapshot.stores, old)) {
                requireCondition(!baseline, "MAGDB_BASELINE_AMBIGUOUS", "Multiple baselines match this database; publish an unambiguous adoption path.");
                const managed = await model(candidate.revision);
                requireCondition(await sha256(canonicalJson(old)) === managed.hash, "MAGDB_BASELINE_INVALID",
                    "A baseline must recognize its matching managed model; use a separate migration to change it.");
                baseline = { revision: candidate.revision, hash: managed.hash };
            }
        }
        requireCondition(baseline, "MAGDB_LEGACY_SCHEMA_UNKNOWN", "The installed database does not match any supported legacy baseline.");
    }
    if (snapshot?.state) {
        assertState(snapshot, options.schema.schemaId);
        requireCondition(snapshot.state.revision <= targetRevision, "MAGDB_DATABASE_NEWER", "This application is older than the database. Load a compatible application.");
        if (snapshot.state.protocolVersion === 2)
            requireCondition(manifest?.protocolVersion === 2, "MAGDB_RECOVERY_BINDING_REQUIRED", "A recovery-aware deployment is required.");
        if (manifest) validateCompletedHistory(snapshot.state.history, snapshot.state.revision, manifest);
        requireCondition(snapshot.state.status !== "recovering", "MAGDB_RECOVERY_REQUIRED", "Explicitly resume the approved recovery package.");
    }
    const startRevision = snapshot?.state?.revision ?? baseline?.revision ?? targetRevision;
    const initial = manifest ? await model(startRevision) : { schema: options.schema, hash: expectedHash };
    if (snapshot) {
        requireCondition(samePhysical(snapshot.stores, initial.schema), "MAGDB_START_SCHEMA_MISMATCH", "Physical database schema differs from its expected starting state.");
        if (snapshot.state) requireCondition(snapshot.state.schemaHash === initial.hash, "MAGDB_MODEL_DRIFT", "Persisted schema identity does not match this history.");
    }
    await authenticateSnapshot(options, snapshot, initial.schema);
    const legacyProtected = Boolean(options.protection && baseline && snapshot.protectionPolicy);
    if (snapshot) assertNumericPolicy(snapshot, initial.schema, legacyProtected);
    const steps = [];
    for (let revision = startRevision; revision < targetRevision; revision++) {
        requireCondition(manifest, "MAGDB_MIGRATION_REQUIRED", "A migration package is required.");
        const reference = manifest.migrations.find(entry => entry.fromRevision === revision);
        requireCondition(reference, "MAGDB_HISTORY_INVALID", "Migration path has a missing revision.");
        const { document, url } = await jsonAsset(reference, base, options.signal);
        const from = await model(revision), to = await model(revision + 1);
        validateMigration(document, reference, from.schema, to.schema, { authenticated: Boolean(options.protection) });
        requireCondition(document.fromSchemaHash === from.hash && document.toSchemaHash === to.hash,
            "MAGDB_MODEL_DRIFT", "Migration schema fingerprints do not match its assets.");
        const hooks = new Map();
        for (let i = 0; i < document.operations.length; i++) if (document.operations[i].op === "transform") {
            hooks.set(i, await loadTransform(document.operations[i], url, options.signal));
        }
        steps.push({ reference, document, from: from.schema, to: to.schema, toHash: to.hash, hooks });
    }
    if (snapshot?.state?.status === "migrating") requireCondition(steps[0]?.document.id === snapshot.state.active?.id &&
        steps[0].reference.sha256 === snapshot.state.active.sha256 && steps[0].document.mode === "resumable",
    "MAGDB_RESUME_PACKAGE_REQUIRED", "Resume requires the same original migration package.");

    let db, expectedState = snapshot?.state;
    try {
        // All descriptors and custom code are verified before the first write.
        if (!snapshot) {
            const initialState = readyState(options.schema.schemaId, targetRevision, expectedHash, [], options.protection ? 3 : 1);
            const prepared = await prepareProtection(options, initialState, options.schema, true);
            db = await upgrade(options, 1, (connection, transaction, oldVersion) => {
                checkAuthorization(options);
                requireCondition(oldVersion === 0, "MAGDB_CONCURRENT_CHANGE", "A database appeared during bootstrap.");
                for (const store of options.schema.stores) createStore(connection, store);
                connection.createObjectStore(metadataStore).put(initialState, stateKey);
                writeNumericPolicy(connection, transaction, options.schema);
                writeProtection(connection, transaction, prepared);
            });
            if (prepared) options.protectionSnapshot = prepared;
            expectedState = initialState;
        } else if (baseline) {
            notify(options, "adopting", { revision: baseline.revision, targetRevision });
            const adopted = readyState(options.schema.schemaId, baseline.revision, baseline.hash, [], options.protection ? 3 : 1);
            const prepared = await prepareProtection(options, adopted, initial.schema, true);
            db = await upgrade(options, snapshot.nativeVersion + 1, (connection, transaction, oldVersion, fail) => {
                requireCondition(oldVersion === snapshot.nativeVersion && samePhysical(nativeStores(connection, transaction), initial.schema),
                    "MAGDB_CONCURRENT_CHANGE", "Legacy database changed before adoption.");
                requireCondition(!connection.objectStoreNames.contains(metadataStore), "MAGDB_STATE_INVALID", "Reserved metadata already exists.");
                guardStoragePolicy(options, connection, transaction, initial.schema, () => {
                    connection.createObjectStore(metadataStore).put(adopted, stateKey);
                    writeNumericPolicy(connection, transaction, initial.schema);
                    writeProtection(connection, transaction, prepared);
                }, fail, legacyProtected);
            });
            if (prepared) options.protectionSnapshot = prepared;
            expectedState = adopted;
        } else if (options.protection && snapshot.state.protocolVersion !== 3) {
            const adopted = { ...snapshot.state, protocolVersion: 3 };
            const prepared = await prepareProtection(options, adopted, initial.schema, true);
            db = await upgrade(options, snapshot.nativeVersion + 1, (connection, transaction, oldVersion, fail) => {
                requireCondition(oldVersion === snapshot.nativeVersion && samePhysical(nativeStores(connection, transaction), initial.schema),
                    "MAGDB_CONCURRENT_CHANGE", "Database changed before authenticated adoption.");
                const request = transaction.objectStore(metadataStore).get(stateKey);
                request.onsuccess = () => guardStoragePolicy(options, connection, transaction, initial.schema, () => {
                    requireCondition(sameState(request.result, snapshot.state), "MAGDB_CONCURRENT_CHANGE", "The journal changed before authenticated adoption.");
                    transaction.objectStore(metadataStore).put(adopted, stateKey);
                    writeNumericPolicy(connection, transaction, initial.schema);
                    writeProtection(connection, transaction, prepared);
                }, fail);
            });
            options.protectionSnapshot = prepared;
            expectedState = adopted;
        } else db = await openExisting(options.name, options.signal);
        requireCondition(db, "MAGDB_DATABASE_MISSING", "Database disappeared before migration.");
        for (const step of steps) {
            checkAuthorization(options);
            snapshot = await inspectOpen(db);
            const { document, reference, to, toHash } = step;
            assertPinnedSnapshot(options, snapshot, expectedState);
            requireCondition(snapshot.state?.revision === document.fromRevision &&
                snapshot.state.schemaHash === document.fromSchemaHash &&
                (snapshot.state.status === "ready" || snapshot.state.status === "migrating" &&
                    snapshot.state.active?.id === document.id && snapshot.state.active.sha256 === reference.sha256),
                "MAGDB_CONCURRENT_CHANGE", "The journal no longer matches the preflighted migration edge.");
            await authenticateSnapshot(options, snapshot, step.from);
            notify(options, "migrating", { migrationId: document.id, revision: document.fromRevision, targetRevision });
            if (document.mode === "atomic") {
                const completed = readyState(document.schemaId, document.toRevision, toHash,
                    [...snapshot.state.history, { id: document.id, sha256: reference.sha256 }], snapshot.state.protocolVersion);
                const prepared = await prepareProtection(options, completed, to, true);
                db.close();
                db = await upgrade(options, snapshot.nativeVersion + 1, (connection, transaction, oldVersion, fail) => {
                    requireCondition(oldVersion === snapshot.nativeVersion && samePhysical(nativeStores(connection, transaction), step.from),
                        "MAGDB_CONCURRENT_CHANGE", "Database changed before atomic upgrade.");
                    const get = transaction.objectStore(metadataStore).get(stateKey);
                    get.onsuccess = () => {
                        try {
                            requireCondition(canonicalJson(get.result) === canonicalJson(snapshot.state), "MAGDB_CONCURRENT_CHANGE", "Migration journal changed.");
                            guardStoragePolicy(options, connection, transaction, step.from, () => applyAtomic(connection, transaction, document, step.hooks, () => {
                                requireCondition(samePhysical(nativeStores(connection, transaction), to), "MAGDB_TARGET_MISMATCH", "Migration did not produce its target schema.");
                                transaction.objectStore(metadataStore).put(completed, stateKey);
                                writeNumericPolicy(connection, transaction, to);
                                writeProtection(connection, transaction, prepared);
                            }, fail), fail);
                        } catch (error) { fail(error); }
                    };
                });
                if (prepared) options.protectionSnapshot = prepared;
                expectedState = completed;
            } else {
                if (snapshot.state.status === "ready") {
                    const admitted = { ...snapshot.state, status: "migrating",
                        active: { id: document.id, sha256: reference.sha256, operationIndex: 0, lastKey: null, processedRecords: 0 } };
                    const prepared = await prepareProtection(options, admitted, step.from, true);
                    db.close();
                    db = await upgrade(options, snapshot.nativeVersion + 1, (connection, transaction, oldVersion, fail) => {
                        requireCondition(oldVersion === snapshot.nativeVersion && samePhysical(nativeStores(connection, transaction), step.from),
                            "MAGDB_CONCURRENT_CHANGE", "Database changed before resumable migration.");
                        const request = transaction.objectStore(metadataStore).get(stateKey);
                        request.onsuccess = () => guardStoragePolicy(options, connection, transaction, step.from, () => {
                            requireCondition(sameState(request.result, snapshot.state), "MAGDB_CONCURRENT_CHANGE", "The journal changed before migration admission.");
                            transaction.objectStore(metadataStore).put(admitted, stateKey);
                            writeProtection(connection, transaction, prepared);
                        }, fail);
                    });
                    if (prepared) options.protectionSnapshot = prepared;
                    snapshot = await inspectOpen(db);
                    assertPinnedSnapshot(options, snapshot, admitted);
                }
                expectedState = await resumeMigration(options, db, snapshot, step);
            }
        }
        const final = await inspectOpen(db);
        assertPinnedSnapshot(options, final, expectedState);
        requireCondition(final.state?.status === "ready" && final.state.revision === targetRevision && final.state.schemaHash === expectedHash &&
            samePhysical(final.stores, options.schema), "MAGDB_TARGET_MISMATCH", "Database is not ready at the application's target revision.");
        await authenticateSnapshot(options, final, options.schema);
        assertNumericPolicy(final, options.schema);
        const result = { name: options.name, schemaId: options.schema.schemaId, revision: targetRevision,
            schemaHash: expectedHash, nativeVersion: final.nativeVersion, protocolVersion: final.state.protocolVersion, history: final.state.history,
            ...(options.protection ? { journal: final.state, journalSeal: final.protectionState,
                policySeal: final.protectionPolicy, numericPolicy: final.numericPolicy } : {}) };
        notify(options, "ready", { revision: targetRevision, targetRevision });
        return result;
    } finally { db?.close(); }
}

/** Check pinned readiness before handing a managed connection to application work. */
export async function assertDatabaseReady(db, expected, protection = null) {
    requireCondition(db.objectStoreNames.contains(metadataStore), "MAGDB_ADOPTION_REQUIRED", "Managed metadata is missing.");
    const transaction = db.transaction(metadataStore, "readonly");
    const done = transactionDone(transaction);
    const state = await requestResult(transaction.objectStore(metadataStore).get(stateKey));
    await done;
    requireCondition(state?.status === "ready" && state.protocolVersion === (expected.protocolVersion ?? 1) &&
        (state.protocolVersion === 1 || canonicalJson(state.history) === canonicalJson(expected.history)) && state.schemaId === expected.schemaId && state.revision === expected.revision &&
        state.schemaHash === expected.schemaHash && db.version === expected.nativeVersion, "MAGDB_DATABASE_INCOMPATIBLE",
    "Database is migrating or has changed since this application opened it. Reload or explicitly retry initialization.");
    if (state.protocolVersion === 3) {
        requireCondition(isProtectedMigrationAdapter(protection) && protection.name === db.name,
            "MAGDB_AUTHORIZATION_REQUIRED", "Authenticated readiness requires an active authorization capability.");
        const snapshot = await inspectOpen(db);
        requireCondition(sameState(snapshot.state, state) && sameEnvelope(snapshot.protectionState, expected.journalSeal) &&
            sameEnvelope(snapshot.protectionPolicy, expected.policySeal),
            "MAGDB_PROTECTION_STATE_INVALID", "Authenticated readiness changed since initialization.");
        requireCondition(canonicalJson(snapshot.numericPolicy) === canonicalJson(expected.numericPolicy),
            "MAGDB_CODEC_POLICY_MISMATCH", "Numeric policy changed since initialization.");
        await protection.verifyState(snapshot.protectionState, state);
        protection.assertCurrent();
    }
}

async function recoverLocked(options, snapshot, manifest, base, model) {
    const requested = options.recovery;
    const binding = manifest?.recoveries?.find(item => item.id === requested?.id && item.sha256 === requested?.sha256);
    requireCondition(binding && Object.keys(requested).length === 2, "MAGDB_RECOVERY_MISMATCH", "The explicit recovery ID and digest must match an approved manifest binding.");
    requireCondition(snapshot?.state, "MAGDB_RECOVERY_NOT_APPLICABLE", "Recovery requires an existing managed journal.");
    let { state } = snapshot;
    assertState(snapshot, manifest.schemaId);
    requireCondition(state.revision <= manifest.targetRevision, "MAGDB_DATABASE_NEWER", "Load a compatible newer application.");
    validateCompletedHistory(state.history, state.revision, manifest);
    const installed = await model(state.revision);
    requireCondition(state.schemaHash === installed.hash && samePhysical(snapshot.stores, installed.schema),
        "MAGDB_MODEL_DRIFT", "The installed model or layout does not match the deployment.");
    assertNumericPolicy(snapshot, installed.schema);
    await authenticateSnapshot(options, snapshot, installed.schema);

    const edge = manifest.migrations.find(item => item.fromRevision === binding.fromRevision);
    const from = await model(edge.fromRevision), to = await model(edge.toRevision);
    const { document: migration, url: migrationUrl } = await jsonAsset(edge, base, options.signal);
    validateMigration(migration, edge, from.schema, to.schema, { authenticated: Boolean(options.protection) });
    requireCondition(migration.fromSchemaHash === from.hash && migration.toSchemaHash === to.hash,
        "MAGDB_MODEL_DRIFT", "Original migration fingerprints do not match the approved models.");
    const { bytes, url } = await fetchAsset(binding, base, options.signal);
    const document = decodeJson(bytes);
    requireCondition(await sha256(canonicalJson(document)) === binding.sha256, "MAGDB_RECOVERY_INVALID", "Recovery documents must use canonical JSON.");
    validateRecovery(document, binding, edge, from, to, migration);
    const { store } = document.operation;
    const original = migration.operations[0];
    await fetchAsset({ path: original.module, sha256: original.sha256 }, migrationUrl, options.signal);
    const result = (outcome, revision) => ({ outcome, recoveryId: binding.id, revision, applicationTargetRevision: manifest.targetRevision });

    if (state.revision >= edge.toRevision) {
        const completed = state.history[edge.fromRevision - (state.revision - state.history.length)];
        requireCondition(completed?.id === edge.id && completed.sha256 === edge.sha256,
            "MAGDB_RECOVERY_NOT_APPLICABLE", "The journal does not establish a completed original edge.");
        await fetchAsset(document.module, url, options.signal);
        return result(completed.outcome === "recovered" ? "alreadyRecovered" : "alreadyCompletedOriginal", state.revision);
    }
    requireCondition(state.revision === edge.fromRevision && (state.status === "migrating" || state.status === "recovering") &&
        state.active.id === edge.id && state.active.sha256 === edge.sha256 &&
        (state.active.operationIndex === 0 || state.active.operationIndex === 1 && state.active.lastKey === null),
        "MAGDB_RECOVERY_NOT_APPLICABLE", "The journal does not match the supported interrupted transform.");
    if (state.status === "recovering") requireCondition(state.recovery.id === binding.id && state.recovery.sha256 === binding.sha256,
        "MAGDB_RECOVERY_PACKAGE_REQUIRED", "Resume with the exact originally admitted recovery package.");

    const hooks = await loadModule(document.module, url, options.signal);
    requireCondition(["repairProcessed", "repairUnprocessed", "verify"].every(name => typeof hooks[name] === "function"),
        "MAGDB_RECOVERY_INVALID", "Recovery requires repairProcessed, repairUnprocessed and verify exports.");
    let db = await openExisting(options.name, options.signal);
    requireCondition(db, "MAGDB_DATABASE_MISSING", "The interrupted database disappeared.");
    try {
        const total = await validateOriginalBoundary(db, store, state.active);
        if (state.status === "migrating") {
            db.close();
            const prior = state;
            state = { ...prior, protocolVersion: options.protection ? 3 : 2, status: "recovering",
                recovery: { id: binding.id, sha256: binding.sha256, phase: "repair", lastKey: null, repaired: 0, verified: 0 } };
            const prepared = await prepareProtection(options, state, from.schema);
            db = await upgrade(options, snapshot.nativeVersion + 1, (connection, tx, oldVersion, fail) => {
                requireCondition(oldVersion === snapshot.nativeVersion && samePhysical(nativeStores(connection, tx), from.schema),
                    "MAGDB_CONCURRENT_CHANGE", "Storage changed before recovery admission.");
                const metadata = tx.objectStore(metadataStore);
                const request = metadata.get(stateKey);
                request.onsuccess = () => {
                    try {
                        requireCondition(sameState(request.result, prior), "MAGDB_CONCURRENT_CHANGE", "The original checkpoint changed.");
                        guardStoragePolicy(options, connection, tx, from.schema, () => {
                            metadata.put(state, stateKey);
                            writeProtection(connection, tx, prepared);
                        }, fail);
                    } catch (error) { fail(error); }
                };
            });
            if (prepared) options.protectionSnapshot = prepared;
        }
        const keyPath = from.schema.stores.find(item => item.name === store).keyPath;
        validateRecoveryProgress(state.recovery, total);
        notify(options, "recovering", { recoveryId: binding.id, migrationId: edge.id, revision: state.revision,
            targetRevision: edge.toRevision, recoveryPhase: state.recovery.phase });
        await validateRecoveryCursor(db, store, state.recovery);
        while (true) {
            checkAuthorization(options);
            const progress = state.recovery;
            const rows = await readBatch(db, store, progress.lastKey, options.batchSize ?? 256);
            const verify = progress.phase === "verify";
            if (!rows.length && verify) break;
            const values = [];
            for (const row of rows) {
                checkAuthorization(options);
                const processed = state.active.operationIndex === 1 || state.active.lastKey !== null &&
                    indexedDB.cmp(row.key, state.active.lastKey) <= 0;
                const hook = verify ? hooks.verify : processed ? hooks.repairProcessed : hooks.repairUnprocessed;
                const logical = options.protection ? await options.protection.openRecord(row.value,
                    verify || processed ? to.schema : from.schema, store, row.key) : row.value;
                let value;
                try {
                    value = await awaitExtension(hook(structuredClone(logical),
                        Object.freeze({ signal: options.signal, primaryKey: structuredClone(row.key) })), options.signal);
                    if (!verify) value = structuredClone(value);
                } catch {
                    abortCheck(options.signal);
                    // Application exceptions can contain the record being repaired.
                    throw new MigrationError(verify ? "MAGDB_RECOVERY_VERIFICATION_FAILED" : "MAGDB_RECOVERY_HOOK_FAILED",
                        "Application recovery logic failed. Developer intervention is required.");
                }
                if (verify) requireCondition(value === true, "MAGDB_RECOVERY_VERIFICATION_FAILED", "The application verifier rejected a repaired record.");
                else {
                    requireCondition(value && typeof value === "object" && !Array.isArray(value),
                        "MAGDB_RECORD_INVALID", "A recovery hook must return an object record.");
                    if (keyPath !== null) requireCondition(equalNativeKey(keyOf(value, keyPath), row.key),
                        "MAGDB_KEY_CHANGE_UNSUPPORTED", "Recovery cannot change primary keys.");
                    values.push(options.protection ? await options.protection.sealRecord(value, to.schema, store, row.key) : value);
                }
            }
            const next = { ...progress };
            if (rows.length) {
                next.lastKey = rows.at(-1).key;
                if (verify) next.verified += rows.length;
                else next.repaired += rows.length;
            } else {
                requireCondition(progress.repaired === total, "MAGDB_CONCURRENT_CHANGE", "The recovery record count changed.");
                next.phase = "verify";
                next.lastKey = null;
            }
            validateRecoveryProgress(next, total);
            const updated = { ...state, recovery: next };
            const prepared = await prepareProtection(options, updated, from.schema);
            await recoveryTransaction(options, db, state, from.schema, (tx, metadata) => {
                const destination = tx.objectStore(store);
                for (let index = 0; index < values.length; index++) {
                    if (keyPath === null) destination.put(values[index], rows[index].key);
                    else destination.put(values[index]);
                }
                metadata.put(updated, stateKey);
            }, prepared);
            state = updated;
            notify(options, "recoveryCheckpoint", { recoveryId: binding.id, migrationId: edge.id, revision: state.revision,
                targetRevision: edge.toRevision, recoveryPhase: next.phase, processedRecords: next.repaired, verifiedRecords: next.verified });
        }
        requireCondition(state.recovery.repaired === total && state.recovery.verified === total,
            "MAGDB_RECOVERY_VERIFICATION_FAILED", "Recovery did not verify the entire store.");
        const history = [...state.history, { id: edge.id, sha256: edge.sha256, outcome: "recovered",
            recovery: { id: binding.id, sha256: binding.sha256 } }];
        const completed = readyState(manifest.schemaId, edge.toRevision, to.hash, history, options.protection ? 3 : 2);
        const prepared = await prepareProtection(options, completed, to.schema, true);
        const audit = { original: state.active, recovery: state.recovery };
        const evidence = options.protection ? await options.protection.sealState(audit) : audit;
        await recoveryTransaction(options, db, state, to.schema, (_tx, metadata) => {
            metadata.add(evidence, "recovery:" + binding.id);
            metadata.put(completed, stateKey);
        }, prepared);
        notify(options, "recovered", { recoveryId: binding.id, migrationId: edge.id, revision: edge.toRevision,
            targetRevision: manifest.targetRevision });
        return result("recovered", edge.toRevision);
    } finally { db.close(); }
}

function equalNativeKey(left, right) {
    try { return indexedDB.cmp(left, right) === 0; } catch { return false; }
}

async function validateOriginalBoundary(db, store, checkpoint) {
    const table = db.transaction(store).objectStore(store);
    const total = requestResult(table.count());
    const prefix = checkpoint.operationIndex === 1 ? total : checkpoint.lastKey === null ? 0
        : requestResult(table.count(IDBKeyRange.upperBound(checkpoint.lastKey)));
    const last = checkpoint.lastKey === null ? null : requestResult(table.getKey(checkpoint.lastKey));
    const [count, processed, lastKey] = await Promise.all([total, prefix, last]);
    requireCondition(processed === checkpoint.processedRecords &&
        (checkpoint.lastKey === null || equalNativeKey(lastKey, checkpoint.lastKey)),
        "MAGDB_STATE_INVALID", "The original checkpoint does not match the native store.");
    return count;
}

function validateRecoveryProgress(progress, total) {
    requireCondition(progress.repaired <= total && progress.verified <= total &&
        (progress.phase !== "verify" || progress.repaired === total), "MAGDB_STATE_INVALID", "Recovery counts do not match the native store.");
}

async function validateRecoveryCursor(db, store, progress) {
    await validateOriginalBoundary(db, store, { operationIndex: 0, lastKey: progress.lastKey,
        processedRecords: progress.phase === "repair" ? progress.repaired : progress.verified });
}

async function recoveryTransaction(options, db, prior, schema, apply, prepared = null) {
    checkAuthorization(options);
    const tx = db.transaction([...db.objectStoreNames], "readwrite");
    const done = transactionDone(tx);
    const metadata = tx.objectStore(metadataStore);
    const cancel = () => { try { tx.abort(); } catch {} };
    options.signal?.addEventListener("abort", cancel, { once: true });
    let failure;
    const read = metadata.get(stateKey);
    read.onsuccess = () => {
        try {
            checkAuthorization(options);
            requireCondition(sameState(read.result, prior) && samePhysical(nativeStores(db, tx), schema),
                "MAGDB_CONCURRENT_CHANGE", "Recovery journal or native layout changed.");
            guardStoragePolicy(options, db, tx, schema, () => {
                apply(tx, metadata);
                writeProtection(db, tx, prepared);
            }, error => { failure = error; tx.abort(); });
        } catch (error) { failure = error; tx.abort(); }
    };
    try { await done; if (prepared) options.protectionSnapshot = prepared; }
    catch (error) { abortCheck(options.signal); throw failure ?? error; }
    finally { options.signal?.removeEventListener("abort", cancel); }
}
