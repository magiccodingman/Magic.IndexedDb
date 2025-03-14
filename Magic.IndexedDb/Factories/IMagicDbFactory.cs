using Magic.IndexedDb.Interfaces;

namespace Magic.IndexedDb
{
    public interface IMagicDbFactory
    {
        ValueTask<IMagicManager> GetMagicManager();
    }
}