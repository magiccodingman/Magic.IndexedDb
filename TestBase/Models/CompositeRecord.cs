using Magic.IndexedDb;
using Magic.IndexedDb.SchemaAnnotations;
using TestBase.Repository;

namespace TestBase.Models;

public sealed class CompositeRecord : MagicTableTool<CompositeRecord>, IMagicTable<Person.DbSets>
{
    public string Tenant { get; set; } = string.Empty;
    public int Sequence { get; set; }

    [MagicIndex]
    public string Category { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public IMagicCompoundKey GetKeys() => CreateCompoundKey(
        record => record.Tenant,
        record => record.Sequence);

    public List<IMagicCompoundIndex> GetCompoundIndexes() =>
    [
        CreateCompoundIndex(record => record.Tenant, record => record.Category)
    ];

    public string GetTableName() => "CompositeRecord";
    public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
    public Person.DbSets Databases { get; } = new();
}
