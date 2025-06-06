using Magic.IndexedDb.Helpers;

namespace Magic.IndexedDb;

public class UpdateRecord<T> : StoreRecord<T>
{
    public List<PrimaryKeys> Key { get; set; }
}