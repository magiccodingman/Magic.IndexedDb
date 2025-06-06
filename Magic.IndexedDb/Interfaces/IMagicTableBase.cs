namespace Magic.IndexedDb.Interfaces;

public interface IMagicTableBase
{
    string GetTableName();

    List<IMagicCompoundIndex>? GetCompoundIndexes();

    IMagicCompoundKey GetKeys();

    // <summary>
    /// Set the default database most commonly utilized for this table.
    /// </summary>
    IndexedDbSet GetDefaultDatabase();
}