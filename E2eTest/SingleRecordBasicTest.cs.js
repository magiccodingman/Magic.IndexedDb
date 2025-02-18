window.getAll = (database, store) =>
{
    return new Promise((resolve, reject) =>
    {
        const request = window.indexedDB.open(database);
        request.onerror = () => reject("Failed to open database.");
        request.onblocked = () => reject("Failed to open database.");
        request.onsuccess = () =>
        {
            const db = request.result;
            const getRequest = db.transaction(store).objectStore(store).getAll();
            getRequest.onerror = () => reject("Failed to get records.");
            getRequest.onsuccess = () =>
            {
                resolve(JSON.stringify(getRequest.result));
            }
        };
    });
};

window.deleteTestNewDatabase = () =>
{
    return new Promise((resolve, reject) =>
    {
        const deleteRequest = window.indexedDB.deleteDatabase("SingleRecordBasic.Delete");
        deleteRequest.onerror = () => reject("Failed to delete database.");
        deleteRequest.onblocked = () => reject("Failed to delete database.");
        deleteRequest.onsuccess = () =>
        {
            const openRequest = window.indexedDB.open("SingleRecordBasic.Delete");
            openRequest.onerror = () => reject("Failed to open database.");
            openRequest.onblocked = () => reject("Failed to open database.");
            openRequest.onupgradeneeded = () =>
            {
                const database = openRequest.result;
                database.createObjectStore("Records", {
                    keyPath: "id"
                });
            }
            openRequest.onsuccess = () =>
            {
                const database = openRequest.result;
                const transaction = database.transaction("Records", "readwrite");
                transaction.oncomplete = () =>
                {
                    // I don't know why database.close() is needed here. 
                    // But without this the later deletion will be blocked.
                    database.close();
                    resolve();
                }

                const store = transaction.objectStore("Records");
                const addRequest = store.add({
                    id: 123,
                    value: "hi"
                });
                addRequest.onerror = () => reject("Failed to add the record.");
                addRequest.onsuccess = () =>
                {
                    const addRequest2 = store.add({
                        id: 1234,
                        value: "hello"
                    });
                    addRequest2.onerror = () => reject("Failed to add the record.");
                    addRequest2.onsuccess = () => transaction.commit();
                }
            }
        };
    });
};