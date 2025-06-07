using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;

namespace E2eTestWebApp.Models;

public class IndexedDbContext : IMagicRepository
{
    public static readonly IndexedDbSet Person = new("Person");
}