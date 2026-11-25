using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public sealed class SingleRecordBasicTestUniqueWrites : TestBase<OpenTestPage>
{
    [TestMethod]
    public Task FailedUniqueUpdate_PreservesTheOriginalAndUpdateOnlySemantics() => RunCaseAsync("""
        await seed();
        await expectConflict(() => api.updateItem(item({ id: 2, token: 'occupied', secret: 'rejected' })));
        await assertOriginals();
        const missing = await api.updateItem(item({ id: 99, token: 'missing', secret: 'missing' }));
        assert(missing === 0 && await api.countTable(dbName, 'rows') === 3,
            'An update of a missing key inserted a record or returned the wrong count.');
        const updated = await api.updateItem(item({ id: 2, token: 'accepted', secret: 'accepted' }));
        const row = await api.findItem(dbName, 'rows', 2);
        assert(updated === 1 && row.token === 'accepted' && row.secret === 'accepted' && row.retained === 'keep-2',
            'A later update failed or no longer preserved fields absent from the patch.');
        """);

    [TestMethod]
    public Task FailedUniquePut_PreservesTheOriginalAndAllowsLaterReplacement() => RunCaseAsync("""
        await seed();
        await expectConflict(() => api.putItem(item({ id: 2, token: 'occupied', secret: 'rejected' })));
        await assertOriginals();
        const key = await api.putItem(item({ id: 2, token: 'accepted', secret: 'accepted' }));
        const row = await api.findItem(dbName, 'rows', 2);
        assert(key === 2 && row.token === 'accepted' && row.secret === 'accepted' && !('retained' in row),
            'A later put changed its key return value or replacement semantics.');
        const insertedKey = await api.putItem(item({ id: 4, token: 'inserted', secret: 'inserted' }));
        assert(insertedKey === 4 && await api.countTable(dbName, 'rows') === 4,
            'Put no longer inserts a missing key.');
        """);

    [TestMethod]
    public Task FailedUniqueBatch_RollsBackEarlierReplacementsForBothBulkRoutes() => RunCaseAsync("""
        for (const method of ['bulkPutItems', 'bulkUpdateItem']) {
            await seed();
            await expectConflict(() => api[method]([
                item({ id: 2, token: 'would-have-succeeded', secret: 'changed-first' }),
                item({ id: 3, token: 'occupied', secret: 'rejected-second' })
            ]));
            await assertOriginals();
            const result = await api[method]([
                item({ id: 2, token: 'accepted-2', secret: 'accepted-2' }),
                item({ id: 3, token: 'accepted-3', secret: 'accepted-3' })
            ]);
            assert(result === (method === 'bulkPutItems' ? 3 : 2), 'The successful bulk return value changed.');
            for (const id of [2, 3]) {
                const row = await api.findItem(dbName, 'rows', id);
                assert(row.token === 'accepted-' + id && row.secret === 'accepted-' + id && !('retained' in row),
                    'A later successful batch failed to replace a record.');
            }
        }
        """);

    private async Task RunCaseAsync(string body)
    {
        await using var disposablePage = new DisposablePage(await NewPageAsync());
        await disposablePage.Page.EvaluateAsync("""
            async () => {
                const api = await import('/_content/Magic.IndexedDb/magicDB.js');
                const assert = (condition, message) => { if (!condition) throw new Error(message); };
                const expectConflict = async operation => {
                    try { await operation(); } catch { return; }
                    throw new Error('A conflicting unique write unexpectedly succeeded.');
                };
                for (const mode of ['legacy', 'lossless', 'protected']) {
                    const suffix = crypto.randomUUID();
                    const dbName = (mode === 'protected' ? '__magic_protected__:' :
                        mode === 'lossless' ? '__magic_numeric_v1__:' : 'unique-write-') + suffix;
                    const schema = {
                        tableName: 'rows', columnNamesInCompoundKey: ['id'], primaryKeyAuto: false,
                        indexes: [], uniqueIndexes: ['token'], columnNamesInCompoundIndex: [],
                        ...(mode === 'protected' ? { encryptedFields: ['secret'] } : {})
                    };
                    const configuration = { name: dbName, version: 1, storeSchemas: [schema],
                        ...(mode === 'lossless' ? {
                            numericStorage: { version: 1, mode: 'lossless' }, numericLegacyName: 'legacy-' + suffix
                        } : {}),
                        ...(mode === 'protected' ? { encryption: { account: 'unique-writes' } } : {})
                    };
                    const item = record => ({ dbName, storeName: 'rows', record });
                    const seed = async () => {
                        await api.clear(dbName, 'rows');
                        await api.bulkAddItem(dbName, 'rows', [
                            { id: 1, token: 'occupied', secret: 'blocker' },
                            { id: 2, token: 'original-2', secret: 'original-2', retained: 'keep-2' },
                            { id: 3, token: 'original-3', secret: 'original-3', retained: 'keep-3' }
                        ]);
                    };
                    const assertOriginals = async () => {
                        assert(await api.countTable(dbName, 'rows') === 3, mode + ': A rejected write changed the row count.');
                        for (const id of [2, 3]) {
                            const row = await api.findItem(dbName, 'rows', id);
                            assert(row && row.token === 'original-' + id && row.secret === 'original-' + id && row.retained === 'keep-' + id,
                                mode + ': A rejected write removed or changed an original record.');
                        }
                        const blocker = await api.findItem(dbName, 'rows', 1);
                        assert(blocker.token === 'occupied' && blocker.secret === 'blocker',
                            mode + ': A rejected write changed the conflicting record.');
                    };
                    try {
                        await api.createDb(configuration);
                        if (mode === 'protected') await api.unlockProtectedDatabase(dbName, 'key', new Uint8Array(32).fill(19), true);
            """ + body + """
                    } finally {
                        await api.deleteDb(dbName);
                    }
                }
            }
            """);
    }
}
