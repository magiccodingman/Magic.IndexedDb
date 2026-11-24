import { importEncryptionKey } from "./encryptionEnvelope.js";
import { verifyEncryptionPolicy, encryptionMetadataStore } from "./encryptionPolicy.js";
import { openRecord, sealRecord, encryptedRecordMember } from "./encryptionRecord.js";
import { EncryptionSession } from "./encryptionSession.js";
import { magicQueryYield } from "../magicLinqToIndexedDb.js";

export const protectedDatabasePrefix = "__magic_protected__:";
const locked = "MAGIC_ENCRYPTION_LOCKED";

function validateMember(name) {
    if (typeof name !== "string" || !name || name.includes(".") ||
        [encryptedRecordMember, "__proto__", "constructor", "prototype"].includes(name)) {
        throw new Error("Invalid or reserved Magic encryption member.");
    }
}

function normalizeMembers(names) {
    for (const name of names) validateMember(name);
    return new Set([...names].sort());
}

export function createProtectedStorage(db, account, schemas) {
    const policies = new Map();
    const schemaFingerprint = [];
    for (const schema of [...schemas].sort((left, right) =>
        left.tableName < right.tableName ? -1 : Number(left.tableName !== right.tableName))) {
        if (!schema.tableName || schema.tableName === encryptionMetadataStore ||
            schemaFingerprint.some(entry => entry[0] === schema.tableName)) {
            throw new Error("Invalid or duplicate store in Magic encryption policy.");
        }
        const keyPaths = schema.columnNamesInCompoundKey;
        const indexes = [...schema.indexes, ...schema.uniqueIndexes, ...schema.columnNamesInCompoundIndex.flat()];
        const fields = normalizeMembers(schema.encryptedFields ?? []);
        const exposed = schema.exposedFields == null ? null : normalizeMembers(schema.exposedFields);
        schemaFingerprint.push([schema.tableName, keyPaths, Boolean(schema.primaryKeyAuto),
            [...schema.indexes].sort(), [...schema.uniqueIndexes].sort(), schema.columnNamesInCompoundIndex.map(index => JSON.stringify(index)).sort(),
            [...fields], exposed && [...exposed]]);
        if (!fields.size && exposed === null) continue;

        if (schema.primaryKeyAuto || !keyPaths.length) {
            throw new Error("Protected Magic stores require explicit primary keys.");
        }
        for (const name of [...keyPaths, ...indexes]) {
            validateMember(name);
            if (fields.has(name) || (exposed && !exposed.has(name))) {
                throw new Error("Protected members cannot be primary keys or indexes.");
            }
        }
        if (exposed && [...fields].some(name => exposed.has(name))) {
            throw new Error("An encrypted member cannot be exposed by the record policy.");
        }
        policies.set(schema.tableName, { fields, exposed, keyPaths });
    }

    if (!policies.size) throw new Error("Magic encryption configuration contains no protected stores.");
    const session = new EncryptionSession();
    const policyFingerprint = JSON.stringify(schemaFingerprint);

    return {
        forTable: name => policies.has(name) ? new ProtectedTable(db, account, name, policies.get(name), session) : undefined,
        unlock: (keyId, input, initialize) => session.ensureUnlocked(async signal => {
            await db.open();
            signal.throwIfAborted();
            const key = input instanceof Uint8Array ? await importEncryptionKey(input) : input;
            await verifyEncryptionPolicy(db, { account, policyFingerprint, keyId, key, initialize, signal });
            return { key, keyId };
        }),
        isUnlocked: () => session.isUnlocked,
        lock: () => session.lock(),
        dispose: () => session.dispose()
    };
}

class ProtectedTable {
    constructor(db, account, name, policy, session) {
        this.db = db;
        this.table = db.table(name);
        this.account = account;
        this.policy = policy;
        this.session = session;
    }

    context(primaryKey) {
        return { database: this.db.name, account: this.account, store: this.table.name, primaryKey };
    }

    key(record) {
        const parts = this.policy.keyPaths.map(name => record[name]);
        return parts.length === 1 ? parts[0] : parts;
    }

    isPublic(name) {
        if (typeof name !== "string" || name === encryptedRecordMember) return false;
        const root = name.split(".")[0];
        return this.policy.exposed !== null ? this.policy.exposed.has(root) : !this.policy.fields.has(root);
    }

    requirePublic(names) {
        if (names.some(name => !this.isPublic(name))) {
            throw new Error("MAGIC_ENCRYPTION_QUERY_UNSUPPORTED: The operation references a protected member.");
        }
    }

    validateQuery(node, additions) {
        if (node?.condition?.property && node.condition.property !== "__constant") {
            this.requirePublic([node.condition.property]);
        }
        for (const child of node?.children ?? []) this.validateQuery(child, []);
        this.requirePublic((additions ?? []).filter(addition => addition.property).map(addition => addition.property));
    }

    assertLease(lease) {
        try {
            lease();
        } catch {
            throw new Error(`${locked}: The protected query was locked during execution.`);
        }
    }

    async access(operation) {
        try {
            return await this.session.useKey(operation);
        } catch (error) {
            if (error.message.startsWith("Magic encryption session is locked") ||
                error.message.startsWith("Magic encryption session was locked")) {
                throw new Error(`${locked}: Unlock the database before accessing a protected record.`);
            }
            throw error;
        }
    }

    mutate(operation) {
        return this.access((_, assertCurrent) => this.db.transaction("rw", this.table, () => {
            assertCurrent();
            return operation(this.table);
        }));
    }

    write(records, operation) {
        return this.access(async ({ key, keyId }, assertCurrent) => {
            const sealed = await Promise.all(records.map(record =>
                sealRecord(record, this.policy, key, keyId, this.context(this.key(record)))));
            assertCurrent();
            return this.db.transaction("rw", this.table, () => {
                assertCurrent();
                return operation(this.table, sealed);
            });
        });
    }

    async patch(primaryKey, changes) {
        const names = Object.keys(changes);
        for (const name of names) validateMember(name);
        this.requirePublic(names);
        if (names.some(name => this.policy.keyPaths.includes(name))) {
            throw new Error("A Magic public patch cannot change the primary key.");
        }
        await this.db.open();
        return this.table.update(primaryKey, changes);
    }

    async *query(predicate, additions, forceCursor, projection = null) {
        if (projection !== null) this.requirePublic(projection);
        // Even an empty full query requires an unlocked session.
        const lease = projection === null ? await this.access((_, assertCurrent) => assertCurrent) : null;
        this.validateQuery(predicate, additions);
        await this.db.open();
        for await (const row of magicQueryYield(this.db, this.table, predicate, additions, forceCursor)) {
            if (projection !== null) {
                yield Object.fromEntries(projection.filter(name => Object.hasOwn(row, name)).map(name => [name, row[name]]));
                continue;
            }
            this.assertLease(lease);
            const opened = await this.access(({ key, keyId }) =>
                openRecord(row, this.policy, key, keyId, this.context(this.key(row))));
            this.assertLease(lease);
            yield opened;
        }
        if (lease) this.assertLease(lease);
    }

    read(primaryKey) {
        return this.access(async ({ key, keyId }) => {
            const row = await this.table.get(primaryKey);
            return row === undefined ? undefined : openRecord(row, this.policy, key, keyId, this.context(primaryKey));
        });
    }
}
