namespace Magic.IndexedDb
{
    public interface IMagicDbFactory
    {
        [Obsolete("Use OpenRegisteredAsync instead.")]
        Task<IndexedDbManager> GetDbManagerAsync(string dbName);
        [Obsolete("Use OpenRegisteredAsync instead.")]
        Task<IndexedDbManager> GetDbManagerAsync(DbStore dbStore);

        ValueTask<IndexedDbManager> OpenAsync(DbStore dbStore, bool force = false, CancellationToken cancellationToken = default);
        IndexedDbManager Get(string dbName);
        ValueTask<IndexedDbManager> GetRegisteredAsync(string dbName, CancellationToken cancellationToken = default);
    }
}