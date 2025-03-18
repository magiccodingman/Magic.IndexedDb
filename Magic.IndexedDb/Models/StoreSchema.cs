namespace Magic.IndexedDb
{   
    public class StoreSchema
    {
        public string TableName { get; set; }

        public int Version { get; set; }

        /// <summary>
        /// will the primary key automatically increment?
        /// </summary>
        public bool PrimaryKeyAuto { get; set; }

        /// <summary>
        /// IndexDB column names that will be provided a unique index
        /// </summary>
        public List<string> UniqueIndexes { get; set; } = new List<string>();

        /// <summary>
        /// IndexDB column names that will be automatically indexed.
        /// </summary>
        public List<string> Indexes { get; set; } = new List<string>();
        public List<List<string>> ColumnNamesInCompoundIndex { get; set; } = new List<List<string>>();
        public List<string> ColumnNamesInCompoundKey { get; set; } = new List<string>();
    }
}
