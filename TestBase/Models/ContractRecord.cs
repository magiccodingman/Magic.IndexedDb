using System.Text.Json.Serialization;
using Magic.IndexedDb;
using Magic.IndexedDb.SchemaAnnotations;
using TestBase.Repository;

namespace TestBase.Models;

public class ContractRecord : MagicTableTool<ContractRecord>, IMagicTable<Person.DbSets>
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Dictionary<string, object?> Metadata { get; set; } = [];

    [MagicIndex]
    public NumericStatus NumericAccess { get; set; }

    [MagicIndex]
    public NamedStatus NamedAccess { get; set; }

    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];

    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(x => x.Id, true);

    public string GetTableName() => "ContractRecord";

    public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;

    public Person.DbSets Databases { get; } = new();

    public enum NumericStatus
    {
        None = 0,
        Read = 1,
        Write = 2
    }

    [JsonConverter(typeof(JsonStringEnumConverter<NamedStatus>))]
    public enum NamedStatus
    {
        Inactive = 0,
        Active = 1
    }
}
