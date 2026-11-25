using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public sealed class SingleRecordBasicTestAudit : TestBase<OpenTestPage>
{
    [TestMethod]
    public async Task Writers_PreserveIdWhenItIsNotThePrimaryKey()
    {
        var page = await NewPageAsync();
        await page.EvaluateAsync("""
            async () => {
                const api = await import('/_content/Magic.IndexedDb/magicDB.js');
                const dbName = 'write-contract-' + crypto.randomUUID();
                const storeName = 'rows';
                await api.createDb({ name: dbName, version: 1, storeSchemas: [{
                    tableName: storeName, columnNamesInCompoundKey: ['key'],
                    primaryKeyAuto: false, indexes: [], uniqueIndexes: [], columnNamesInCompoundIndex: []
                }] });
                const record = key => ({ key, id: 'independent-' + key, value: 'payload' });
                const item = key => ({ dbName, storeName, record: record(key) });
                try {
                    await api.addItem(item('add'));
                    await api.bulkAddItem(dbName, storeName, [record('bulk-add')]);
                    await api.putItem(item('put'));
                    await api.bulkPutItems([item('bulk-put')]);
                    await api.bulkUpdateItem([item('bulk-update')]);
                    const rows = await api.toArray(dbName, storeName);
                    if (rows.length !== 5 || rows.some(row => row.id !== 'independent-' + row.key))
                        throw new Error('A writer overwrote a non-key id property.');
                    const input = record('input');
                    await api.addItem({ dbName, storeName, record: input });
                    if (input.id !== 'independent-input') throw new Error('A writer mutated the input.');
                } finally {
                    await api.deleteDb(dbName);
                }
            }
            """);
    }

    [TestMethod]
    public async Task Writers_PreserveCompoundKeysContainingId()
    {
        var page = await NewPageAsync();
        await page.EvaluateAsync("""
            async () => {
                const api = await import('/_content/Magic.IndexedDb/magicDB.js');
                const dbName = 'compound-write-contract-' + crypto.randomUUID();
                const storeName = 'rows';
                await api.createDb({ name: dbName, version: 1, storeSchemas: [{
                    tableName: storeName, columnNamesInCompoundKey: ['id', 'tenant'],
                    primaryKeyAuto: false, indexes: [], uniqueIndexes: [], columnNamesInCompoundIndex: []
                }] });
                const record = id => ({ id, tenant: 'tenant', value: 'payload' });
                const item = id => ({ dbName, storeName, record: record(id) });
                try {
                    await api.addItem(item('add'));
                    await api.bulkAddItem(dbName, storeName, [record('bulk-add')]);
                    await api.putItem(item('put'));
                    await api.bulkPutItems([item('bulk-put')]);
                    await api.bulkUpdateItem([item('bulk-update')]);
                    for (const id of ['add', 'bulk-add', 'put', 'bulk-put', 'bulk-update']) {
                        const row = await api.findItem(dbName, storeName, [id, 'tenant']);
                        if (!row || row.id !== id || row.tenant !== 'tenant')
                            throw new Error('A writer changed the compound primary key.');
                    }
                } finally {
                    await api.deleteDb(dbName);
                }
            }
            """);
    }

    [TestMethod]
    public async Task KeyMetadata_DoesNotCollideAcrossDatabaseAndTableNames()
    {
        var page = await NewPageAsync();
        await page.EvaluateAsync("""
            async () => {
                const api = await import('/_content/Magic.IndexedDb/magicDB.js');
                const first = 'key-cache-contract-' + crypto.randomUUID();
                const second = first + '.table';
                const configure = (name, tableName, key) => api.createDb({ name, version: 1, storeSchemas: [{
                    tableName, columnNamesInCompoundKey: [key], primaryKeyAuto: false,
                    indexes: [], uniqueIndexes: [], columnNamesInCompoundIndex: []
                }] });
                await configure(first, 'table.rows', 'firstKey');
                await configure(second, 'rows', 'secondKey');
                try {
                    await api.addItem({ dbName: first, storeName: 'table.rows', record: { firstKey: 'first' } });
                    await api.updateItem({ dbName: first, storeName: 'table.rows', record: { firstKey: 'first', value: 'warm metadata' } });
                    await api.addItem({ dbName: second, storeName: 'rows', record: { secondKey: 'second', value: 'old' } });
                    const updated = await api.updateItem({
                        dbName: second, storeName: 'rows', record: { secondKey: 'second', value: 'new' }
                    });
                    if (updated !== 1 || (await api.findItem(second, 'rows', 'second')).value !== 'new')
                        throw new Error('A database used another table\'s key metadata.');
                    await api.deleteItem({ dbName: first, storeName: 'table.rows', record: { firstKey: 'first' } });
                    if (await api.countTable(first, 'table.rows') !== 0)
                        throw new Error('Delete used another table\'s key metadata.');
                } finally {
                    await api.deleteDb(first);
                    await api.deleteDb(second);
                }
            }
            """);
    }
}
