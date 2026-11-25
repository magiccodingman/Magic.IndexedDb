import { MigrationError, metadataStore, canonicalJson, sha256, requirePlainSchema, validateManifest, validateCompletedHistory,
    validateMigration, requireCondition, physicalStores, samePhysical, compareNames } from "./protocol.js";

export { MigrationError };
const stateKey = "state";
const numericStore = "__magic_numeric_policy";
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
    return [...db.objectStoreNames].filter(name => name !== metadataStore && name !== numericStore)
        .map(name => {
            const store = transaction.objectStore(name);
            return { name, keyPath: store.keyPath, autoIncrement: store.autoIncrement,
                indexes: [...store.indexNames].map(indexName => {
                    const index = store.index(indexName);
                    return { name: index.name, keyPath: index.keyPath, unique: index.unique, multiEntry: index.multiEntry };
                }).sort((a, b) => compareNames(a.name, b.name)) };
        }).sort((a, b) => compareNames(a.name, b.name));
}

async function inspectOpen(db) {
    if (!db.objectStoreNames.length) return { name: db.name, nativeVersion: db.version, stores: [], state: null, numericPolicy: null };
    const transaction = db.transaction([...db.objectStoreNames], "readonly");
    const done = transactionDone(transaction);
    const stores = nativeStores(db, transaction);
    const [state, numericPolicy] = await Promise.all([
        db.objectStoreNames.contains(metadataStore) ? requestResult(transaction.objectStore(metadataStore).get(stateKey)) : null,
        db.objectStoreNames.contains(numericStore) ? requestResult(transaction.objectStore(numericStore).get("policy")) : null
    ]);
    await done;
    return { name: db.name, nativeVersion: db.version, stores, state: state ?? null, numericPolicy: numericPolicy ?? null };
}

async function inspectDatabaseInternal(name, signal) {
    const db = await openExisting(name, signal);
    if (!db) return null;
    try { return await inspectOpen(db); } finally { db.close(); }
}

/** Inspect metadata only. Durable record keys are never exposed in diagnostics. */
export async function inspectDatabase(name) {
    const snapshot = await inspectDatabaseInternal(name);
    if (snapshot?.state?.active) {
        const { lastKey, ...progress } = snapshot.state.active;
        snapshot.state.active = { ...progress, hasCheckpoint: lastKey !== null && lastKey !== undefined };
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

function assertNumericPolicy(snapshot, schema) {
    if (schema.numericFormat === "lossless-v1") {
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
    const { bytes } = await fetchAsset({ path: operation.module, sha256: operation.sha256 }, base, signal);
    let module = moduleCache.get(operation.sha256);
    if (!module) {
        // Import the verified bytes, not a second potentially different HTTP response.
        // Custom modules are self-contained ES modules; CSP must permit blob: scripts.
        const blob = URL.createObjectURL(new Blob([bytes], { type: "text/javascript" }));
        try { module = await awaitExtension(import(blob), signal); moduleCache.set(operation.sha256, module); }
        finally { URL.revokeObjectURL(blob); }
    }
    const transform = module[operation.exportName ?? "transform"];
    requireCondition(typeof transform === "function", "MAGDB_HOOK_INVALID", "Custom migration module does not export its declared transform.");
    return transform;
}

function transformRecord(operation, record, hook, context) {
    requireCondition(record && typeof record === "object" && !Array.isArray(record), "MAGDB_RECORD_INVALID", "A field transform requires an object record.");
    if (operation.op === "transform") return hook(structuredClone(record), Object.freeze(context));
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

function readyState(schemaId, revision, schemaHash, history = []) {
    return { protocolVersion: 1, schemaId, revision, schemaHash, status: "ready", history, active: null };
}

function assertState(snapshot, schemaId) {
    const state = snapshot.state;
    requireCondition(state?.protocolVersion === 1 && state.schemaId === schemaId && Number.isSafeInteger(state.revision) &&
        state.revision > 0 && /^sha256:[0-9a-f]{64}$/.test(state.schemaHash) &&
        ["ready", "migrating"].includes(state.status),
    "MAGDB_STATE_INVALID", "The managed database journal is invalid or belongs to another schema.");
    validateCompletedHistory(state.history, state.revision);
    if (state.status === "ready") requireCondition(state.active === null,
        "MAGDB_STATE_INVALID", "A ready journal cannot contain an unfinished checkpoint.");
    else {
        const active = state.active;
        requireCondition(active && typeof active.id === "string" && active.id && /^sha256:[0-9a-f]{64}$/.test(active.sha256) &&
            Number.isSafeInteger(active.operationIndex) && active.operationIndex >= 0 &&
            Number.isSafeInteger(active.processedRecords) && active.processedRecords >= 0 &&
            Object.hasOwn(active, "lastKey"), "MAGDB_STATE_INVALID", "The migration checkpoint is malformed.");
        if (active.lastKey !== null) {
            let valid = false;
            try { valid = indexedDB.cmp(active.lastKey, active.lastKey) === 0; } catch {}
            requireCondition(valid, "MAGDB_STATE_INVALID", "The migration checkpoint key is invalid.");
        }
    }
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

async function resumeMigration(options, db, snapshot, step) {
    const { document, reference, hooks, to, toHash } = step;
    let state = snapshot.state;
    while (state.active.operationIndex < document.operations.length) {
        abortCheck(options.signal);
        const index = state.active.operationIndex;
        const operation = document.operations[index];
        const rows = await readBatch(db, operation.store, state.active.lastKey, options.batchSize ?? 256);
        const results = [];
        const nativeStore = db.transaction(operation.store).objectStore(operation.store);
        const keyPath = nativeStore.keyPath;
        for (const row of rows) {
            abortCheck(options.signal);
            // Hooks may reuse a scratch object or mutate an earlier return value
            // during a later invocation. Pin each result before validating its
            // key and before yielding to another hook; checkpointed writes must
            // use precisely the values that passed those checks.
            const result = structuredClone(await awaitExtension(transformRecord(operation, row.value, hooks.get(index), { schemaId: document.schemaId,
                migrationId: document.id, store: operation.store, primaryKey: structuredClone(row.key), signal: options.signal }), options.signal));
            requireCondition(result && typeof result === "object" && !Array.isArray(result),
                "MAGDB_RECORD_INVALID", "A transform must return an object record.");
            if (keyPath !== null) requireCondition(indexedDB.cmp(keyOf(result, keyPath), row.key) === 0,
                "MAGDB_KEY_CHANGE_UNSUPPORTED", "Resumable transforms must preserve primary keys; a staging migration is required.");
            results.push(result);
        }
        abortCheck(options.signal);
        const transaction = db.transaction([metadataStore, operation.store], "readwrite");
        const done = transactionDone(transaction);
        const previousCheckpoint = structuredClone(state.active);
        const get = transaction.objectStore(metadataStore).get(stateKey);
        let failure;
        get.onsuccess = () => {
            try {
                const live = get.result;
                requireCondition(live?.status === "migrating" && sameCheckpoint(live.active, previousCheckpoint),
                    "MAGDB_CONCURRENT_CHANGE", "Migration progress changed unexpectedly.");
                const store = transaction.objectStore(operation.store);
                for (let i = 0; i < rows.length; i++) keyPath === null ? store.put(results[i], rows[i].key) : store.put(results[i]);
                state = structuredClone(live);
                if (rows.length) {
                    state.active.lastKey = rows[rows.length - 1].key;
                    state.active.processedRecords += rows.length;
                } else { state.active.operationIndex++; state.active.lastKey = null; }
                transaction.objectStore(metadataStore).put(state, stateKey);
            } catch (error) { failure = error; transaction.abort(); }
        };
        try { await done; } catch (error) { throw failure ?? error; }
        notify(options, "checkpoint", { migrationId: document.id, revision: state.revision,
            targetRevision: document.toRevision, processedRecords: state.active.processedRecords });
    }
    abortCheck(options.signal);
    const transaction = db.transaction([...db.objectStoreNames], "readwrite");
    const done = transactionDone(transaction);
    let failure;
    const request = transaction.objectStore(metadataStore).get(stateKey);
    request.onsuccess = () => {
        try {
            requireCondition(request.result?.active?.id === document.id && request.result.active.sha256 === reference.sha256 &&
                request.result.active.operationIndex === document.operations.length, "MAGDB_CONCURRENT_CHANGE", "Migration finalization lost its checkpoint.");
            requireCondition(samePhysical(nativeStores(db, transaction), to), "MAGDB_TARGET_MISMATCH", "Final schema differs from the migration target.");
            const history = [...state.history, { id: document.id, sha256: reference.sha256 }];
            transaction.objectStore(metadataStore).put(readyState(document.schemaId, document.toRevision, toHash, history), stateKey);
            writeNumericPolicy(db, transaction, to);
        } catch (error) { failure = error; transaction.abort(); }
    };
    try { await done; } catch (error) { throw failure ?? error; }
}

/** Opening is a readiness boundary: resolution occurs only after the journal commits. */
export async function ensureDatabase(options) {
    options ??= {};
    try {
        requireCondition(typeof options.name === "string" && options.name.length > 0, "MAGDB_NAME_REQUIRED", "A physical database name is required.");
        // Pin semantic inputs before any await (including lock acquisition). User
        // callbacks and subsequent configuration edits must not change a planned
        // transition after its schema hash has been calculated.
        try {
            options = { ...options, schema: structuredClone(options.schema),
                ...(options.manifest ? { manifest: structuredClone(options.manifest) } : {}) };
        } catch (cause) {
            throw new MigrationError("MAGDB_INVALID_DOCUMENT", "Migration inputs must be serializable data.", {}, false, cause);
        }
        requirePlainSchema(options.schema);
        requireCondition(!options.name.startsWith("__magic_protected__:"), "MAGDB_PROTECTED_ADAPTER_REQUIRED",
        "Protected migrations must run through an authenticated storage adapter.");
        requireCondition(options.batchSize == null || Number.isSafeInteger(options.batchSize) && options.batchSize > 0 && options.batchSize <= 10000,
            "MAGDB_BATCH_INVALID", "batchSize must be between 1 and 10000.");
        requireCondition(navigator.locks?.request, "MAGDB_LOCK_UNAVAILABLE", "Managed migrations require the Web Locks API in a secure context.");
        return await navigator.locks.request(`magic-indexeddb:migrate:${options.name}`, { mode: "exclusive", signal: options.signal },
            () => ensureLocked(options));
    } catch (cause) {
        const error = cause instanceof MigrationError ? cause : new MigrationError(
            cause?.name === "QuotaExceededError" ? "MAGDB_QUOTA_EXCEEDED" : cause?.name === "AbortError" ? "MAGDB_CANCELLED" : "MAGDB_MIGRATION_FAILED",
            "Migration did not complete. Inspect its state and retry with the same immutable package.", {}, true, cause);
        notify(options, "failed", { code: error.code, retryable: error.retryable, documentationUrl: error.documentationUrl });
        throw error;
    }
}

async function ensureLocked(options) {
    notify(options, "preflight");
    abortCheck(options.signal);
    const expectedHash = await sha256(canonicalJson(options.schema));
    let manifest, base = new URL(options.manifestUrl ?? ".", globalThis.location.href).href;
    requireCondition(new URL(base).origin === globalThis.location.origin && ["http:", "https:"].includes(new URL(base).protocol),
        "MAGDB_ASSET_ORIGIN", "The application-pinned manifest must use the application's HTTP(S) origin.");
    if (options.manifestUrl) {
        const bytes = await fetchBytes(base, options.signal, "MAGDB_MANIFEST_UNAVAILABLE", "The application-pinned manifest is unavailable.");
        manifest = decodeJson(bytes);
    } else if (options.manifest) manifest = structuredClone(options.manifest);
    else manifest = null;

    const models = new Map();
    async function model(revision) {
        if (models.has(revision)) return models.get(revision);
        const reference = manifest.schemas.find(entry => entry.revision === revision);
        requireCondition(reference, "MAGDB_HISTORY_INVALID", "Required schema revision is missing.");
        const { document } = await jsonAsset(reference, base, options.signal);
        requirePlainSchema(document);
        requireCondition(document.schemaId === options.schema.schemaId, "MAGDB_SCHEMA_ID_MISMATCH", "Schema asset belongs to another database.");
        const resolved = { schema: document, hash: await sha256(canonicalJson(document)) };
        models.set(revision, resolved);
        return resolved;
    }
    if (manifest) {
        validateManifest(manifest, options.schema.schemaId);
        const target = await model(manifest.targetRevision);
        requireCondition(target.hash === expectedHash, "MAGDB_MODEL_DRIFT", "The application's resolved model differs from its published target schema.");
    }
    const targetRevision = manifest?.targetRevision ?? 1;
    let snapshot = await inspectDatabaseInternal(options.name, options.signal);
    let baseline;
    if (snapshot && !snapshot.state) {
        requireCondition(manifest, "MAGDB_ADOPTION_REQUIRED", "Existing unmanaged databases require an explicit validated adoption baseline.");
        for (const candidate of manifest.baselines ?? []) {
            const old = (await jsonAsset(candidate.schema, base, options.signal)).document;
            requirePlainSchema(old);
            requireCondition(old.schemaId === options.schema.schemaId, "MAGDB_BASELINE_INVALID", "Baseline schema identity does not match.");
            if (samePhysical(snapshot.stores, old)) {
                assertNumericPolicy(snapshot, old);
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
        if (manifest) validateCompletedHistory(snapshot.state.history, snapshot.state.revision, manifest);
    }
    const startRevision = snapshot?.state?.revision ?? baseline?.revision ?? targetRevision;
    const initial = manifest ? await model(startRevision) : { schema: options.schema, hash: expectedHash };
    if (snapshot) {
        requireCondition(samePhysical(snapshot.stores, initial.schema), "MAGDB_START_SCHEMA_MISMATCH", "Physical database schema differs from its expected starting state.");
        assertNumericPolicy(snapshot, initial.schema);
        if (snapshot.state) requireCondition(snapshot.state.schemaHash === initial.hash, "MAGDB_MODEL_DRIFT", "Persisted schema identity does not match this history.");
    }
    const steps = [];
    for (let revision = startRevision; revision < targetRevision; revision++) {
        requireCondition(manifest, "MAGDB_MIGRATION_REQUIRED", "A migration package is required.");
        const reference = manifest.migrations.find(entry => entry.fromRevision === revision);
        requireCondition(reference, "MAGDB_HISTORY_INVALID", "Migration path has a missing revision.");
        const { document, url } = await jsonAsset(reference, base, options.signal);
        const from = await model(revision), to = await model(revision + 1);
        validateMigration(document, reference, from.schema, to.schema);
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

    let db;
    try {
        // All descriptors and custom code are verified before the first write.
        if (!snapshot) {
            db = await upgrade(options, 1, (connection, transaction, oldVersion) => {
                requireCondition(oldVersion === 0, "MAGDB_CONCURRENT_CHANGE", "A database appeared during bootstrap.");
                for (const store of options.schema.stores) createStore(connection, store);
                connection.createObjectStore(metadataStore).put(readyState(options.schema.schemaId, targetRevision, expectedHash), stateKey);
                writeNumericPolicy(connection, transaction, options.schema);
            });
        } else if (baseline) {
            notify(options, "adopting", { revision: baseline.revision, targetRevision });
            db = await upgrade(options, snapshot.nativeVersion + 1, (connection, transaction, oldVersion) => {
                requireCondition(oldVersion === snapshot.nativeVersion && samePhysical(nativeStores(connection, transaction), initial.schema),
                    "MAGDB_CONCURRENT_CHANGE", "Legacy database changed before adoption.");
                requireCondition(!connection.objectStoreNames.contains(metadataStore), "MAGDB_STATE_INVALID", "Reserved metadata already exists.");
                connection.createObjectStore(metadataStore).put(readyState(options.schema.schemaId, baseline.revision, baseline.hash), stateKey);
            });
        } else db = await openExisting(options.name, options.signal);
        requireCondition(db, "MAGDB_DATABASE_MISSING", "Database disappeared before migration.");
        for (const step of steps) {
            abortCheck(options.signal);
            snapshot = await inspectOpen(db);
            const { document, reference, to, toHash } = step;
            notify(options, "migrating", { migrationId: document.id, revision: document.fromRevision, targetRevision });
            if (document.mode === "atomic") {
                db.close();
                db = await upgrade(options, snapshot.nativeVersion + 1, (connection, transaction, oldVersion, fail) => {
                    requireCondition(oldVersion === snapshot.nativeVersion && samePhysical(nativeStores(connection, transaction), step.from),
                        "MAGDB_CONCURRENT_CHANGE", "Database changed before atomic upgrade.");
                    const get = transaction.objectStore(metadataStore).get(stateKey);
                    get.onsuccess = () => {
                        try {
                            requireCondition(canonicalJson(get.result) === canonicalJson(snapshot.state), "MAGDB_CONCURRENT_CHANGE", "Migration journal changed.");
                            applyAtomic(connection, transaction, document, step.hooks, () => {
                                requireCondition(samePhysical(nativeStores(connection, transaction), to), "MAGDB_TARGET_MISMATCH", "Migration did not produce its target schema.");
                                transaction.objectStore(metadataStore).put(readyState(document.schemaId, document.toRevision, toHash,
                                    [...snapshot.state.history, { id: document.id, sha256: reference.sha256 }]), stateKey);
                                writeNumericPolicy(connection, transaction, to);
                            }, fail);
                        } catch (error) { fail(error); }
                    };
                });
            } else {
                if (snapshot.state.status === "ready") {
                    db.close();
                    db = await upgrade(options, snapshot.nativeVersion + 1, (connection, transaction, oldVersion) => {
                        requireCondition(oldVersion === snapshot.nativeVersion && samePhysical(nativeStores(connection, transaction), step.from),
                            "MAGDB_CONCURRENT_CHANGE", "Database changed before resumable migration.");
                        transaction.objectStore(metadataStore).put({ ...snapshot.state, status: "migrating",
                            active: { id: document.id, sha256: reference.sha256, operationIndex: 0, lastKey: null, processedRecords: 0 } }, stateKey);
                    });
                    snapshot = await inspectOpen(db);
                }
                await resumeMigration(options, db, snapshot, step);
            }
        }
        const final = await inspectOpen(db);
        requireCondition(final.state?.status === "ready" && final.state.revision === targetRevision && final.state.schemaHash === expectedHash &&
            samePhysical(final.stores, options.schema), "MAGDB_TARGET_MISMATCH", "Database is not ready at the application's target revision.");
        const result = { name: options.name, schemaId: options.schema.schemaId, revision: targetRevision,
            schemaHash: expectedHash, nativeVersion: final.nativeVersion };
        notify(options, "ready", { revision: targetRevision, targetRevision });
        return result;
    } finally { db?.close(); }
}

/** Check pinned readiness before handing a managed connection to application work. */
export async function assertDatabaseReady(db, expected) {
    requireCondition(db.objectStoreNames.contains(metadataStore), "MAGDB_ADOPTION_REQUIRED", "Managed metadata is missing.");
    const transaction = db.transaction(metadataStore, "readonly");
    const done = transactionDone(transaction);
    const state = await requestResult(transaction.objectStore(metadataStore).get(stateKey));
    await done;
    requireCondition(state?.status === "ready" && state.schemaId === expected.schemaId && state.revision === expected.revision &&
        state.schemaHash === expected.schemaHash && db.version === expected.nativeVersion, "MAGDB_DATABASE_INCOMPATIBLE",
    "Database is migrating or has changed since this application opened it. Reload or explicitly retry initialization.");
}
