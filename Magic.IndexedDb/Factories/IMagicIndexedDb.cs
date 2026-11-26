using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.LinqTranslation.Interfaces;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.Migrations;

namespace Magic.IndexedDb;

public interface IMagicIndexedDb
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
    //ValueTask<IMagicQuery<T>> QueryOverride<T>(string? databaseNameOverride = null, 
    //    string? schemaNameOverride = null) where T : class, IMagicTableBase, new();

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
    //ValueTask<IMagicQuery<T>> Query<T>(IndexedDbSet indexedDbSet)
    //                where T : class, IMagicTableBase, new();

    // I think this should be under a utilities functionality?
    Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default);

    ValueTask<IMagicDatabaseScoped> Database(IndexedDbSet indexedDbSet);

    /// <summary>Uses a registered schema with a separately named physical database instance.</summary>
    /// <remarks>Protected databases use account-bound encryption contexts instead.</remarks>
    ValueTask<IMagicDatabaseScoped> Database(IndexedDbSet indexedDbSet, string instanceName);

    /// <summary>Observes metadata-only migration events. Observers never decide transaction outcomes.</summary>
    Task<IAsyncDisposable> ObserveMigrationsAsync(Func<MagicMigrationEvent, ValueTask> observer,
        CancellationToken cancellationToken = default);
}
