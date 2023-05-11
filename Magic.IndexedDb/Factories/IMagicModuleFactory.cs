using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public interface IMagicModuleFactory
    {
        Task<IndexedDbManager> GetDbManager(string dbName);

        Task<IndexedDbManager> GetDbManager(DbStore dbStore);
    }
}