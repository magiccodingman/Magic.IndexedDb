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

export function createDb(dbStore)
{
    if (databases.find(d => d.name == dbStore.name) !== undefined)
        console.warn("Blazor.IndexedDB.Framework - Database already exists");

    const db = new Dexie(dbStore.name);

    const stores = {};
    for (let i = 0; i < dbStore.storeSchemas.length; i++)
    {
        // build string
        const schema = dbStore.storeSchemas[i];
        let def = "";
        if (schema.primaryKeyAuto)
            def = def + "++";
        if (schema.primaryKey !== null && schema.primaryKey !== "")
            def = def + schema.primaryKey;
        if (schema.uniqueIndexes !== undefined)
        {
            for (var j = 0; j < schema.uniqueIndexes.length; j++)
            {
                def = def + ",";
                var u = "&" + schema.uniqueIndexes[j];
                def = def + u;
            }
        }
        if (schema.indexes !== undefined)
        {
            for (var j = 0; j < schema.indexes.length; j++)
            {
                def = def + ",";
                var u = schema.indexes[j];
                def = def + u;
            }
        }
        stores[schema.name] = def;
    }
    db.version(dbStore.version).stores(stores);
    if (databases.find(d => d.name == dbStore.name) !== undefined)
    {
        databases.find(d => d.name == dbStore.name).db = db;
    }
    else
    {
        databases.push({
            name: dbStore.name,
            db: db
        });
    }
    db.open();
}

export async function deleteDb(dbName)
{
    const db = await getDb(dbName);
    const index = databases.findIndex(d => d.name == dbName);
    databases.splice(index, 1);
    db.delete();
}

export async function addItem(item)
{
    const table = await getTable(item.dbName, item.storeName);
    return await table.add(item.record);
}

export async function bulkAddItem(dbName, storeName, items)
{
    const table = await getTable(dbName, storeName);
    return await table.bulkAdd(items);
}

export async function countTable(dbName, storeName)
{
    const table = await getTable(dbName, storeName);
    return await table.count();
}

export async function putItem(item)
{
    const table = await getTable(item.dbName, item.storeName);
    return await table.put(item.record);
}

export async function updateItem(item)
{
    const table = await getTable(item.dbName, item.storeName);
    return await table.update(item.key, item.record);
}

export async function bulkUpdateItem(items)
{
    const table = await getTable(items[0].dbName, items[0].storeName);
    let updatedCount = 0;
    let errors = false;

    for (const item of items)
    {
        try
        {
            await table.update(item.key, item.record);
            updatedCount++;
        }
        catch (e)
        {
            console.error(e);
            errors = true;
        }
    }

    if (errors)
        throw new Error('Some items could not be updated');
    else
        return updatedCount;
}

export async function bulkDelete(dbName, storeName, keys)
{
    const table = await getTable(dbName, storeName);
    let deletedCount = 0;
    let errors = false;

    for (const key of keys)
    {
        try
        {
            await table.delete(key);
            deletedCount++;
        }
        catch (e)
        {
            console.error(e);
            errors = true;
        }
    }

    if (errors)
        throw new Error('Some items could not be deleted');
    else
        return deletedCount;
}

export async function deleteItem(item)
{
    const table = await getTable(item.dbName, item.storeName);
    await table.delete(item.key)
}

export async function clear(dbName, storeName)
{
    const table = await getTable(dbName, storeName);
    await table.clear();
}

export async function findItem(dbName, storeName, keyValue)
{
    const table = await getTable(dbName, storeName);
    return await table.get(keyValue);
}

export async function toArray(dbName, storeName)
{
    const table = await getTable(dbName, storeName);
    return await table.toArray();
}
export function getStorageEstimate()
{
    return navigator.storage.estimate();
}

export async function where(dbName, storeName, jsonQueries, jsonQueryAdditions, uniqueResults = true)
{
    const orConditionsArray = jsonQueries.map(query => JSON.parse(query));
    const QueryAdditions = JSON.parse(jsonQueryAdditions);

    const table = await getTable(dbName, storeName);

    let combinedQuery;

    function applyConditions(conditions)
    {
        let dexieQuery;
        for (let i = 0; i < conditions.length; i++)
        {
            const condition = conditions[i];
            const parsedValue = condition.isString ? condition.value : parseInt(condition.value);

            switch (condition.operation)
            {
                case 'GreaterThan':
                    if (!dexieQuery)
                    {
                        dexieQuery = table.where(condition.property).above(parsedValue);
                    } else
                    {
                        dexieQuery = dexieQuery.and(item => item[condition.property] > parsedValue);
                    }
                    break;
                case 'GreaterThanOrEqual':
                    if (!dexieQuery)
                    {
                        dexieQuery = table.where(condition.property).aboveOrEqual(parsedValue);
                    } else
                    {
                        dexieQuery = dexieQuery.and(item => item[condition.property] >= parsedValue);
                    }
                    break;
                case 'LessThan':
                    if (!dexieQuery)
                    {
                        dexieQuery = table.where(condition.property).below(parsedValue);
                    } else
                    {
                        dexieQuery = dexieQuery.and(item => item[condition.property] < parsedValue);
                    }
                    break;
                case 'LessThanOrEqual':
                    if (!dexieQuery)
                    {
                        dexieQuery = table.where(condition.property).belowOrEqual(parsedValue);
                    } else
                    {
                        dexieQuery = dexieQuery.and(item => item[condition.property] <= parsedValue);
                    }
                    break;
                case 'Equal':
                    if (!dexieQuery)
                    {
                        if (condition.isString)
                        {
                            if (condition.caseSensitive)
                            {
                                dexieQuery = table.where(condition.property).equals(condition.value);
                            } else
                            {
                                dexieQuery = table.where(condition.property).equalsIgnoreCase(condition.value);
                            }
                        } else
                        {
                            dexieQuery = table.where(condition.property).equals(parsedValue);
                        }
                    } else
                    {
                        if (condition.isString)
                        {
                            if (condition.caseSensitive)
                            {
                                dexieQuery = dexieQuery.and(item => item[condition.property] === condition.value);
                            } else
                            {
                                dexieQuery = dexieQuery.and(item => item[condition.property].toLowerCase() === condition.value.toLowerCase());
                            }
                        } else
                        {
                            dexieQuery = dexieQuery.and(item => item[condition.property] === parsedValue);
                        }
                    }
                    break;
                case 'NotEqual':
                    if (!dexieQuery)
                    {
                        if (condition.isString)
                        {
                            if (condition.caseSensitive)
                            {
                                dexieQuery = table.where(condition.property).notEqual(condition.value);
                            } else
                            {
                                dexieQuery = table.where(condition.property).notEqualIgnoreCase(condition.value);
                            }
                        } else
                        {
                            dexieQuery = table.where(condition.property).notEqual(parsedValue);
                        }
                    } else
                    {
                        if (condition.isString)
                        {
                            if (condition.caseSensitive)
                            {
                                dexieQuery = dexieQuery.and(item => item[condition.property] !== condition.value);
                            } else
                            {
                                dexieQuery = dexieQuery.and(item => item[condition.property].toLowerCase() !== condition.value.toLowerCase());
                            }
                        } else
                        {
                            dexieQuery = dexieQuery.and(item => item[condition.property] !== parsedValue);
                        }
                    }
                    break;
                case 'Contains':
                    if (!dexieQuery)
                    {
                        if (condition.caseSensitive)
                        {
                            dexieQuery = table.where(condition.property).filter(item => item[condition.property].includes(condition.value));
                        } else
                        {
                            dexieQuery = table.where(condition.property).filter(item => item[condition.property].toLowerCase().includes(condition.value.toLowerCase()));
                        }
                    } else
                    {
                        if (condition.caseSensitive)
                        {
                            dexieQuery = dexieQuery.and(item => item[condition.property].includes(condition.value));
                        } else
                        {
                            dexieQuery = dexieQuery.and(item => item[condition.property].toLowerCase().includes(condition.value.toLowerCase()));
                        }
                    }
                    break;
                case 'StartsWith':
                    if (!dexieQuery)
                    {
                        if (condition.caseSensitive)
                        {
                            dexieQuery = table.where(condition.property).startsWith(condition.value);
                        } else
                        {
                            dexieQuery = table.where(condition.property).startsWithIgnoreCase(condition.value);
                        }
                    } else
                    {
                        if (condition.caseSensitive)
                        {
                            dexieQuery = dexieQuery.and(item => item[condition.property].startsWith(condition.value));
                        } else
                        {
                            dexieQuery = dexieQuery.and(item => item[condition.property].toLowerCase().startsWith(condition.value.toLowerCase()));
                        }
                    }
                    break;
                case 'StringEquals':
                    if (!dexieQuery)
                    {
                        if (condition.caseSensitive)
                        {
                            dexieQuery = table.where(condition.property).equals(condition.value);
                        } else
                        {
                            dexieQuery = table.where(condition.property).equalsIgnoreCase(condition.value);
                        }
                    } else
                    {
                        if (condition.caseSensitive)
                        {
                            dexieQuery = dexieQuery.and(item => item[condition.property] === condition.value);
                        } else
                        {
                            dexieQuery = dexieQuery.and(item => item[condition.property].toLowerCase() === condition.value.toLowerCase());
                        }
                    }
                    break;
                case 'In':
                    if (!dexieQuery)
                    {
                        dexieQuery = table.where(condition.property).anyOf(condition.value);
                    }
                    else
                    {
                        dexieQuery = dexieQuery.and(item => condition.value.includes(item[condition.property]));
                    }
                    break;
                default:
                    throw new Error('Unsupported operation: ' + condition.operation);
            }
        }

        return dexieQuery;
    }

    function applyArrayQueryAdditions(results, queryAdditions)
    {
        if (queryAdditions != null)
        {
            for (let i = 0; i < queryAdditions.length; i++)
            {
                const queryAddition = queryAdditions[i];

                switch (queryAddition.Name)
                {
                    case 'skip':
                        results = results.slice(queryAddition.IntValue);
                        break;
                    case 'take':
                        results = results.slice(0, queryAddition.IntValue);
                        break;
                    case 'takeLast':
                        results = results.slice(-queryAddition.IntValue).reverse();
                        break;
                    case 'orderBy':
                        results = results.sort((a, b) => a[queryAddition.StringValue] - b[queryAddition.StringValue]);
                        break;
                    case 'orderByDescending':
                        results = results.sort((a, b) => b[queryAddition.StringValue] - a[queryAddition.StringValue]);
                        break;
                    default:
                        console.error('Unsupported query addition for array:', queryAddition.Name);
                        break;
                }
            }
        }
        return results;
    }

    async function combineQueries()
    {
        const allQueries = [];

        for (const conditions of orConditionsArray)
        {
            const andQuery = applyConditions(conditions[0]);
            if (andQuery)
            {
                allQueries.push(andQuery);
            }
        }

        if (allQueries.length > 0)
        {
            // Use Dexie.Promise.all to resolve all toArray promises
            const allResults = await Dexie.Promise.all(allQueries.map(query => query.toArray()));

            // Combine all the results into one array
            let combinedResults = [].concat(...allResults);

            // Apply query additions to the combined results
            combinedResults = applyArrayQueryAdditions(combinedResults, QueryAdditions);

            if (allQueries.length > 1 && uniqueResults)
            {
                // Make sure the objects in the array are unique
                const uniqueObjects = new Set(combinedResults.map(obj => JSON.stringify(obj)));
                return Array.from(uniqueObjects).map(str => JSON.parse(str));
            }
            else
            {
                return combinedResults;
            }
        }
        else
        {
            return [];
        }
    }

    if (orConditionsArray.length > 0)
        return await combineQueries();
    else
        return [];
}

async function getDb(dbName)
{
    if (databases.find(d => d.name == dbName) === undefined)
    {
        console.warn("Blazor.IndexedDB.Framework - Database doesn't exist");
        var db1 = new Dexie(dbName);
        await db1.open();
        if (databases.find(d => d.name == dbName) !== undefined)
        {
            databases.find(d => d.name == dbName).db = db1;
        } else
        {
            databases.push({
                name: dbName,
                db: db1
            });
        }
        return db1;
    }
    else
    {
        return databases.find(d => d.name == dbName).db;
    }
}

async function getTable(dbName, storeName)
{
    let db = await getDb(dbName);
    let table = db.table(storeName);
    return table;
}

function createFilterObject(filters)
{
    const jsonFilter = {};
    for (const filter in filters)
    {
        if (filters.hasOwnProperty(filter))
            jsonFilter[filters[filter].indexName] = filters[filter].filterValue;
    }
    return jsonFilter;
}

function getAll(dotnetReference, transaction, dbName, storeName)
{
    return new Promise((resolve, reject) =>
    {
        getTable(dbName, storeName).then(table =>
        {
            table.toArray().then(items =>
            {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'getAll succeeded');
                resolve(items);
            }).catch(e =>
            {
                console.error(e);
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'getAll failed');
                reject(e);
            });
        });
    });
}

export function encryptString(data, key)
{
    // Convert the data to an ArrayBuffer
    let dataBuffer = new TextEncoder().encode(data).buffer;

    // Generate a random initialization vector
    let iv = crypto.getRandomValues(new Uint8Array(16));

    // Convert the key to an ArrayBuffer
    let keyBuffer = new TextEncoder().encode(key).buffer;

    // Create a CryptoKey object from the key buffer
    return crypto.subtle.importKey('raw', keyBuffer, { name: 'AES-CBC' }, false, ['encrypt'])
        .then(key =>
        {
            // Encrypt the data with AES-CBC encryption
            return crypto.subtle.encrypt({ name: 'AES-CBC', iv }, key, dataBuffer);
        })
        .then(encryptedDataBuffer =>
        {
            // Concatenate the initialization vector and encrypted data
            let encryptedData = new Uint8Array(encryptedDataBuffer);
            let encryptedDataWithIV = new Uint8Array(encryptedData.byteLength + iv.byteLength);
            encryptedDataWithIV.set(iv);
            encryptedDataWithIV.set(encryptedData, iv.byteLength);

            // Convert the encrypted data to a base64 string and return it
            return btoa(String.fromCharCode.apply(null, encryptedDataWithIV));
        });
}

export function decryptString(encryptedData, key)
{
    // Convert the base64 string to a Uint8Array
    let encryptedDataWithIV = new Uint8Array(atob(encryptedData).split('').map(c => c.charCodeAt(0)));
    let iv = encryptedDataWithIV.slice(0, 16);
    let data = encryptedDataWithIV.slice(16);

    // Convert the key to an ArrayBuffer
    let keyBuffer = new TextEncoder().encode(key).buffer;

    // Create a CryptoKey object from the key buffer
    return crypto.subtle.importKey('raw', keyBuffer, { name: 'AES-CBC' }, false, ['decrypt'])
        .then(key =>
        {
            // Decrypt the data with AES-CBC decryption
            return crypto.subtle.decrypt({ name: 'AES-CBC', iv }, key, data);
        })
        .then(decryptedDataBuffer =>
        {
            // Convert the decrypted data to a string and return it
            return new TextDecoder().decode(decryptedDataBuffer);
        });
}