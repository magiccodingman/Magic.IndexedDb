using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;

namespace IndexDb.Example.Repository;

public class IndexDbContext : IMagicRepository
{
    public static readonly IndexedDbSet Client = new(DbNames.Client);
}
