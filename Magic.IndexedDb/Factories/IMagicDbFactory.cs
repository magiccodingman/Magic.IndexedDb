using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public interface IMagicDbFactory
    {
        Task<IndexedDbManager> GetDbManagerAsync(string dbName);

        Task<IndexedDbManager> GetDbManagerAsync(DbStore dbStore);
    }
}