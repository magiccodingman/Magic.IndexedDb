using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb
{
    public interface IMagicDbFactory
    {

        /// <summary>
        /// Opens a ready query to utilize IndexDB database and capabilities utilizing LINQ to IndexDB. 
        /// Use example: IMagicQuery<Person> query = await _MagicDb.Query<Person>();
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="databaseNameOverride"></param>
        /// <param name="schemaNameOverride"></param>
        /// <returns></returns>
        ValueTask<IMagicQuery<T>> Query<T>(string? databaseNameOverride = null, string? schemaNameOverride = null) where T : class;
        Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default);
    }
}