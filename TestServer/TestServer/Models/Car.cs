using Magic.IndexedDb;
using TestWasm.Repository;

namespace TestWasm.Models;

public class Car : MagicTableTool<Car>, IMagicTable<Person.DbSets>
{
    public DateTime Created { get; set; } = DateTime.UtcNow;
    
    public string Brand { get; set; } = string.Empty;

    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];

    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(x => x.Created, false);

    public string GetTableName() => "Car";
    public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
    public Person.DbSets Databases { get; } = new();
}