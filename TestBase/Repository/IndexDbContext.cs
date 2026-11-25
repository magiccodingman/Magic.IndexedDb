using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;
using TestBase.Models;

namespace TestBase.Repository;

public class IndexDbContext : IMagicRepository
{
    public static readonly IndexedDbSet Client = new ("Client");
    public static readonly IndexedDbSet Employee = new IndexedDbSet("Employee").Include<Person>();
    public static readonly IndexedDbSet Animal = new ("Animal");
}
