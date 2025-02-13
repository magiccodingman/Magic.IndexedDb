namespace Magic.IndexedDb
{
    public interface IMagicDbFactory
    {
        Task<IndexedDbManager> GetDbManagerAsync(string dbName);
        ValueTask<IndexedDbManager> OpenAsync(DbStore dbStore, bool force = false, CancellationToken cancellationToken = default);
    }
}