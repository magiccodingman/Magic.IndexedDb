using Magic.IndexedDb;
using Magic.IndexedDb.SchemaAnnotations;
using IndexDb.Example.Repository;

namespace IndexDb.Example;

public class Person : MagicTableTool<Person>, IMagicTable<Person.DbSets>
{
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];

    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(x => x._Id, true);

    public string GetTableName() => "Person";

    public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;

    public DbSets Databases { get; } = new();

    public sealed class DbSets
    {
        public readonly IndexedDbSet Client = IndexDbContext.Client;
    }

    [MagicName("id")]
    public int _Id { get; set; }

    [MagicIndex]
    public string Name { get; set; } = string.Empty;

    [MagicIndex("Age")]
    public int _Age { get; set; }

    [MagicIndex]
    public int TestInt { get; set; }

    [MagicUniqueIndex("guid")]
    public Guid GUIY { get; set; } = Guid.NewGuid();

    public string Notes { get; set; } = string.Empty;

    [MagicNotMapped]
    public string DoNotMapTest { get; set; } = string.Empty;

    public bool GetTest() => true;

    [Flags]
    public enum Permissions
    {
        None = 0,
        CanRead = 1,
        CanWrite = 1 << 1,
        CanDelete = 1 << 2,
        CanCreate = 1 << 3
    }

    public Permissions Access { get; set; }
}
