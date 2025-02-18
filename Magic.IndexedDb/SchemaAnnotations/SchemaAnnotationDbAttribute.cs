namespace Magic.IndexedDb
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MagicTableAttribute : Attribute
    {
        public string SchemaName { get; }
        public string? DatabaseName { get; }

        public MagicTableAttribute(string schemaName, string? databaseName)
        {
            this.SchemaName = schemaName;
            this.DatabaseName = databaseName;
        }
    }
}
