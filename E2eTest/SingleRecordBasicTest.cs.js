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
    })
};