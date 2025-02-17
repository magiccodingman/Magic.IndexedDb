namespace Magic.IndexedDb.Models
{
    public class IndexFilterValue
    {
        public IndexFilterValue(string indexName, object filterValue)
        {
            this.IndexName = indexName;
            this.FilterValue = filterValue;
        }

        public string IndexName { get; set; }
        public object FilterValue { get; set; }
    }
}
