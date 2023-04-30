namespace Magic.IndexedDb
{
    public class UpdateRecord<T> : StoreRecord<T>
    {
        public object Key { get; set; }
    }
}