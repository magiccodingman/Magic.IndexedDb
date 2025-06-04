namespace Magic.IndexedDb.Models;

public struct MagicTableEntry
{

    /// <summary>
    /// Property with the, "MagicTableAttribute" attribute appended
    /// </summary>
    public bool IsIndexDbTable { get; }

    public MagicTableEntry(bool isIndexDbTable)
    {
        IsIndexDbTable = isIndexDbTable;
    }
}