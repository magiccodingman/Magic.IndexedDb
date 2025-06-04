namespace Magic.IndexedDb;

public class IndexedDbSet
{
    public string DatabaseName { get; }
    public IndexedDbSet(string databaseName)
    {
        DatabaseName = databaseName;
    }
}