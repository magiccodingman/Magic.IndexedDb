using Magic.IndexedDb;
using Magic.IndexedDb.SchemaAnnotations;

namespace E2eTestWebApp.Models;

public class Person : MagicTableTool<Person>, IMagicTable<Person.DbSets>
{
    public int Id { get; set; }

    public IMagicCompoundKey GetKeys() =>
        CreatePrimaryKey(x => x.Id, true); // Auto-incrementing primary key

    public List<IMagicCompoundIndex>? GetCompoundIndexes() => [];

    public string GetTableName() => "Person";
    public IndexedDbSet GetDefaultDatabase() => IndexedDbContext.Person;

    public DbSets Databases { get; } = new();
    public sealed class DbSets
    {
        public readonly IndexedDbSet Person = IndexedDbContext.Person;
    }

    [MagicIndex] // Creates an index on this field
    public string Name { get; set; }

    [MagicUniqueIndex("guid")] // Unique constraint
    public Guid UniqueGuid { get; set; } = Guid.NewGuid();

    public int Age { get; set; }

    [MagicNotMapped] // Exclude from IndexedDB schema
    public string Secret { get; set; }
}