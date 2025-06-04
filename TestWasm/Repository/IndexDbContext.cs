using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;

namespace TestWasm.Repository;

public class IndexDbContext : IMagicRepository
{
    public static readonly IndexedDbSet Client = new ("Client");
    public static readonly IndexedDbSet Employee = new ("Employee");
    public static readonly IndexedDbSet Animal = new ("Animal");
}