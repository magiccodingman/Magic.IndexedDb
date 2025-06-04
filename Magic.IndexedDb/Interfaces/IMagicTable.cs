using Magic.IndexedDb.Interfaces;

namespace Magic.IndexedDb;

public interface IMagicTable<TDbSets> : IMagicTableBase
{
    TDbSets Databases { get; } // Enforce that every model has a `DbSets` instance
}