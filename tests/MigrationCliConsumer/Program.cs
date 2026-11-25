using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Migrations;
using Magic.IndexedDb.SchemaAnnotations;

return MagicSchemaExporter.Run(args, [typeof(ConsumerRecord)], [typeof(ConsumerDatabase)]);

public sealed class ConsumerDatabase : IMagicRepository
{
    public static readonly IndexedDbSet Main = new("consumer-fixture");
}

public sealed class ConsumerRecord : MagicTableTool<ConsumerRecord>, IMagicTable<ConsumerDatabase>
{
    public int Id { get; set; }
    [MagicIndex]
    public string Name { get; set; } = "";
    public ConsumerDatabase Databases { get; } = new();
    public string GetTableName() => "items";
    public IndexedDbSet GetDefaultDatabase() => ConsumerDatabase.Main;
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(item => item.Id, false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
}
