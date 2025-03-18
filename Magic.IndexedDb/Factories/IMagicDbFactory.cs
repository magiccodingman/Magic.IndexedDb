using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb
{
    public interface IMagicDbFactory
    {

        /// <summary>
        /// Allows manually inserting database names and schema names via strings. 
        /// Please be careful, Magic IndexDB can't protect you from potential issues 
        /// if you use this.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="databaseNameOverride"></param>
        /// <param name="schemaNameOverride"></param>
        /// <returns></returns>
        ValueTask<IMagicQuery<T>> QueryOverride<T>(string? databaseNameOverride = null, 
            string? schemaNameOverride = null) where T : class, IMagicTableBase, new();

        /// <summary>
        /// Opens a ready query to utilize IndexDB database and capabilities utilizing LINQ to IndexDB. 
        /// Use example: IMagicQuery<Person> query = await _MagicDb.Query<Person>();
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="databaseNameOverride"></param>
        /// <param name="schemaNameOverride"></param>
        /// <returns></returns>
        ValueTask<IMagicQuery<T>> Query<T>()
                        where T : class, IMagicTableBase, new();

        /// <summary>
        /// Opens a query for a table to a specified database.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dbSetSelector"></param>
        /// <returns></returns>
        ValueTask<IMagicQuery<T>> Query<T>(Func<T, IndexedDbSet> dbSetSelector) where T : class, IMagicTableBase, new();

        /// <summary>
        /// Utilize any Database you want, but be careful that it's assigned! 
        /// Highly suggested you utilize `Query<T>(Func<T, IndexedDbSet> dbSetSelector)`
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="indexedDbSet"></param>
        /// <returns></returns>
        ValueTask<IMagicQuery<T>> Query<T>(IndexedDbSet indexedDbSet)
                        where T : class, IMagicTableBase, new();

        Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default);
    }
}