using Magic.IndexedDb;
using Magic.IndexedDb.SchemaAnnotations;
using TestBase.Repository;
using static TestBase.Models.Person;

namespace TestBase.Models;

public class Nested
{
    public string Value { get; set; } = "abc";
}


public class Person : MagicTableTool<Person>, IMagicTable<DbSets>
{
    public List<IMagicCompoundIndex> GetCompoundIndexes() =>
        new List<IMagicCompoundIndex>() {
            CreateCompoundIndex(x => x.TestIntStable2, x => x.Name)
        };

    // When using this, the e2e fails, but the Testserver succeeds
    /*public IMagicCompoundKey GetKeys() =>
        CreateCompoundKey(x => x.TestIntStable2, x => x.TestIntStable);*/

    // When using this, the e2e succeeds, but the Testserver fails when not in debug mode 🤯
    public IMagicCompoundKey GetKeys() =>
        CreatePrimaryKey(x => x._Id, true);

    public string GetTableName() => "Person";
    public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
    public DbSets Databases { get; } = new();
    public sealed class DbSets
    {
        public readonly IndexedDbSet Client = IndexDbContext.Client;
        public readonly IndexedDbSet Employee = IndexDbContext.Employee;
    }


    public int TestIntStable { get; set; }
    public int TestIntStable2 { get; set; } = 10;

    public Nested Nested { get; set; } = new Nested();

    [MagicName("_id")]
    public int _Id { get; set; }

    [MagicName("guid1")]
    public Guid Guid1 { get; set; } = new Guid();

    [MagicName("guid2")]
    public Guid Guid2 { get; set; } = new Guid();

    [MagicIndex]
    public string Name { get; set; }

    [MagicName("Age")]
    public int _Age { get; set; }

    [MagicIndex("TestInt")]
    public int TestInt { get; set; }

    public DateTime? DateOfBirth { get; set; }

    [MagicUniqueIndex("guid")]
    public Guid GUIY { get; set; } = Guid.NewGuid();
    public string Secret { get; set; }

    [MagicNotMapped]
    public string DoNotMapTest { get; set; }

    [MagicNotMapped]
    public static string DoNotMapTest2 { get; set; }

    [MagicNotMapped]
    public string SecretDecrypted { get; set; }

    private bool testPrivate { get; set; } = false;

    public bool GetTest()
    {
        return true;
    }

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