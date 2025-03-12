"use strict";

/// <reference types="./dexie/dexie.d.ts" />
import Dexie from "./dexie/dexie.js";

/**
 * @typedef {Object} DatabasesItem
 * @property {string} name
 * @property {Dexie} db
 */

/**
 * @type {Array.<DatabasesItem>}
 */
let databases = [];

export function createDb(dbStore) {
    console.log("Debug: Received dbStore in createDb", dbStore);
    if (databases.find(d => d.name == dbStore.name) !== undefined)
        console.warn("Blazor.IndexedDB.Framework - Database already exists");

    const db = new Dexie(dbStore.name);

    const stores = {};
    for (let i = 0; i < dbStore.storeSchemas.length; i++) {
        // build string
        const schema = dbStore.storeSchemas[i];
        let def = "";
        if (schema.primaryKeyAuto)
            def = def + "++";
        if (schema.primaryKey !== null && schema.primaryKey !== "")
            def = def + schema.primaryKey;
        if (schema.uniqueIndexes !== undefined) {
            for (var j = 0; j < schema.uniqueIndexes.length; j++) {
                def = def + ",";
                var u = "&" + schema.uniqueIndexes[j];
                def = def + u;
            }
        }
        if (schema.indexes !== undefined) {
            for (var j = 0; j < schema.indexes.length; j++) {
                def = def + ",";
                var u = schema.indexes[j];
                def = def + u;
            }
        }
        stores[schema.name] = def;
    }
    db.version(dbStore.version).stores(stores);
    if (databases.find(d => d.name == dbStore.name) !== undefined) {
        databases.find(d => d.name == dbStore.name).db = db;
    }
    else {
        databases.push({
            name: dbStore.name,
            db: db
        });
    }
    db.open();
}

export function closeAll() {
    const dbs = databases;
    databases = [];
    for (db of dbs)
        db.db.close();
}

export async function deleteDb(dbName) {
    const db = await getDb(dbName);
    const index = databases.findIndex(d => d.name == dbName);
    databases.splice(index, 1);
    db.delete();
}

export async function addItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    return await table.add(item.record);
}

export async function bulkAddItem(dbName, storeName, items) {
    const table = await getTable(dbName, storeName);
    return await table.bulkAdd(items);
}

export async function countTable(dbName, storeName) {
    const table = await getTable(dbName, storeName);
    return await table.count();
}

export async function putItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    return await table.put(item.record);
}

export async function updateItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    return await table.update(item.key, item.record);
}

export async function bulkUpdateItem(items) {
    const table = await getTable(items[0].dbName, items[0].storeName);
    let updatedCount = 0;
    let errors = false;

    for (const item of items) {
        try {
            await table.update(item.key, item.record);
            updatedCount++;
        }
        catch (e) {
            console.error(e);
            errors = true;
        }
    }

    if (errors)
        throw new Error('Some items could not be updated');
    else
        return updatedCount;
}

export async function bulkDelete(dbName, storeName, keys) {
    const table = await getTable(dbName, storeName);
    let deletedCount = 0;
    let errors = false;

    for (const key of keys) {
        try {
            await table.delete(key);
            deletedCount++;
        }
        catch (e) {
            console.error(e);
            errors = true;
        }
    }

    if (errors)
        throw new Error('Some items could not be deleted');
    else
        return deletedCount;
}

export async function deleteItem(item) {
    const table = await getTable(item.dbName, item.storeName);
    await table.delete(item.key)
}

export async function clear(dbName, storeName) {
    const table = await getTable(dbName, storeName);
    await table.clear();
}

export async function findItem(dbName, storeName, keyValue) {
    const table = await getTable(dbName, storeName);
    return await table.get(keyValue);
}

export async function toArray(dbName, storeName) {
    const table = await getTable(dbName, storeName);
    return await table.toArray();
}
export function getStorageEstimate() {
    return navigator.storage.estimate();
}

export async function where(dbName, storeName, jsonQueries, jsonQueryAdditions, uniqueResults = true) {
    const orConditionsArray = jsonQueries.map(query => JSON.parse(query));
    console.log('or condition array')
    console.log(jsonQueries)
    const QueryAdditions = JSON.parse(jsonQueryAdditions);
    console.log('jsonQueryAdditions')
    console.log(jsonQueryAdditions)

    let db = await getDb(dbName);
    let table = db.table(storeName);

    let results = [];

    function applyConditionsToRecord(record, conditions) {
        for (const condition of conditions) {
            const parsedValue = condition.isString ? condition.value : parseInt(condition.value);
            switch (condition.operation) {
                case 'GreaterThan':
                    if (!(record[condition.property] > parsedValue)) return false;
                    break;
                case 'GreaterThanOrEqual':
                    if (!(record[condition.property] >= parsedValue)) return false;
                    break;
                case 'LessThan':
                    if (!(record[condition.property] < parsedValue)) return false;
                    break;
                case 'LessThanOrEqual':
                    if (!(record[condition.property] <= parsedValue)) return false;
                    break;
                case 'Equal':
                    if (record[condition.property] === null && condition.value === null) {
                        return true;
                    }
                    if (condition.isString) {
                        if (condition.caseSensitive) {
                            if (record[condition.property] !== condition.value) return false;
                        } else {
                            if (record[condition.property]?.toLowerCase() !== condition.value?.toLowerCase()) return false;
                        }
                    } else {
                        if (record[condition.property] !== parsedValue) return false;
                    }
                    break;
                case 'NotEqual':
                    if (record[condition.property] === null && condition.value === null) {
                        return false;
                    }
                    if (condition.isString) {
                        if (condition.caseSensitive) {
                            if (record[condition.property] === condition.value) return false;
                        } else {
                            if (record[condition.property]?.toLowerCase() === condition.value?.toLowerCase()) return false;
                        }
                    } else {
                        if (record[condition.property] === parsedValue) return false;
                    }
                    break;
                case 'Contains':
                    if (!record[condition.property]?.toLowerCase().includes(condition.value.toLowerCase())) return false;
                    break;
                case 'StartsWith':
                    if (!record[condition.property]?.toLowerCase().startsWith(condition.value.toLowerCase())) return false;
                    break;
                case 'In':
                    if (!condition.value.includes(record[condition.property])) return false;
                    break;
                default:
                    throw new Error('Unsupported operation: ' + condition.operation);
            }
        }
        return true;
    }

    async function processWithCursor(conditions) {
        return new Promise((resolve, reject) => {
            let primaryKey = table.schema.primKey.name;
            let cursorResults = [];
            let request = table.orderBy(primaryKey).each((record) => {
                if (applyConditionsToRecord(record, conditions)) {
                    cursorResults.push(record);
                }
            });
            request.then(() => resolve(cursorResults)).catch(reject);
        });
    }

    async function processIndexedQuery(conditions) {
        let localResults = [];
        for (const condition of conditions) {
            if (table.schema.idxByName[condition.property]) {
                let indexQuery = null;
                switch (condition.operation) {
                    case 'GreaterThan':
                        indexQuery = table.where(condition.property).above(condition.value);
                        break;
                    case 'GreaterThanOrEqual':
                        indexQuery = table.where(condition.property).aboveOrEqual(condition.value);
                        break;
                    case 'LessThan':
                        indexQuery = table.where(condition.property).below(condition.value);
                        break;
                    case 'LessThanOrEqual':
                        indexQuery = table.where(condition.property).belowOrEqual(condition.value);
                        break;
                    case 'Equal':
                        indexQuery = table.where(condition.property).equals(condition.value);
                        break;
                    case 'In':
                        indexQuery = table.where(condition.property).anyOf(condition.value);
                        break;
                }
                if (indexQuery) {
                    let indexedResults = await indexQuery.toArray();
                    localResults.push(...indexedResults.filter(record => applyConditionsToRecord(record, conditions)));
                }
            }
        }
        if (localResults.length === 0) {
            localResults = await processWithCursor(conditions);
        }
        results.push(...localResults);
    }

    function applyArrayQueryAdditions(results, queryAdditions) {
        if (queryAdditions) {
            if (queryAdditions.some(q => q.Name === 'orderBy')) {
                const orderBy = queryAdditions.find(q => q.Name === 'orderBy');
                results.sort((a, b) => a[orderBy.StringValue] - b[orderBy.StringValue]);
            }
            if (queryAdditions.some(q => q.Name === 'orderByDescending')) {
                const orderByDescending = queryAdditions.find(q => q.Name === 'orderByDescending');
                results.sort((a, b) => b[orderByDescending.StringValue] - a[orderByDescending.StringValue]);
            }
            if (queryAdditions.some(q => q.Name === 'skip')) {
                results = results.slice(queryAdditions.find(q => q.Name === 'skip').IntValue);
            }
            if (queryAdditions.some(q => q.Name === 'take')) {
                results = results.slice(0, queryAdditions.find(q => q.Name === 'take').IntValue);
            }
            if (queryAdditions.some(q => q.Name === 'takeLast')) {
                const takeLastValue = queryAdditions.find(q => q.Name === 'takeLast').IntValue;
                results = results.slice(-takeLastValue).reverse();
            }
        }
        return results;
    }

    async function combineQueries() {
        for (const conditions of orConditionsArray) {
            await processIndexedQuery(conditions[0]);
        }
        results = applyArrayQueryAdditions(results, QueryAdditions);
        if (uniqueResults) {
            const uniqueObjects = new Set(results.map(obj => JSON.stringify(obj)));
            results = Array.from(uniqueObjects).map(str => JSON.parse(str));
        }
        return results;
    }

    if (orConditionsArray.length > 0) {
        return await combineQueries();
    } else {
        return [];
    }
}






async function getDb(dbName) {
    if (databases.find(d => d.name == dbName) === undefined) {
        console.warn("Blazor.IndexedDB.Framework - Database doesn't exist");
        var db1 = new Dexie(dbName);
        await db1.open();
        if (databases.find(d => d.name == dbName) !== undefined) {
            databases.find(d => d.name == dbName).db = db1;
        } else {
            databases.push({
                name: dbName,
                db: db1
            });
        }
        return db1;
    }
    else {
        return databases.find(d => d.name == dbName).db;
    }
}

async function getTable(dbName, storeName) {
    let db = await getDb(dbName);
    let table = db.table(storeName);
    return table;
}

function createFilterObject(filters) {
    const jsonFilter = {};
    for (const filter in filters) {
        if (filters.hasOwnProperty(filter))
            jsonFilter[filters[filter].indexName] = filters[filter].filterValue;
    }
    return jsonFilter;
}

function getAll(dotnetReference, transaction, dbName, storeName) {
    return new Promise((resolve, reject) => {
        getTable(dbName, storeName).then(table => {
            table.toArray().then(items => {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'getAll succeeded');
                resolve(items);
            }).catch(e => {
                console.error(e);
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'getAll failed');
                reject(e);
            });
        });
    });
}

export function encryptString(data, key) {
    // Convert the data to an ArrayBuffer
    let dataBuffer = new TextEncoder().encode(data).buffer;

    // Generate a random initialization vector
    let iv = crypto.getRandomValues(new Uint8Array(16));

    // Convert the key to an ArrayBuffer
    let keyBuffer = new TextEncoder().encode(key).buffer;

    // Create a CryptoKey object from the key buffer
    return crypto.subtle.importKey('raw', keyBuffer, { name: 'AES-CBC' }, false, ['encrypt'])
        .then(key => {
            // Encrypt the data with AES-CBC encryption
            return crypto.subtle.encrypt({ name: 'AES-CBC', iv }, key, dataBuffer);
        })
        .then(encryptedDataBuffer => {
            // Concatenate the initialization vector and encrypted data
            let encryptedData = new Uint8Array(encryptedDataBuffer);
            let encryptedDataWithIV = new Uint8Array(encryptedData.byteLength + iv.byteLength);
            encryptedDataWithIV.set(iv);
            encryptedDataWithIV.set(encryptedData, iv.byteLength);

            // Convert the encrypted data to a base64 string and return it
            return btoa(String.fromCharCode.apply(null, encryptedDataWithIV));
        });
}

export function decryptString(encryptedData, key) {
    // Convert the base64 string to a Uint8Array
    let encryptedDataWithIV = new Uint8Array(atob(encryptedData).split('').map(c => c.charCodeAt(0)));
    let iv = encryptedDataWithIV.slice(0, 16);
    let data = encryptedDataWithIV.slice(16);

    // Convert the key to an ArrayBuffer
    let keyBuffer = new TextEncoder().encode(key).buffer;

    // Create a CryptoKey object from the key buffer
    return crypto.subtle.importKey('raw', keyBuffer, { name: 'AES-CBC' }, false, ['decrypt'])
        .then(key => {
            // Decrypt the data with AES-CBC decryption
            return crypto.subtle.decrypt({ name: 'AES-CBC', iv }, key, data);
        })
        .then(decryptedDataBuffer => {
            // Convert the decrypted data to a string and return it
            return new TextDecoder().decode(decryptedDataBuffer);
        });
}