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
    let db = await getDb(dbName);
    let index = databases.findIndex(d => d.name == dbName);
    databases.splice(index, 1);
    db.delete();
}

export function addItem(dotnetReference, transaction, item)
{
    getTable(item.dbName, item.storeName).then(table =>
    {
        table.add(item.record).then(_ =>
        {
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'Item added');
        }).catch(e =>
        {
            console.error(e);
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Item could not be added');
        });
    });
}

export function bulkAddItem(dotnetReference, transaction, dbName, storeName, items)
{
    getTable(dbName, storeName).then(table =>
    {
        table.bulkAdd(items).then(_ =>
        {
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'Item(s) bulk added');
        }).catch(e =>
        {
            console.error(e);
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Item(s) could not be bulk added');
        });
    });
}

export function countTable(dotnetReference, transaction, dbName, storeName)
{
    var promise = new Promise((resolve, reject) =>
    {
        getTable(dbName, storeName).then(table =>
        {
            table.count().then(count =>
            {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'Table count: ' + count);
                resolve(count);
            }).catch(e =>
            {
                console.error(e);
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Could not get table count');
                reject(e);
            });
        });
    });
    return promise.then(count =>
    {
        return count;
    });
}

export function putItem(dotnetReference, transaction, item)
{
    getTable(item.dbName, item.storeName).then(table =>
    {
        table.put(item.record).then(_ =>
        {
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'Item put successful');
        }).catch(e =>
        {
            console.error(e);
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Item put failed');
        });
    });
}

export function updateItem(dotnetReference, transaction, item)
{
    getTable(item.dbName, item.storeName).then(table =>
    {
        table.update(item.key, item.record).then(_ =>
        {
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'Item updated');
        }).catch(e =>
        {
            console.error(e);
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Item could not be updated');
        });
    });
}

export function bulkUpdateItem(dotnetReference, transaction, items)
{
    return new Promise(async (resolve, reject) =>
    {
        try
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
                } catch (e)
                {
                    console.error(e);
                    errors = true;
                }
            }

            if (errors)
            {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Some items could not be updated');
                reject(new Error('Some items could not be updated'));
            } else
            {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, `${updatedCount} items updated`);
                resolve(updatedCount);
            }
        } catch (e)
        {
            console.error(e);
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Items could not be updated');
            reject(e);
        }
    });
}

export function bulkDelete(dotnetReference, transaction, dbName, storeName, keys)
{
    return new Promise(async (resolve, reject) =>
    {
        try
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
                } catch (e)
                {
                    console.error(e);
                    errors = true;
                }
            }

            if (errors)
            {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Some items could not be deleted');
                reject(new Error('Some items could not be deleted'));
            } else
            {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, `${deletedCount} items deleted`);
                resolve(deletedCount);
            }
        } catch (e)
        {
            console.error(e);
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Items could not be deleted');
            reject(e);
        }
    });
}

export function deleteItem(dotnetReference, transaction, item)
{
    getTable(item.dbName, item.storeName).then(table =>
    {
        table.delete(item.key).then(_ =>
        {
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'Item deleted');
        }).catch(e =>
        {
            console.error(e);
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Item could not be deleted');
        });
    });
}

export function clear(dotnetReference, transaction, dbName, storeName)
{
    getTable(dbName, storeName).then(table =>
    {
        table.clear().then(_ =>
        {
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'Table cleared');
        }).catch(e =>
        {
            console.error(e);
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Table could not be cleared');
        });
    });
}

export function findItem(dotnetReference, transaction, item)
{
    var promise = new Promise((resolve, reject) =>
    {
        getTable(item.dbName, item.storeName).then(table =>
        {
            table.get(item.key).then(i =>
            {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'Found item');
                resolve(i);
            }).catch(e =>
            {
                console.error(e);
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Could not find item');
                reject(e);
            });
        });
    });
    return promise;
}

export function findItemv2(dotnetReference, transaction, dbName, storeName, keyValue)
{
    var promise = new Promise((resolve, reject) =>
    {
        getTable(dbName, storeName).then(table =>
        {
            table.get(keyValue).then(i =>
            {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'Found item');
                resolve(i);
            }).catch(e =>
            {
                console.error(e);
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'Could not find item');
                reject(e);
            });
        });
    });
    return promise;
}

export function toArray(dotnetReference, transaction, dbName, storeName)
{
    return new Promise((resolve, reject) =>
    {
        getTable(dbName, storeName).then(table =>
        {
            table.toArray(items =>
            {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'toArray succeeded');
                resolve(items);
            }).catch(e =>
            {
                console.error(e);
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'toArray failed');
                reject(e);
            });
        });
    });
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

export function getTable(dbName, storeName)
{
    return new Promise((resolve, reject) =>
    {
        getDb(dbName).then(db =>
        {
            var table = db.table(storeName);
            resolve(table);
        });
    });
}

export function createFilterObject(filters)
{
    const jsonFilter = {};
    for (const filter in filters)
    {
        if (filters.hasOwnProperty(filter))
            jsonFilter[filters[filter].indexName] = filters[filter].filterValue;
    }
    return jsonFilter;
}

export function where(dotnetReference, transaction, dbName, storeName, filters)
{
    const filterObject = this.createFilterObject(filters);
    return new Promise((resolve, reject) =>
    {
        getTable(dbName, storeName).then(table =>
        {
            table.where(filterObject).toArray(items =>
            {
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'where succeeded');
                resolve(items);
            })
        }).catch(e =>
        {
            console.error(e);
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'where failed');
            reject(e);
        });
    });
}

export function wherev2(dotnetReference, transaction, dbName, storeName, jsonQueries, jsonQueryAdditions, uniqueResults = true)
{
    const orConditionsArray = jsonQueries.map(query => JSON.parse(query));
    const QueryAdditions = JSON.parse(jsonQueryAdditions);

    return new Promise((resolve, reject) =>
    {
        getTable(dbName, storeName).then(table =>
        {
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

                        default:
                            console.error('Unsupported operation:', condition.operation);
                            reject(new Error('Unsupported operation: ' + condition.operation));
                            return;
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

                    if (uniqueResults)
                    {
                        // Make sure the objects in the array are unique
                        let uniqueResults = combinedResults.filter((result, index, self) =>
                            index === self.findIndex((r) => (
                                r.id === result.id && r.Name === result.Name && r.Age === result.Age
                            ))
                        );
                        dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'where succeeded');
                        resolve(uniqueResults);
                    }
                    else
                    {
                        dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'where succeeded');
                        resolve(combinedResults);
                    }
                } else
                {
                    dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'where succeeded');
                    resolve([]);
                }
            }

            (async () =>
            { // Add an async IIFE to handle the promise
                if (orConditionsArray.length > 0)
                {
                    await combineQueries(); // Add 'await' here
                } else
                {
                    dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, false, 'where succeeded');
                    resolve([]);
                }
            })().catch(e =>
            { // Add error handling for the async IIFE
                console.error(e);
                dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'where failed');
                reject(e);
            });
        }).catch(e =>
        {
            console.error(e);
            dotnetReference.invokeMethodAsync('BlazorDBCallback', transaction, true, 'where failed');
            reject(e);
        });
    });
}

export function getAll(dotnetReference, transaction, dbName, storeName)
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

export async function getStorageEstimate()
{
    if (navigator.storage && navigator.storage.estimate)
    {
        const estimate = await navigator.storage.estimate();
        return {
            quota: estimate.quota,
            usage: estimate.usage
        };
    } else
    {
        return {
            quota: -1,
            usage: -1
        };
    }
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

export function DynamicJsCaller(functionName, ...args)
{
    // Get the function referenced by functionName
    let func = window[functionName];

    // Check if the function exists
    if (typeof func === "function")
    {
        // Call the function and get the result
        let result = func(...args);

        // Check if the result is an object and has the required properties
        if (typeof result === "object" && result !== null && 'Data' in result && 'Success' in result && 'Message' in result)
        {
            if (result.Success === false && (!result.Message || result.Message.trim() === ''))
            {
                // If Success is false and there is no Message, create an error object
                return {
                    Data: null,
                    Success: false,
                    Message: "There was an error but no message associated with the error"
                };
            }
            else
            {
                // If everything is OK, return the result as is
                return result;
            }
        }
        else
        {
            // If the result is not in the correct format, create an error object
            return {
                Data: null,
                Success: false,
                Message: "The function called did not return data in the correct format"
            };
        }
    }
    else
    {
        // If the function does not exist, create an error object
        return {
            Data: null,
            Success: false,
            Message: "The function " + functionName + " does not exist"
        };
    }
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